using System.Net;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class SelfHealingExecutionTests
{
    private readonly TestApiFactory _factory;

    public SelfHealingExecutionTests(TestApiFactory factory)
    {
        _factory = factory;
        AIWorkerStubHandler.LocateResults.Clear();
        AIWorkerStubHandler.AssertResults.Clear();
    }

    [Fact]
    public async Task ExecuteWithAiSelector_LocatesAndPasses()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var pagePath = WritePage("登 录");
        var testCase = await CreateWebCaseAsync(client, project.Id, new object[]
        {
            new { stepOrder = 0, actionType = 2, config = new { url = pagePath }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            new { stepOrder = 1, actionType = 0, config = new { selector = new { type = "ai", value = (string?)null, description = "蓝色的登录按钮" }, value = (string?)null }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
        });

        var stub = _factory.Services.GetRequiredService<AIWorkerStubHandler>();
        var locateBaseline = stub.LocateCallCount;

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);
        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.True(stub.LocateCallCount > locateBaseline, "AI 定位未被调用");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var cached = await db.AIElementCaches
            .AnyAsync(e => e.ProjectId == project.Id && e.ElementDescription == "蓝色的登录按钮");
        Assert.True(cached, "AI 定位成功后应写入元素缓存");
    }

    [Fact]
    public async Task ExecuteWithWrongSelector_SelfHealsViaLlm()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var pagePath = WritePage("登 录");
        var testCase = await CreateWebCaseAsync(client, project.Id, new object[]
        {
            new { stepOrder = 0, actionType = 2, config = new { url = pagePath }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            new { stepOrder = 1, actionType = 0, config = new { selector = new { type = "css", value = "#not-exist", description = "蓝色的登录按钮" }, value = (string?)null }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
        });

        var stub = _factory.Services.GetRequiredService<AIWorkerStubHandler>();
        var locateBaseline = stub.LocateCallCount;

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);
        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.True(stub.LocateCallCount > locateBaseline, "自愈未触发 AI 定位");
    }

    [Fact]
    public async Task ExecuteAIAssert_Passes()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var pagePath = WritePage("登 录");
        var testCase = await CreateWebCaseAsync(client, project.Id, new object[]
        {
            new { stepOrder = 0, actionType = 2, config = new { url = pagePath }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            new { stepOrder = 1, actionType = 0, config = new { selector = new { type = "css", value = ".btn", description = (string?)null }, value = (string?)null }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            new { stepOrder = 2, actionType = 10, config = new { value = "登录后显示 ok" }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
        });

        var stub = _factory.Services.GetRequiredService<AIWorkerStubHandler>();
        stub.ResetAssertEvidenceShape();
        var assertBaseline = stub.AssertCallCount;

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);
        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.True(stub.AssertCallCount > assertBaseline, "AI 断言未被调用");
        Assert.True(stub.AssertEvidenceShapeChecked,
            "evidence 应以结构化对象传输（含 url/text 字段）");
    }

    [Fact]
    public async Task ExecuteWithCssSelector_SelfHealsPastStaleCache()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var pagePath = WritePage("登 录");
        var description = $"登录按钮-{Guid.NewGuid():N}";

        // 第一次执行：AI 定位写入缓存（按钮文本「登 录」的 xpath）
        var seedCase = await CreateWebCaseAsync(client, project.Id, new object[]
        {
            NavigateStep(pagePath),
            new { stepOrder = 1, actionType = 0, config = new { selector = new { type = "ai", value = (string?)null, description }, value = (string?)null }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
        }, timeout: 5000);
        var seedDetail = await RunExecutionAsync(client, seedCase.Id);
        Assert.Equal(ExecutionStatus.Passed, seedDetail.Status);

        // 页面变化：缓存 xpath 失效
        RewritePage(pagePath, "Sign In");

        var staleCase = await CreateWebCaseAsync(client, project.Id, new object[]
        {
            NavigateStep(pagePath),
            new { stepOrder = 1, actionType = 0, config = new { selector = new { type = "css", value = "#not-exist", description }, value = (string?)null }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
        }, timeout: 5000);

        var stub = _factory.Services.GetRequiredService<AIWorkerStubHandler>();
        var locateBaseline = stub.LocateCallCount;

        var detail = await RunExecutionAsync(client, staleCase.Id);
        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.True(stub.LocateCallCount > locateBaseline, "陈旧缓存失败后应跳过缓存重新 LLM 定位");
    }

    [Fact]
    public async Task ExecuteWithAiSelector_RecoversFromStaleCache()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var pagePath = WritePage("登 录");
        var description = $"登录按钮-{Guid.NewGuid():N}";

        var testCase = await CreateWebCaseAsync(client, project.Id, new object[]
        {
            NavigateStep(pagePath),
            new { stepOrder = 1, actionType = 0, config = new { selector = new { type = "ai", value = (string?)null, description }, value = (string?)null }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
        }, timeout: 5000);

        // 第一次执行：AI 定位成功并写入缓存
        var firstDetail = await RunExecutionAsync(client, testCase.Id);
        Assert.Equal(ExecutionStatus.Passed, firstDetail.Status);

        // 页面变化：缓存 xpath 失效
        RewritePage(pagePath, "Sign In");

        var stub = _factory.Services.GetRequiredService<AIWorkerStubHandler>();
        var locateBaseline = stub.LocateCallCount;

        // 第二次执行：缓存命中失败 → skipCache 重新 LLM 定位 → 覆盖写缓存
        var secondDetail = await RunExecutionAsync(client, testCase.Id);
        Assert.Equal(ExecutionStatus.Passed, secondDetail.Status);
        Assert.True(stub.LocateCallCount > locateBaseline, "缓存失效后应跳过缓存重新 LLM 定位");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var cacheEntry = await db.AIElementCaches
            .SingleAsync(e => e.ProjectId == project.Id && e.ElementDescription == description);
        Assert.True(cacheEntry.SelectorHistory.Count >= 2, "自愈后应写入新的历史选择器");
        Assert.Contains("Sign In", cacheEntry.SelectorHistory.Last().Value);
    }

    private static object NavigateStep(string pageUrl) =>
        new { stepOrder = 0, actionType = 2, config = new { url = pageUrl }, aiInstruction = (string?)null, aiElementDescription = (string?)null };

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

    private static string WritePage(string buttonText)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "m4-testpages")).FullName;
        var path = Path.Combine(dir, $"page-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, PageHtml(buttonText));
        return new Uri(path).AbsoluteUri; // file:///...
    }

    private static void RewritePage(string pageUrl, string buttonText)
        => File.WriteAllText(new Uri(pageUrl).LocalPath, PageHtml(buttonText));

    private static string PageHtml(string buttonText) => $$"""
        <!doctype html><html><body>
          <input id="username" />
          <button class="btn" onclick="document.getElementById('username').value='ok'">{{buttonText}}</button>
        </body></html>
        """;

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private static async Task<TestCaseDto> CreateWebCaseAsync(
        HttpClient client, Guid projectId, object[] steps, int timeout = 15000)
    {
        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId,
            name = $"web-{Guid.NewGuid():N}",
            type = 0,
            description = (string?)null,
            browser = "chromium",
            timeout,
            retryCount = 0,
            steps,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestCaseDto>())!;
    }
}
