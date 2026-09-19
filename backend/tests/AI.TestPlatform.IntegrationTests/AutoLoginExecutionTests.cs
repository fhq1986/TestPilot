using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using AI.TestPlatform.Application.Environments;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class AutoLoginExecutionTests
{
    private readonly TestApiFactory _factory;

    public AutoLoginExecutionTests(TestApiFactory factory)
    {
        _factory = factory;
        AIWorkerStubHandler.LocateResults.Clear();
        AIWorkerStubHandler.AssertResults.Clear();
    }

    [Fact]
    public async Task AutoLogin_RelativeNavigate_Passes()
    {
        using var server = new LoginTestServer();
        ConfigureLoginStub();
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var environment = await CreateEnvironmentAsync(client, project.Id, server.BaseUrl, "correct", autoLogin: true);
        var testCase = await CreateRelativeCaseAsync(client, project.Id, "/main");

        var detail = await RunExecutionAsync(client, testCase.Id, environment.Id);

        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        var login = detail.Results.SingleOrDefault(r => r.StepOrder == -1);
        Assert.NotNull(login);
        Assert.Equal(ExecutionStatus.Passed, login!.Status);
        Assert.Equal("自动登录成功", login.Log);
        Assert.All(detail.Results.Where(r => r.StepOrder >= 0), r => Assert.Equal(ExecutionStatus.Passed, r.Status));
    }

    [Fact]
    public async Task AutoLogin_WrongPassword_FailsWithLoginError()
    {
        using var server = new LoginTestServer();
        ConfigureLoginStub();
        AIWorkerStubHandler.AssertResults["登录成功进入主页"] =
            "{\"passed\": false, \"reason\": \"仍在登录页\", \"confidence\": 0.9}";
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var environment = await CreateEnvironmentAsync(client, project.Id, server.BaseUrl, "wrong",
            autoLogin: true, indicator: "登录成功进入主页");
        var testCase = await CreateRelativeCaseAsync(client, project.Id, "/main");

        var detail = await RunExecutionAsync(client, testCase.Id, environment.Id);

        Assert.Equal(ExecutionStatus.Failed, detail.Status);
        var login = detail.Results.SingleOrDefault(r => r.StepOrder == -1);
        Assert.NotNull(login);
        Assert.Equal(ExecutionStatus.Error, login!.Status);
        Assert.NotNull(login.ErrorMessage);
        Assert.Contains("自动登录失败", login.ErrorMessage);
    }

    [Fact]
    public async Task AutoLoginDisabled_NoLoginPreface()
    {
        using var server = new LoginTestServer();
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var environment = await CreateEnvironmentAsync(client, project.Id, server.BaseUrl, "correct", autoLogin: false);
        var testCase = await CreateRelativeCaseAsync(client, project.Id, "/main");

        var detail = await RunExecutionAsync(client, testCase.Id, environment.Id);

        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.DoesNotContain(detail.Results, r => r.StepOrder == -1);
    }

    [Fact]
    public async Task NoEnvironment_AbsoluteUrl_Unchanged()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var pagePath = WriteLocalPage();
        var testCase = await CreateWebCaseAsync(client, project.Id, new object[]
        {
            new { stepOrder = 0, actionType = 2, config = new { url = pagePath }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            new { stepOrder = 1, actionType = 12, config = new { selector = new { type = "css", value = "body", description = (string?)null }, value = "hello" }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
        });

        var detail = await RunExecutionAsync(client, testCase.Id, null);

        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.DoesNotContain(detail.Results, r => r.StepOrder == -1);
    }

    [Fact]
    public async Task ExecuteWithEnvironment_DetailShowsEnvironmentName()
    {
        using var server = new LoginTestServer();
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        const string envName = "预发环境";
        var environment = await CreateEnvironmentAsync(client, project.Id, server.BaseUrl, "correct",
            autoLogin: false, name: envName);
        var testCase = await CreateRelativeCaseAsync(client, project.Id, "/main");

        var detail = await RunExecutionAsync(client, testCase.Id, environment.Id);

        Assert.Equal(ExecutionStatus.Passed, detail.Status);
        Assert.Equal(envName, detail.EnvironmentName);
    }

    private static void ConfigureLoginStub()
    {
        AIWorkerStubHandler.LocateResults["用户名输入框"] =
            "{\"matched_index\": 0, \"confidence\": 0.9, \"reasoning\": \"stub\"}";
        AIWorkerStubHandler.LocateResults["密码输入框"] =
            "{\"matched_index\": 1, \"confidence\": 0.9, \"reasoning\": \"stub\"}";
        AIWorkerStubHandler.LocateResults["登录按钮"] =
            "{\"matched_index\": 2, \"confidence\": 0.9, \"reasoning\": \"stub\"}";
    }

    private static async Task<ExecutionDetailDto> RunExecutionAsync(
        HttpClient client, Guid testCaseId, Guid? environmentId)
    {
        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId, environmentId });
        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            var detail = await client.GetAsync($"/api/executions/{created!.Id}");
            var dto = await detail.Content.ReadFromJsonAsync<ExecutionDetailDto>();
            if (dto!.Status is ExecutionStatus.Passed or ExecutionStatus.Failed or ExecutionStatus.Error)
                return dto;
            await Task.Delay(1000);
        }
        throw new TimeoutException($"执行 {created!.Id} 90 秒内未完成");
    }

    private static async Task<EnvironmentView> CreateEnvironmentAsync(
        HttpClient client, Guid projectId, string baseUrl, string password,
        bool autoLogin, string? indicator = null, string? name = null)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/environments", new
        {
            name = name ?? $"env-{Guid.NewGuid():N}",
            baseUrl,
            loginUrl = "/login",
            loginUsername = "admin",
            loginPassword = password,
            loginSuccessIndicator = indicator,
            autoLogin,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EnvironmentView>())!;
    }

    private static async Task<TestCaseDto> CreateRelativeCaseAsync(
        HttpClient client, Guid projectId, string navigateUrl)
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
                new { stepOrder = 0, actionType = 2, config = new { url = navigateUrl }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
                new { stepOrder = 1, actionType = 12, config = new { selector = new { type = "css", value = "body", description = (string?)null }, value = "欢迎" }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestCaseDto>())!;
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

    private static string WriteLocalPage()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "m5-testpages")).FullName;
        var path = Path.Combine(dir, $"page-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, """
            <!doctype html><html><body><h1>hello</h1></body></html>
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
}

