using System.Net;
using System.Net.Http.Json;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class SwaggerImportTests
{
    private const string SpecJson = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Test API", "version": "1.0.0" },
          "servers": [ { "url": "http://localhost:8080" } ],
          "paths": {
            "/login": {
              "post": {
                "summary": "用户登录",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "required": [ "username", "password" ],
                        "properties": {
                          "username": { "type": "string" },
                          "password": { "type": "string" }
                        }
                      }
                    }
                  }
                },
                "responses": {
                  "200": { "description": "ok" },
                  "401": { "description": "unauthorized" }
                }
              }
            },
            "/users/{id}": {
              "get": {
                "summary": "查询用户",
                "parameters": [
                  { "name": "id", "in": "path", "required": true,
                    "schema": { "type": "integer" } }
                ],
                "responses": {
                  "200": { "description": "ok" },
                  "404": { "description": "not found" }
                }
              }
            }
          }
        }
        """;

    private const string NullableTypeSpecJson = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Nullable API", "version": "1.0.0" },
          "paths": {
            "/users": {
              "post": {
                "summary": "创建用户",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "properties": {
                          "name": { "type": "string" },
                          "nickname": { "type": [ "string", "null" ] }
                        }
                      }
                    }
                  }
                },
                "responses": {
                  "201": { "description": "created" }
                }
              }
            }
          }
        }
        """;

    private const string EnumSpecJson = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Enum API", "version": "1.0.0" },
          "paths": {
            "/users": {
              "post": {
                "summary": "创建用户",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "required": [ "status" ],
                        "properties": {
                          "status": { "type": "string", "enum": [ "active", "disabled" ] }
                        }
                      }
                    }
                  }
                },
                "responses": {
                  "201": { "description": "created" }
                }
              }
            }
          }
        }
        """;

    private const string RecursiveSpecJson = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Recursive API", "version": "1.0.0" },
          "paths": {
            "/nodes": {
              "post": {
                "summary": "创建节点",
                "requestBody": {
                  "content": {
                    "application/json": {
                      "schema": { "$ref": "#/components/schemas/Node" }
                    }
                  }
                },
                "responses": { "201": { "description": "created" } }
              }
            }
          },
          "components": {
            "schemas": {
              "Node": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "children": {
                    "type": "array",
                    "items": { "$ref": "#/components/schemas/Node" }
                  }
                }
              }
            }
          }
        }
        """;

    private readonly TestApiFactory _factory;

    public SwaggerImportTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ImportSwagger_WithContent_GeneratesCases()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync("/api/ai/import-swagger",
            new { projectId = project.Id, content = SpecJson });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImportSwaggerResponse>();
        Assert.NotNull(result);
        Assert.Equal("Test API", result!.ApiName);
        Assert.Equal("http://localhost:8080", result.BaseUrl);
        Assert.Equal(2, result.EndpointCount);
        Assert.True(result.GeneratedCases >= 2, $"GeneratedCases={result.GeneratedCases}");
        Assert.Equal(result.GeneratedCases, result.Cases.Count);
        Assert.NotEmpty(result.EndpointSummaries);
        Assert.All(result.Cases, c =>
        {
            Assert.Contains(c.Steps, s => s.ActionType == ActionType.Request);
            Assert.Contains(c.Steps, s => s.ActionType == ActionType.AssertResponse);
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var definition = await db.ApiDefinitions.SingleAsync(a => a.Id == result.ApiDefinitionId);
        Assert.Equal(SpecJson, definition.Spec);
        Assert.Equal(2, definition.EndpointCount);
        Assert.Equal(project.Id, definition.ProjectId);
    }

    [Fact]
    public async Task ImportSwagger_InvalidJson_Returns400()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync("/api/ai/import-swagger",
            new { projectId = project.Id, content = "{ not valid json" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportSwagger_UnknownProject_Returns400()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/ai/import-swagger",
            new { projectId = Guid.NewGuid(), content = SpecJson });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportSwagger_WithNullableType_Succeeds()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync("/api/ai/import-swagger",
            new { projectId = project.Id, content = NullableTypeSpecJson });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImportSwaggerResponse>();
        Assert.NotNull(result);
        Assert.True(result!.GeneratedCases >= 1);
    }

    [Fact]
    public async Task ImportSwagger_WithEnum_ProducesEnumCases()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync("/api/ai/import-swagger",
            new { projectId = project.Id, content = EnumSpecJson });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("OpenApi", json);
        Assert.Contains("active", json);
        Assert.Contains("INVALID_ENUM_VALUE", json);
    }

    [Fact]
    public async Task ImportSwagger_WithRecursiveSchema_DoesNotOverflow()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync("/api/ai/import-swagger",
            new { projectId = project.Id, content = RecursiveSpecJson });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImportSwaggerResponse>();
        Assert.NotNull(result);
        Assert.True(result!.GeneratedCases >= 1);
    }

    [Fact]
    public async Task ImportSwagger_PrivateUrl_BlockedOnlyWhenDisabled()
    {
        // 该用例需要开关 /api/settings（需 ManageSettings，仅 SuperAdmin）→ 用超管客户端（Task #122）
        var client = await SuperAdminClient.CreateAsync(_factory);
        var project = await CreateProjectAsync(client);
        var url = $"http://127.0.0.1:{GetDeadPort()}/x";

        try
        {
            var disable = await client.PutAsJsonAsync("/api/settings",
                new { allowPrivateNetworkImport = false });
            Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

            var blocked = await client.PostAsJsonAsync("/api/ai/import-swagger",
                new { projectId = project.Id, url });
            Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
            var blockedJson = await blocked.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Contains("内网", blockedJson.GetProperty("message").GetString());

            var enable = await client.PutAsJsonAsync("/api/settings",
                new { allowPrivateNetworkImport = true });
            Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

            var allowed = await client.PostAsJsonAsync("/api/ai/import-swagger",
                new { projectId = project.Id, url });
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
            var allowedJson = await allowed.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.DoesNotContain("内网", allowedJson.GetProperty("message").GetString());
        }
        finally
        {
            var restore = await client.PutAsJsonAsync("/api/settings",
                new { allowPrivateNetworkImport = true });
            restore.EnsureSuccessStatusCode();
        }
    }

    private static int GetDeadPort()
    {
        var tcp = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((System.Net.IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return port;
    }

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }
}
