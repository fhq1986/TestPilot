using System.Collections.Concurrent;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Application.ApiTesting;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace AI.TestPlatform.Api.Mocks;

// WireMock 智能 Mock 管理：进程内实例 + 端口动态分配（9000-9999）+ 语义示例响应
public class MockService
{
    private readonly ConcurrentDictionary<Guid, WireMockServer> _servers = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SwaggerImporter _importer;
    private readonly ILogger<MockService> _logger;

    public MockService(
        IServiceScopeFactory scopeFactory,
        SwaggerImporter importer,
        ILogger<MockService> logger)
    {
        _scopeFactory = scopeFactory;
        _importer = importer;
        _logger = logger;
    }

    public async Task<MockDefinition> CreateAndStartAsync(
        Guid projectId, string name, string specJson, CancellationToken ct)
    {
        var (_, _, endpoints) = _importer.Parse(specJson);
        if (endpoints.Count == 0)
            throw new InvalidOperationException("未解析到任何端点");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var definition = new MockDefinition
        {
            ProjectId = projectId,
            Name = name,
            BasePath = "/",
            Status = MockStatus.Running,
            Spec = specJson,
        };
        db.MockDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);

        try
        {
            definition.Port = await StartFromDefinitionAsync(definition, ct);
            await db.SaveChangesAsync(ct);
            return definition;
        }
        catch (Exception ex)
        {
            definition.Status = MockStatus.Stopped;
            definition.Port = null;
            definition.OwnerNode = null;
            await db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Mock 启动失败: {Name}", name);
            throw;
        }
    }

    // 从已有定义启动：解析 Spec → 分配端口 → 注册 WireMock 实例，返回端口（CreateAndStartAsync 与 start 端点复用）
    public Task<int> StartFromDefinitionAsync(MockDefinition definition, CancellationToken ct)
    {
        var (_, _, endpoints) = _importer.Parse(definition.Spec);
        if (endpoints.Count == 0)
            throw new InvalidOperationException("未解析到任何端点");
        var port = FindFreePort();

        var server = WireMockServer.Start(new WireMockServerSettings
        {
            Port = port,
            Urls = new[] { $"http://localhost:{port}" },
            StartAdminInterface = false,
            Logger = new WireMock.Logging.WireMockNullLogger(),
        });
        try
        {
            foreach (var endpoint in endpoints)
            {
                var body = BuildResponseBody(endpoint);
                var successCode = endpoint.ResponseCodes.FirstOrDefault(c => c is >= 200 and < 300);
                var response = Response.Create()
                    .WithStatusCode(successCode > 0 ? successCode : 200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body);
                server.Given(BuildRequest(endpoint)).RespondWith(response);
            }
            _servers[definition.Id] = server;
            // 记录归属节点（多实例就绪）：启动清理据此按节点心跳判断死活，
            // 不再依赖回环探活——跨容器/跨机时探不到对方端口，会误杀
            definition.OwnerNode = ExecutionNodeRegistry.NodeName;
            return Task.FromResult(port);
        }
        catch
        {
            server.Stop();
            server.Dispose();
            throw;
        }
    }

    public async Task StopAsync(Guid id, CancellationToken ct)
    {
        if (_servers.TryRemove(id, out var server))
        {
            server.Stop();
            server.Dispose();
        }
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var definition = await db.MockDefinitions.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (definition is not null)
        {
            definition.Status = MockStatus.Stopped;
            definition.Port = null;
            definition.OwnerNode = null;
            await db.SaveChangesAsync(ct);
        }
    }

    public bool IsRunning(Guid id) => _servers.ContainsKey(id);

    private static IRequestBuilder BuildRequest(ApiEndpointSpec endpoint)
    {
        var path = System.Text.RegularExpressions.Regex.Replace(endpoint.Path, @"\{[^/]+\}", "*");
        return endpoint.Method switch
        {
            "GET" => Request.Create().WithPath(path).UsingGet(),
            "POST" => Request.Create().WithPath(path).UsingPost(),
            "PUT" => Request.Create().WithPath(path).UsingPut(),
            "DELETE" => Request.Create().WithPath(path).UsingDelete(),
            _ => Request.Create().WithPath(path).UsingAnyMethod(),
        };
    }

    private static string BuildResponseBody(ApiEndpointSpec endpoint)
    {
        var props = endpoint.RequestBody?.Properties;
        var obj = new System.Text.Json.Nodes.JsonObject();
        if (props is not null)
        {
            foreach (var (name, schema) in props)
            {
                var example = SemanticExampleGenerator.Generate(name, schema.Type);
                obj[name] = System.Text.Json.JsonSerializer.SerializeToNode(example);
            }
        }
        obj["code"] = 0;
        return obj.ToJsonString();
    }

    private static int FindFreePort()
    {
        var rnd = new Random();
        for (var i = 0; i < 30; i++)
        {
            var port = rnd.Next(9000, 10000);
            try
            {
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch
            {
                // 端口被占用，继续探测
            }
        }
        throw new InvalidOperationException("无法分配可用端口");
    }
}
