using AI.TestPlatform.Api.AI;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AI.TestPlatform.IntegrationTests;

/// <summary>
/// 集成测试宿主工厂。取库有两条路：
///
/// 1) **外部库**（环境变量 <c>TEST_PG_CONNECTION</c>）：直连既有 PostgreSQL，**不启容器**。
///    本机环境（WSL 内 docker 29 + Testcontainers 3.9.0）实测：Docker API 的 create/start 均返回成功、
///    容器内 PostgreSQL 也已 "ready to accept connections"，但 Testcontainers 的容器启动/就绪等待**永不返回**
///    （抓包显示请求停在 <c>POST /containers/{id}/start</c>，之后再无任何 Docker API 调用）。
///    离线 NuGet 源最高只有 Testcontainers 3.9.0，无法升级修复，故**默认走外部库**，保证集成测试能真跑。
///    可选 <c>TEST_PG_RECREATE=1</c>：先 DROP 再 CREATE 目标库（仅当库名含 "test"，避免误删）。
///
/// 2) **Testcontainers**（未设 <c>TEST_PG_CONNECTION</c>）：自起 PG 容器——
///    供 CI 或 Docker 与 Testcontainers 版本兼容的环境使用。
/// </summary>
public class TestApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer? _dbContainer;
    private string _connectionString = string.Empty;

    public TestApiFactory()
    {
        // 未显式指定外部库时才启容器
        if (string.IsNullOrWhiteSpace(ExternalConnection))
        {
            _dbContainer = new PostgreSqlBuilder()
                .WithImage("swr.cn-north-4.myhuaweicloud.com/ddn-k8s/docker.io/pgvector/pgvector:pg18")
                .WithDatabase("ai_test_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                // 显式用"外部映射端口"就绪判据（默认策略在本机挂起）
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
                .Build();
        }
    }

    private static string? ExternalConnection => Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
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

    public async Task InitializeAsync()
    {
        if (_dbContainer is not null)
        {
            await _dbContainer.StartAsync();
            _connectionString = _dbContainer.GetConnectionString();
            return;
        }

        _connectionString = ExternalConnection!;
        await EnsureExternalDatabaseAsync(_connectionString);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_dbContainer is not null)
            await _dbContainer.DisposeAsync();
    }

    /// <summary>确保外部库存在（可选重建）。连到维护库 <c>postgres</c> 执行 CREATE / DROP。</summary>
    private static async Task EnsureExternalDatabaseAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("TEST_PG_CONNECTION 未指定 Database（请指向专用测试库，如 ai_test_integration）");
        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        // 默认只"建库"；重建需显式开启，且库名必须含 test（防止误删开发库）
        if (Environment.GetEnvironmentVariable("TEST_PG_RECREATE") == "1" &&
            database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            // WITH (FORCE)：踢掉占用连接（PG13+）
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)");
        }

        var exists = await ScalarAsync(connection, "SELECT 1 FROM pg_database WHERE datname = @name", database);
        if (exists is null)
            await ExecuteAsync(connection, $"CREATE DATABASE \"{database}\"");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, string parameter)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("name", parameter);
        return await command.ExecuteScalarAsync();
    }
}
