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
///    建库走 <c>TEMPLATE ai_test_pgvector_template</c>（见 <see cref="VectorTemplateDatabase"/>），
///    让测试库「出生即带 vector 扩展」——空库建出来会因扩展晚于首次连接而让整个进程解析不出 vector。
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

    /// <summary>
    /// 预装 pgvector 的模板库名。测试库用它做 `TEMPLATE` 创建。
    ///
    /// 为什么非要多一个模板库（2026-09-23 定案）：
    /// 空库建出来时**没有 vector 类型**（扩展本来由迁移创建），而应用的第一次连接
    /// （启动块里的 <c>Database.OpenConnectionAsync()</c>）发生在迁移之前 —— Npgsql 的类型目录
    /// 就此定格，之后整个进程都解析不出 vector，表现为池化向量列读写全报
    /// <c>Reading as 'System.Object' is not supported for fields having DataTypeName '-'</c>
    /// （重启进程恢复正常；换用已有的库也正常）。也就是说**光在迁移之后补建扩展救不回来**，
    /// 必须让测试库「一出生就带扩展」。TEMPLATE 创建是物理拷贝，连 pg_type 行一起带过来。
    /// 注：`TEST_PG_RECREATE=0` 且目标库是**旧的、没有 vector 扩展**的库时不受此修复覆盖，
    /// 让它重建一次即可（默认 RECREATE=1 就是重建）。
    /// </summary>
    private const string VectorTemplateDatabase = "ai_test_pgvector_template";

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
        {
            await EnsureVectorTemplateAsync(connection, connectionString);
            await ExecuteAsync(connection, $"CREATE DATABASE \"{database}\" TEMPLATE \"{VectorTemplateDatabase}\"");
        }
    }

    /// <summary>确保模板库存在且装有 pgvector（幂等；只为给测试库提供"出生即带扩展"的副本）。</summary>
    /// <param name="maintenance">已连到 <c>postgres</c> 维护库的连接（用于建库与存在性检查）。</param>
    /// <param name="originalConnectionString">原始连接串。**不要从 <paramref name="maintenance"/>.ConnectionString 派生**——
    /// 那样得到的串可能丢失密码，实测报 `No password has been provided but the backend requires one`。</param>
    private static async Task EnsureVectorTemplateAsync(NpgsqlConnection maintenance, string originalConnectionString)
    {
        if (await ScalarAsync(maintenance, "SELECT 1 FROM pg_database WHERE datname = @name", VectorTemplateDatabase) is null)
            await ExecuteAsync(maintenance, $"CREATE DATABASE \"{VectorTemplateDatabase}\"");

        // 每次都确认扩展在（幂等）：上一次跑到一半失败可能留下"库建了、扩展没装成"的模板，
        // 那种模板做出的测试库照样没有 vector。连进去查一下比假设便宜得多。
        var templateBuilder = new NpgsqlConnectionStringBuilder(originalConnectionString)
        {
            Database = VectorTemplateDatabase,
        };
        await using (var templateConnection = new NpgsqlConnection(templateBuilder.ConnectionString))
        {
            await templateConnection.OpenAsync();

            bool hasVector;
            await using (var check = templateConnection.CreateCommand())
            {
                check.CommandText = "SELECT 1 FROM pg_extension WHERE extname = 'vector'";
                hasVector = await check.ExecuteScalarAsync() is not null;
            }
            if (!hasVector)
            {
                await using var installVector = templateConnection.CreateCommand();
                installVector.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
                await installVector.ExecuteNonQueryAsync();
            }

            // 无论装没装都要关连接清池：留着会话会让
            // CREATE DATABASE ... TEMPLATE 报 "source database is being accessed by other users"
            await templateConnection.CloseAsync();
            NpgsqlConnection.ClearPool(templateConnection);
        }
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
