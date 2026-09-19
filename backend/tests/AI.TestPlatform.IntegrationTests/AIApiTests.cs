using System.Net;
using System.Net.Http.Json;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class AIApiTests
{
    private readonly TestApiFactory _factory;

    public AIApiTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GenerateCases_ReturnsMappedCases()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/ai/generate-cases",
            new { requirement = "用户登录功能，支持用户名密码登录", minCases = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cases = await response.Content.ReadFromJsonAsync<List<GeneratedCaseDto>>();
        Assert.NotNull(cases);
        Assert.Equal(2, cases!.Count);
        Assert.Equal("登录成功", cases[0].Name);
        Assert.Equal("P0", cases[0].Priority);
        Assert.Equal(2, cases[0].Steps.Count);
        Assert.Equal("#username", cases[0].Steps[1].Config.Selector!.Value);
    }

    [Fact]
    public async Task GenerateCases_WorkerError_Returns502()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/ai/generate-cases",
            new { requirement = "这段需求会触发500错误响应", minCases = 3 });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task AnalyzeApiFlow_ReturnsCases()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/ai/analyze-api-flow", new
        {
            endpoints = new object[]
            {
                new { method = "POST", path = "/login", summary = "登录", response_codes = new[] { 200 } },
                new { method = "GET", path = "/user", summary = "查询当前用户", response_codes = new[] { 200 } },
            },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cases = await response.Content.ReadFromJsonAsync<List<GeneratedCaseDto>>();
        Assert.NotNull(cases);
        var flowCase = Assert.Single(cases!);
        Assert.Equal("登录后查询用户信息", flowCase.Name);
        Assert.Equal(TestType.Api, flowCase.Type);
        var requestStep = Assert.Single(flowCase.Steps, s => s.ActionType == ActionType.Request);
        Assert.Equal("POST", requestStep.Config.Method);
        Assert.Equal("/login", requestStep.Config.Endpoint);
        Assert.Contains("admin", requestStep.Config.Body);
        var extractStep = Assert.Single(flowCase.Steps, s => s.ActionType == ActionType.ExtractVariable);
        Assert.Equal("token", extractStep.Config.Value);
        Assert.Equal("$.data.token", extractStep.Config.Endpoint);
    }

    [Fact]
    public async Task AnalyzeApiFlow_HeadersWrapper_IsUnwrapped()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/ai/analyze-api-flow", new
        {
            endpoints = new object[]
            {
                new { method = "POST", path = "/wrapped-login", summary = "登录", response_codes = new[] { 200 } },
            },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cases = await response.Content.ReadFromJsonAsync<List<GeneratedCaseDto>>();
        Assert.NotNull(cases);
        var flowCase = Assert.Single(cases!);
        var requestStep = Assert.Single(flowCase.Steps, s => s.ActionType == ActionType.Request);
        Assert.NotNull(requestStep.Config.Headers);
        var tokenHeader = Assert.Single(requestStep.Config.Headers!, h => h.Name == "X-Token");
        Assert.Equal("{token}", tokenHeader.Value);
        Assert.NotNull(requestStep.Config.Body);
        Assert.Contains("\"id\":1", requestStep.Config.Body);
    }

    [Fact]
    public async Task AdoptCases_CreatesAIGeneratedTestCases()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync("/api/ai/adopt", new
        {
            projectId = project.Id,
            aiPrompt = "用户登录功能，支持用户名密码登录",
            cases = new object[]
            {
                new
                {
                    name = "登录成功",
                    priority = "P0",
                    type = 0,
                    steps = new object[]
                    {
                        new { stepOrder = 0, actionType = 2, config = new { url = "https://example.com/login" }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
                    },
                },
            },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<AdoptCasesResponse>();
        Assert.NotNull(result);
        Assert.Equal(1, result!.Created);
        Assert.Single(result.CaseIds);

        var detail = await client.GetAsync($"/api/testcases/{result.CaseIds[0]}");
        var dto = await detail.Content.ReadFromJsonAsync<TestCaseDto>();
        Assert.NotNull(dto);
        Assert.True(dto!.AIGenerated);
        Assert.Single(dto.Steps);
        Assert.Equal(TestCaseStatus.Active, dto.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var tc = await db.TestCases.SingleAsync(t => t.Id == result.CaseIds[0]);
        Assert.Equal("P0", tc.Priority);
        Assert.Equal(TestCaseStatus.Active, tc.Status);
    }

    [Fact]
    public async Task AdoptCases_UnknownProject_Returns400()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/ai/adopt", new
        {
            projectId = Guid.NewGuid(),
            aiPrompt = (string?)null,
            cases = new object[]
            {
                new { name = "x", priority = "P1", type = 0, steps = Array.Empty<object>() },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DiagnoseFailure_ReturnsMappedDiagnosis()
    {
        const string caseName = "诊断映射测试用例";
        AIWorkerStubHandler.DiagnoseResults[caseName] =
            "{\"category\":\"超时\",\"root_cause\":\"页面加载超时\",\"confidence\":0.72,\"suggested_fix\":\"增加等待时间\",\"retry_recommended\":true}";
        try
        {
            var aiClient = _factory.Services.GetRequiredService<AIClient>();
            var result = await aiClient.DiagnoseAsync(
                new Dictionary<string, object?> { ["name"] = caseName, ["type"] = "Web" },
                new List<FailedStepEvidence>
                {
                    new(1, "Wait", "等待元素超时", null, new StepConfig { Value = "1000" }),
                },
                new List<SimilarCaseEvidence>
                {
                    new(0, "历史", "等待元素超时"),
                }, CancellationToken.None);

            Assert.Equal("超时", result.Category);
            Assert.Equal("页面加载超时", result.RootCause);
            Assert.Equal(0.72f, result.Confidence);
            Assert.Equal("增加等待时间", result.SuggestedFix);
            Assert.True(result.RetryRecommended);
        }
        finally
        {
            AIWorkerStubHandler.DiagnoseResults.TryRemove(caseName, out _);
        }
    }

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }
}
