using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class DiagnosisTests
{
    private readonly TestApiFactory _factory;

    public DiagnosisTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task FailedExecution_GetsAutoDiagnosis()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var pagePath = WritePage();
        var testCase = await CreateWebCaseAsync(client, project.Id, new object[]
        {
            NavigateStep(pagePath),
            AssertTextStep("期望不存在的内容"),
        });

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);
        Assert.Equal(ExecutionStatus.Failed, detail.Status);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/executions/{created.Id}");
            detail = await response.Content.ReadFromJsonAsync<ExecutionDetailDto>();
            if (!string.IsNullOrEmpty(detail!.AIDiagnosis))
                break;
            await Task.Delay(250);
        }

        Assert.False(string.IsNullOrEmpty(detail!.AIDiagnosis), "终态后应自动完成 AI 诊断");
        Assert.Contains("断言失败", detail.AIDiagnosis);
        Assert.False(string.IsNullOrEmpty(detail.AISuggestedFix));
        Assert.True(detail.DiagnosisConfidence > 0);

        var list = await client.GetAsync($"/api/executions?testCaseId={testCase.Id}");
        var page = await list.Content.ReadFromJsonAsync<JsonElement>();
        var item = page.GetProperty("items")[0];
        Assert.True(item.TryGetProperty("aiDiagnosis", out var diagnosis), "列表摘要应包含 aiDiagnosis 字段");
        Assert.Equal(JsonValueKind.String, diagnosis.ValueKind);
        Assert.Contains("断言失败", diagnosis.GetString());
    }

    [Fact]
    public async Task ManualDiagnose_FailedReturnsTrue_PassedReturnsFalse()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var pagePath = WritePage();

        var failedCase = await CreateWebCaseAsync(client, project.Id, new object[]
        {
            NavigateStep(pagePath),
            AssertTextStep("期望不存在的内容"),
        });
        var failedDetail = await RunExecutionAsync(client, failedCase.Id);
        Assert.Equal(ExecutionStatus.Failed, failedDetail.Status);

        var diagnoseFailed = await client.PostAsync($"/api/executions/{failedDetail.Id}/diagnose", null);
        Assert.Equal(HttpStatusCode.OK, diagnoseFailed.StatusCode);
        var failedResult = await diagnoseFailed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(failedResult.GetProperty("diagnosed").GetBoolean());

        var passedCase = await CreateWebCaseAsync(client, project.Id, new object[] { NavigateStep(pagePath) });
        var passedDetail = await RunExecutionAsync(client, passedCase.Id);
        Assert.Equal(ExecutionStatus.Passed, passedDetail.Status);

        var diagnosePassed = await client.PostAsync($"/api/executions/{passedDetail.Id}/diagnose", null);
        Assert.Equal(HttpStatusCode.OK, diagnosePassed.StatusCode);
        var passedResult = await diagnosePassed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(passedResult.GetProperty("diagnosed").GetBoolean());
    }

    private static object NavigateStep(string pageUrl) =>
        new { stepOrder = 0, actionType = 2, config = new { url = pageUrl }, aiInstruction = (string?)null, aiElementDescription = (string?)null };

    private static object AssertTextStep(string expected) =>
        new
        {
            stepOrder = 1,
            actionType = 12,
            config = new { selector = new { type = "css", value = "#status", description = (string?)null }, value = expected },
            aiInstruction = (string?)null,
            aiElementDescription = (string?)null,
        };

    private static async Task<ExecutionDetailDto> RunExecutionAsync(HttpClient client, Guid testCaseId)
    {
        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId });
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();
        return await PollUntilFinishedAsync(client, created!.Id);
    }

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
