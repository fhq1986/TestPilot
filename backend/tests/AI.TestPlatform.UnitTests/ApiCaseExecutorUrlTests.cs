using System.Net;
using System.Text;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// API 用例请求步骤的 URL 组装。
///
/// 回归目标（线上事故 + 集成测试红）：判定「endpoint 是不是绝对地址」时**只能认 http/https**。
/// 旧实现只问 <c>Uri.TryCreate(endpoint, UriKind.Absolute, out _)</c>，而 .NET 在 Unix（Linux 容器 / WSL）
/// 上会把以 "/" 开头的站内路径（如 "/login"）当成绝对 file 路径 → 判定为「已是绝对地址」→
/// 跳过 BaseUrl 拼接 → <see cref="HttpClient"/> 抛
/// "Either the request URI must be an absolute URI or BaseAddress must be set"。
/// 同一坑 <see cref="AI.TestPlatform.Application.ApiTesting.UrlResolver"/> 已修过一次，
/// 但 ApiCaseExecutor 自己又写了一份判断，于是又踩了一遍——这里用桩 handler 钉死行为。
/// </summary>
public class ApiCaseExecutorUrlTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"code\":0}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (ApiCaseExecutor Executor, CapturingHandler Handler) NewExecutor(string? baseUrl)
    {
        var handler = new CapturingHandler();
        return (new ApiCaseExecutor(new HttpClient(handler), baseUrl), handler);
    }

    private static TestStep RequestStep(string endpoint) => new()
    {
        Id = Guid.NewGuid(),
        StepOrder = 1,
        ActionType = ActionType.Request,
        Config = new StepConfig { Method = "GET", Endpoint = endpoint },
    };

    [Fact]
    public async Task 站内相对路径_拼上BaseUrl()
    {
        // 这条就是事故现场："/login" 在 Unix 上会被 Uri 判成绝对路径，绝不能原样返回
        var (executor, handler) = NewExecutor("http://127.0.0.1:1234");

        await executor.ExecuteRequestAsync(RequestStep("/login"), 5000, CancellationToken.None);

        Assert.Equal("http://127.0.0.1:1234/login", handler.LastUri!.ToString());
    }

    [Fact]
    public async Task 不带前导斜杠的相对路径_同样拼上BaseUrl()
    {
        var (executor, handler) = NewExecutor("http://127.0.0.1:1234");

        await executor.ExecuteRequestAsync(RequestStep("login"), 5000, CancellationToken.None);

        Assert.Equal("http://127.0.0.1:1234/login", handler.LastUri!.ToString());
    }

    [Fact]
    public async Task BaseUrl带尾斜杠_不产生双斜杠()
    {
        var (executor, handler) = NewExecutor("http://127.0.0.1:1234/");

        await executor.ExecuteRequestAsync(RequestStep("/login"), 5000, CancellationToken.None);

        Assert.Equal("http://127.0.0.1:1234/login", handler.LastUri!.ToString());
    }

    [Fact]
    public async Task 绝对http地址_原样使用不由BaseUrl改写()
    {
        var (executor, handler) = NewExecutor("http://127.0.0.1:1234");

        await executor.ExecuteRequestAsync(
            RequestStep("https://api.example.com/v1/users"), 5000, CancellationToken.None);

        Assert.Equal("https://api.example.com/v1/users", handler.LastUri!.ToString());
    }

    [Fact]
    public async Task 相对路径且未配BaseUrl_抛可读错误而不是交给HttpClient()
    {
        var (executor, _) = NewExecutor(null);

        var ex = await Assert.ThrowsAsync<StepExecutionException>(
            () => executor.ExecuteRequestAsync(RequestStep("/login"), 5000, CancellationToken.None));

        Assert.Contains("BaseUrl", ex.Message);
    }
}
