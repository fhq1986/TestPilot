using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class ApiExecutionTests
{
    private readonly TestApiFactory _factory;

    public ApiExecutionTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ExecuteApiCase_RequestExtractAssertChain_Passes()
    {
        using var server = new ApiTestServer();
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateApiCaseAsync(client, project.Id, server.BaseUrl, new object[]
        {
            new
            {
                stepOrder = 0,
                actionType = 6,
                config = new { method = "POST", endpoint = "/login", body = """{"username":"admin"}""" },
            },
            new
            {
                stepOrder = 1,
                actionType = 8,
                config = new { value = "token", endpoint = "$.data.token" },
            },
            new
            {
                stepOrder = 2,
                actionType = 6,
                config = new
                {
                    method = "GET",
                    endpoint = "/user",
                    headers = new[] { new { name = "X-Token", value = "{token}" } },
                },
            },
            new
            {
                stepOrder = 3,
                actionType = 7,
                config = new { value = "200", body = """{"data":{"name":"张三"}}""" },
            },
        });

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);

        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.Equal(4, detail.Results.Count);
        Assert.All(detail.Results, r => Assert.Equal(ExecutionStatus.Passed, r.Status));
        var requestLog = detail.Results.First(r => r.StepOrder == 0).Log;
        Assert.NotNull(requestLog);
        Assert.Contains("200", requestLog);
        Assert.Contains("tok-123", requestLog);
        Assert.Equal("变量提取成功", detail.Results.First(r => r.StepOrder == 1).Log);
        Assert.Equal("响应断言通过", detail.Results.First(r => r.StepOrder == 3).Log);
    }

    [Fact]
    public async Task ExecuteApiCase_AssertFailure_MarksFailed()
    {
        using var server = new ApiTestServer();
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateApiCaseAsync(client, project.Id, server.BaseUrl, new object[]
        {
            new { stepOrder = 0, actionType = 6, config = new { method = "GET", endpoint = "/user" } },
            new { stepOrder = 1, actionType = 7, config = new { value = "404" } },
        });

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);

        Assert.Equal(ExecutionStatus.Failed, detail.Status);
        var failed = detail.Results.First(r => r.StepOrder == 1);
        Assert.Equal(ExecutionStatus.Failed, failed.Status);
        Assert.NotNull(failed.ErrorMessage);
        Assert.Contains("状态码断言失败", failed.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteApiCase_ExtractMissingPath_Fails()
    {
        using var server = new ApiTestServer();
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateApiCaseAsync(client, project.Id, server.BaseUrl, new object[]
        {
            new { stepOrder = 0, actionType = 6, config = new { method = "POST", endpoint = "/login", body = """{"username":"admin"}""" } },
            new { stepOrder = 1, actionType = 8, config = new { value = "token", endpoint = "$.data.nope" } },
        });

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);

        Assert.Equal(ExecutionStatus.Failed, detail.Status);
        var failed = detail.Results.First(r => r.StepOrder == 1);
        Assert.Equal(ExecutionStatus.Failed, failed.Status);
        Assert.NotNull(failed.ErrorMessage);
        Assert.Contains("提取失败", failed.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteApiCase_FailedRequestInvalidatesLastResponse()
    {
        using var server = new ApiTestServer();
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var deadPort = GetDeadPort();
        var testCase = await CreateApiCaseAsync(client, project.Id, server.BaseUrl, new object[]
        {
            new
            {
                stepOrder = 0,
                actionType = 6,
                config = new { method = "GET", endpoint = "/user", headers = new[] { new { name = "X-Token", value = "tok-123" } } },
            },
            new
            {
                stepOrder = 1,
                actionType = 6,
                config = new { method = "GET", endpoint = $"http://127.0.0.1:{deadPort}/unreachable" },
            },
            new { stepOrder = 2, actionType = 7, config = new { value = "200", body = """{"data":{"name":"张三"}}""" } },
        });

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);

        Assert.Equal(ExecutionStatus.Failed, detail.Status);
        Assert.Equal(ExecutionStatus.Failed, detail.Results.First(r => r.StepOrder == 1).Status);
        var assertResult = detail.Results.First(r => r.StepOrder == 2);
        Assert.Equal(ExecutionStatus.Failed, assertResult.Status);
        Assert.NotNull(assertResult.ErrorMessage);
        Assert.Contains("AssertResponse 前必须有 Request 步骤", assertResult.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteApiCase_AssertInvalidStatusFormat_FriendlyError()
    {
        using var server = new ApiTestServer();
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateApiCaseAsync(client, project.Id, server.BaseUrl, new object[]
        {
            new { stepOrder = 0, actionType = 6, config = new { method = "GET", endpoint = "/user" } },
            new { stepOrder = 1, actionType = 7, config = new { value = "abc" } },
        });

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);

        Assert.Equal(ExecutionStatus.Failed, detail.Status);
        var failed = detail.Results.First(r => r.StepOrder == 1);
        Assert.Equal(ExecutionStatus.Failed, failed.Status);
        Assert.NotNull(failed.ErrorMessage);
        Assert.Contains("无效的期望状态码", failed.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteApiCase_AssertUppercaseRangeStatus_Passes()
    {
        using var server = new ApiTestServer();
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateApiCaseAsync(client, project.Id, server.BaseUrl, new object[]
        {
            new { stepOrder = 0, actionType = 6, config = new { method = "GET", endpoint = "/user" } },
            new { stepOrder = 1, actionType = 7, config = new { value = "4XX" } },
        });

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();

        var detail = await PollUntilFinishedAsync(client, created!.Id);

        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.All(detail.Results, r => Assert.Equal(ExecutionStatus.Passed, r.Status));
    }

    private static int GetDeadPort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    private static async Task<TestCaseDto> CreateApiCaseAsync(
        HttpClient client, Guid projectId, string baseUrl, object[] steps)
    {
        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId,
            name = $"api-{Guid.NewGuid():N}",
            type = 1,
            description = (string?)null,
            browser = (string?)null,
            timeout = 30000,
            retryCount = 0,
            steps,
            baseUrl,
        });
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<TestCaseDto>();
        Assert.NotNull(dto);
        Assert.Equal(baseUrl, dto!.BaseUrl);
        return dto;
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

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }
}

// 测试用临时 HTTP 服务：HttpListener 随机端口，提供 /login 与 /user 两个端点
internal sealed class ApiTestServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public ApiTestServer()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        Port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();

        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private static async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var res = ctx.Response;
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
                await reader.ReadToEndAsync();

            if (req.HttpMethod == "POST" && req.Url!.AbsolutePath == "/login")
                await WriteJsonAsync(res, 200, """{"code":0,"data":{"token":"tok-123"}}""");
            else if (req.HttpMethod == "GET" && req.Url!.AbsolutePath == "/user")
                await WriteJsonAsync(res, req.Headers["X-Token"] == "tok-123" ? 200 : 401,
                    req.Headers["X-Token"] == "tok-123"
                        ? """{"code":0,"data":{"name":"张三"}}"""
                        : """{"code":401}""");
            else
                await WriteJsonAsync(res, 404, """{"code":404}""");
        }
        catch
        {
            // 客户端断开等异常忽略，测试服务仅存活于单测试生命周期
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse res, int status, string json)
    {
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.OutputStream.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
    }
}
