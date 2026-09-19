using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class WebhookTests
{
    private const string ValidToken = "dev-webhook-token-0123456789";

    private readonly TestApiFactory _factory;

    public WebhookTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Webhook_ValidToken_CreatesCIExecution_ThatRunsToCompletion()
    {
        var authClient = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(authClient);
        var pagePath = WritePage();
        var testCase = await CreateWebCaseAsync(authClient, project.Id, new[]
        {
            NavigateStep(pagePath),
        });

        var webhookClient = _factory.CreateClient();
        webhookClient.DefaultRequestHeaders.Add("X-Webhook-Token", ValidToken);
        var trigger = await webhookClient.PostAsJsonAsync("/api/webhooks/executions",
            new { testCaseIds = new[] { testCase.Id } });

        Assert.Equal(HttpStatusCode.Accepted, trigger.StatusCode);
        var payload = await trigger.Content.ReadFromJsonAsync<JsonElement>();
        // 迭代 B 起响应体是 CiTriggerResponse：展开后可能有多个执行（用例 × 浏览器 × 数据行），
        // 因此契约是 created 计数 + executions 列表，而不是早期的扁平 executionIds 数组。
        Assert.Equal(1, payload.GetProperty("created").GetInt32());
        var executions = payload.GetProperty("executions");
        Assert.Equal(1, executions.GetArrayLength());
        var executionId = executions[0].GetProperty("executionId").GetGuid();

        var detail = await PollUntilFinishedAsync(authClient, executionId);
        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.Equal(TriggerType.CIWebhook, detail.TriggerType);
    }

    [Fact]
    public async Task Webhook_InvalidToken_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Webhook-Token", "wrong-token");
        var response = await client.PostAsJsonAsync("/api/webhooks/executions",
            new { testCaseIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var noToken = await _factory.CreateClient().PostAsJsonAsync("/api/webhooks/executions",
            new { testCaseIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
    }

    [Fact]
    public async Task Webhook_InvalidTestCaseIds_Returns400()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Webhook-Token", ValidToken);
        var response = await client.PostAsJsonAsync("/api/webhooks/executions",
            new { testCaseIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static object NavigateStep(string pageUrl) =>
        new { stepOrder = 0, actionType = 2, config = new { url = pageUrl }, aiInstruction = (string?)null, aiElementDescription = (string?)null };

    private static async Task<ExecutionDetailDto> PollUntilFinishedAsync(HttpClient client, Guid executionId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            var detail = await client.GetAsync($"/api/executions/{executionId}");
            var dto = await detail.Content.ReadFromJsonAsync<ExecutionDetailDto>();
            if (dto!.Status is ExecutionStatus.Passed or ExecutionStatus.Failed or ExecutionStatus.Error)
                return dto;
            await Task.Delay(1000);
        }
        throw new TimeoutException($"执行 {executionId} 90 秒内未完成");
    }

    private static string WritePage()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "m4-testpages")).FullName;
        var path = Path.Combine(dir, $"page-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, "<!doctype html><html><body><div id=\"status\">ready</div></body></html>");
        return new Uri(path).AbsoluteUri; // file:///...
    }

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private static async Task<TestCaseDto> CreateWebCaseAsync(
        HttpClient client, Guid projectId, object[] steps)
    {
        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId,
            name = $"web-{Guid.NewGuid():N}",
            type = 0,
            description = (string?)null,
            browser = "chromium",
            timeout = 15000,
            retryCount = 0,
            steps,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestCaseDto>())!;
    }
}
