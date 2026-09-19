using System.Net;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Mocks;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class MockApiTests
{
    private const string SpecJson = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Mock API", "version": "1.0.0" },
          "paths": {
            "/login": {
              "get": {
                "summary": "登录（语义示例）",
                "responses": {
                  "200": { "description": "ok" }
                }
              }
            },
            "/users": {
              "post": {
                "summary": "创建用户",
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": {
                        "type": "object",
                        "required": [ "name", "email" ],
                        "properties": {
                          "id": { "type": "integer" },
                          "name": { "type": "string" },
                          "email": { "type": "string" },
                          "price": { "type": "number" }
                        }
                      }
                    }
                  }
                },
                "responses": {
                  "201": { "description": "created" }
                }
              }
            },
            "/users/{id}": {
              "get": {
                "summary": "查询用户",
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } }
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

    private readonly TestApiFactory _factory;

    public MockApiTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateMock_ServesSemanticResponse_AndDeleteStopsServer()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/mocks",
            new { projectId = project.Id, name = "Login Mock", spec = SpecJson });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var mock = await createResponse.Content.ReadFromJsonAsync<MockDto>();
        Assert.NotNull(mock);
        Assert.Equal(MockStatus.Running, mock!.Status);
        Assert.NotNull(mock.Port);
        Assert.InRange(mock.Port!.Value, 9000, 9999);

        using (var mockClient = new HttpClient())
        {
            var loginResponse = await mockClient.GetAsync($"http://127.0.0.1:{mock.Port}/login");
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            var loginBody = await loginResponse.Content.ReadAsStringAsync();
            Assert.Contains("\"code\":0", loginBody);

            var createUserResponse = await mockClient.PostAsync(
                $"http://127.0.0.1:{mock.Port}/users",
                new StringContent("""{"name":"张三","email":"x@y.com"}""", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Created, createUserResponse.StatusCode);
            var userBody = await createUserResponse.Content.ReadAsStringAsync();
            Assert.Contains("\"code\":0", userBody);
            Assert.Contains("\"email\":\"user@example.com\"", userBody);
            Assert.Contains("\"id\":1001", userBody);

            var getUserResponse = await mockClient.GetAsync($"http://127.0.0.1:{mock.Port}/users/42");
            Assert.Equal(HttpStatusCode.OK, getUserResponse.StatusCode);
            var getUserBody = await getUserResponse.Content.ReadAsStringAsync();
            Assert.Contains("\"code\":0", getUserBody);
        }

        var listResponse = await client.GetAsync($"/api/mocks?projectId={project.Id}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<MockDto>>();
        var listed = Assert.Single(list!, m => m.Id == mock.Id);
        Assert.Equal(MockStatus.Running, listed.Status);

        var detailResponse = await client.GetAsync($"/api/mocks/{mock.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<MockDto>();
        Assert.Equal(MockStatus.Running, detail!.Status);

        var deleteResponse = await client.DeleteAsync($"/api/mocks/{mock.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            Assert.Null(await db.MockDefinitions.FirstOrDefaultAsync(m => m.Id == mock.Id));
        }

        using var refusedClient = new HttpClient();
        await Assert.ThrowsAnyAsync<HttpRequestException>(
            () => refusedClient.GetAsync($"http://127.0.0.1:{mock.Port}/login"));
    }

    [Fact]
    public async Task StartMock_OnStoppedDefinition_RestartsServer_AndRunningReturns400()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var createResponse = await client.PostAsJsonAsync("/api/mocks",
            new { projectId = project.Id, name = "Restart Mock", spec = SpecJson });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var mock = await createResponse.Content.ReadFromJsonAsync<MockDto>();
        Assert.NotNull(mock);
        var firstPort = mock!.Port;

        // 模拟服务重启：进程内实例已停（经 MockService.StopAsync）
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<AI.TestPlatform.Api.Mocks.MockService>();
            await service.StopAsync(mock.Id, CancellationToken.None);
        }

        var startResponse = await client.PostAsync($"/api/mocks/{mock.Id}/start", null);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<MockDto>();
        Assert.Equal(MockStatus.Running, started!.Status);
        Assert.NotNull(started.Port);
        Assert.InRange(started.Port!.Value, 9000, 9999);
        Assert.NotEqual(firstPort, started.Port);

        using (var mockClient = new HttpClient())
        {
            var loginResponse = await mockClient.GetAsync($"http://127.0.0.1:{started.Port}/login");
            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        }

        // 已 Running 定义再次 start → 400
        var againResponse = await client.PostAsync($"/api/mocks/{mock.Id}/start", null);
        Assert.Equal(HttpStatusCode.BadRequest, againResponse.StatusCode);
    }

    [Fact]
    public async Task CreateMock_InvalidSpec_Returns400()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync("/api/mocks",
            new { projectId = project.Id, name = "Bad Mock", spec = "{ not valid json" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }
}