// 测试用登录站点：HttpListener 随机端口，/login 登录页（用户名/密码输入框 + 登录按钮），/main 欢迎页
internal sealed class LoginTestServer : IDisposable
{
    private const string LoginPageHtml = """
        <!doctype html><html><body>
          <h1>登录</h1>
          <input id="username" type="text" />
          <input id="password" type="password" />
          <button id="login" onclick="if(document.getElementById('password').value==='correct'){location='/main'}else{location='/login?error=1'}">登录</button>
        </body></html>
        """;
    private const string MainPageHtml = """
        <!doctype html><html><body>
          <h1>欢迎</h1>
          <div>欢迎回来，已登录</div>
        </body></html>
        """;

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public LoginTestServer()
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

            if (req.HttpMethod == "GET" && req.Url!.AbsolutePath == "/login")
                await WriteHtmlAsync(res, LoginPageHtml);
            else if (req.HttpMethod == "GET" && req.Url!.AbsolutePath == "/main")
                await WriteHtmlAsync(res, MainPageHtml);
            else
            {
                res.StatusCode = 404;
                res.Close();
            }
        }
        catch
        {
            // 客户端断开等异常忽略，测试服务仅存活于单测试生命周期
        }
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse res, string html)
    {
        res.StatusCode = 200;
        res.ContentType = "text/html; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(html);
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
