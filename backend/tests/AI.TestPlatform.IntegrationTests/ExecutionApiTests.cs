using System.Net;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class ExecutionApiTests
{
    private readonly TestApiFactory _factory;

    public ExecutionApiTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ExecuteWebCase_PassesAndWritesScreenshot()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var pagePath = WriteTestPage();
        var testCase = await CreateWebCaseAsync(client, project.Id, pagePath, expected: "done");

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();
        Assert.NotNull(created);
        Assert.Equal(testCase.Id, created!.TestCaseId);

        var detail = await PollUntilFinishedAsync(client, created.Id);
        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.NotNull(detail.BrowserVersion);
        Assert.Equal(5, detail.Results.Count);
        Assert.All(detail.Results, r => Assert.Equal(ExecutionStatus.Passed, r.Status));
        Assert.Contains(detail.Results, r => r.ScreenshotUrl != null && r.ScreenshotUrl.Contains("/screenshots/"));

        // 截图文件已落盘（TestApiFactory 已将 Screenshots:Path 重定向到临时目录）
        var screenshotRel = detail.Results.First(r => r.ScreenshotUrl != null).ScreenshotUrl!;
        var fileName = screenshotRel[(screenshotRel.LastIndexOf('/') + 1)..];
        var expectedFile = Path.Combine(Path.GetTempPath(), "m2-test-screenshots", created.Id.ToString("N"), fileName);
        Assert.True(File.Exists(expectedFile), $"截图文件不存在: {expectedFile}");
    }

    [Fact]
    public async Task ExecuteWebCase_AssertFailure_MarksFailed()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var pagePath = WriteTestPage();
        var testCase = await CreateWebCaseAsync(client, project.Id, pagePath, expected: "wrong-text");

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);
        Assert.Equal(ExecutionStatus.Failed, detail.Status);
        Assert.Contains(detail.Results, r => r.Status == ExecutionStatus.Failed && r.ErrorMessage != null);
    }

    [Fact]
    public async Task ExecuteMobileCase_Returns400()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var apiCase = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId = project.Id,
            name = $"mobile-{Guid.NewGuid():N}",
            type = 2,
            description = (string?)null,
            browser = (string?)null,
            timeout = 30000,
            retryCount = 0,
            steps = new object[] { new { stepOrder = 0, actionType = 6, config = new { method = "GET", endpoint = "/ping" }, aiInstruction = (string?)null, aiElementDescription = (string?)null } },
        });
        var apiTestCase = await apiCase.Content.ReadFromJsonAsync<TestCaseDto>();

        var response = await client.PostAsJsonAsync("/api/executions", new { testCaseId = apiTestCase!.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    private static string WriteTestPage()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "m2-testpages")).FullName;
        var path = Path.Combine(dir, $"page-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, """
            <!doctype html>
            <html><body>
              <input id="name" type="text" />
              <button id="btn" onclick="document.getElementById('name').value='done'">Go</button>
            </body></html>
            """);
        return new Uri(path).AbsoluteUri; // file:///...
    }

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private static async Task<TestCaseDto> CreateWebCaseAsync(HttpClient client, Guid projectId, string pageUrl, string expected)
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
            steps = new object[]
            {
                new { stepOrder = 0, actionType = 2, config = new { url = pageUrl }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
                new { stepOrder = 1, actionType = 1, config = new { selector = new { type = "css", value = "#name", description = (string?)null }, value = "hello" }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
                new { stepOrder = 2, actionType = 0, config = new { selector = new { type = "css", value = "#btn", description = (string?)null }, value = (string?)null }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
                new { stepOrder = 3, actionType = 12, config = new { selector = new { type = "css", value = "#name", description = (string?)null }, value = expected }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
                new { stepOrder = 4, actionType = 4, config = new { }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestCaseDto>())!;
    }
}
