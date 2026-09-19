using System.Net;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Environments;
using AI.TestPlatform.Application.Projects;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class EnvironmentApiTests
{
    private readonly TestApiFactory _factory;

    public EnvironmentApiTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task EnvironmentCrud_Roundtrip_Works()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var create = await client.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new
        {
            name = "测试环境",
            baseUrl = "https://example.com",
            loginUrl = "/login",
            loginUsername = "admin",
            loginPassword = (string?)null,
            loginSuccessIndicator = (string?)null,
            autoLogin = true,
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<EnvironmentView>();
        Assert.NotNull(created);
        Assert.Equal(project.Id, created!.ProjectId);
        Assert.Equal("测试环境", created.Name);
        Assert.Equal("https://example.com", created.BaseUrl);
        Assert.Equal("/login", created.LoginUrl);
        Assert.True(created.AutoLogin);

        var list = await client.GetAsync($"/api/projects/{project.Id}/environments");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listDto = await list.Content.ReadFromJsonAsync<List<EnvironmentView>>();
        Assert.Contains(listDto!, e => e.Id == created.Id);

        var update = await client.PutAsJsonAsync($"/api/environments/{created.Id}", new
        {
            name = "生产环境",
            baseUrl = "https://example.org",
            loginUrl = (string?)null,
            loginUsername = "ops",
            loginPassword = (string?)null,
            loginSuccessIndicator = (string?)null,
            autoLogin = false,
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<EnvironmentView>();
        Assert.NotNull(updated);
        Assert.Equal("生产环境", updated!.Name);
        Assert.Equal("https://example.org", updated.BaseUrl);
        Assert.False(updated.AutoLogin);
    }

    [Fact]
    public async Task Environment_PasswordMaskedAndKeptOnEmptyUpdate()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        const string secret = "secret-1234567890";

        var create = await client.PostAsJsonAsync($"/api/projects/{project.Id}/environments", new
        {
            name = "带密码环境",
            baseUrl = "https://example.com",
            loginUrl = (string?)null,
            loginUsername = "admin",
            loginPassword = secret,
            loginSuccessIndicator = (string?)null,
            autoLogin = true,
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<EnvironmentView>();
        Assert.True(created!.HasLoginPassword);
        Assert.NotEqual(secret, created.LoginPasswordMasked);
        Assert.DoesNotContain(secret, await create.Content.ReadAsStringAsync());

        var update = await client.PutAsJsonAsync($"/api/environments/{created.Id}", new
        {
            name = created.Name,
            baseUrl = created.BaseUrl,
            loginUrl = (string?)null,
            loginUsername = "admin",
            loginPassword = (string?)null,
            loginSuccessIndicator = (string?)null,
            autoLogin = true,
        });
        var afterNullUpdate = await update.Content.ReadFromJsonAsync<EnvironmentView>();
        Assert.True(afterNullUpdate!.HasLoginPassword, "密码留空更新应保留原密码");
        Assert.Equal(created.LoginPasswordMasked, afterNullUpdate.LoginPasswordMasked);

        var updateEmpty = await client.PutAsJsonAsync($"/api/environments/{created.Id}", new
        {
            name = created.Name,
            baseUrl = created.BaseUrl,
            loginUrl = (string?)null,
            loginUsername = "admin",
            loginPassword = "",
            loginSuccessIndicator = (string?)null,
            autoLogin = true,
        });
        var afterEmptyUpdate = await updateEmpty.Content.ReadFromJsonAsync<EnvironmentView>();
        Assert.True(afterEmptyUpdate!.HasLoginPassword, "密码空字符串更新应保留原密码");
        Assert.Equal(created.LoginPasswordMasked, afterEmptyUpdate.LoginPasswordMasked);
    }

    [Fact]
    public async Task CreateEnvironment_UnknownProject_Returns400()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);

        var response = await client.PostAsJsonAsync($"/api/projects/{Guid.NewGuid()}/environments", new
        {
            name = "环境",
            baseUrl = "https://example.com",
            loginUrl = (string?)null,
            loginUsername = (string?)null,
            loginPassword = (string?)null,
            loginSuccessIndicator = (string?)null,
            autoLogin = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEnvironment_Returns204AndListsEmpty()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var created = await CreateEnvironmentAsync(client, project.Id, "待删除");

        var delete = await client.DeleteAsync($"/api/environments/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await client.GetAsync($"/api/projects/{project.Id}/environments");
        var listDto = await list.Content.ReadFromJsonAsync<List<EnvironmentView>>();
        Assert.Empty(listDto!);

        var deleteAgain = await client.DeleteAsync($"/api/environments/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private static async Task<EnvironmentView> CreateEnvironmentAsync(
        HttpClient client, Guid projectId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/environments", new
        {
            name,
            baseUrl = "https://example.com",
            loginUrl = (string?)null,
            loginUsername = (string?)null,
            loginPassword = (string?)null,
            loginSuccessIndicator = (string?)null,
            autoLogin = true,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EnvironmentView>())!;
    }
}
