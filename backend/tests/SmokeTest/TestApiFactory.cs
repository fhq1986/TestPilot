using AI.TestPlatform.Api.AI;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AI.TestPlatform.IntegrationTests;

public class TestApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("swr.cn-north-4.myhuaweicloud.com/ddn-k8s/docker.io/pgvector/pgvector:pg18")
        .WithDatabase("ai_test_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _dbContainer.GetConnectionString());
        builder.UseSetting("Screenshots:Path", Path.Combine(Path.GetTempPath(), "m2-test-screenshots"));
        // 放开全局限流：本集合是 ICollectionFixture，所有测试类共享同一个宿主且都用 admin 登录，
        // 会全部命中同一个「用户分区」的 100 请求/分钟桶，导致大量测试拿到 429 而非被测状态码。
        builder.UseSetting("RateLimit:GlobalPermitLimit", "1000000");
        // 登录端点有独立的按 IP 限流（5 次/分钟），测试同宿主同 IP 且反复登录，同样放开
        builder.UseSetting("RateLimit:LoginPermitLimit", "1000000");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<AIWorkerStubHandler>();
            services.AddHttpClient<AIClient>(client =>
            {
                client.BaseAddress = new Uri("http://ai-worker-stub");
                client.Timeout = TimeSpan.FromSeconds(30);
            }).ConfigurePrimaryHttpMessageHandler<AIWorkerStubHandler>();
        });
    }

    public Task InitializeAsync() => _dbContainer.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }
}
