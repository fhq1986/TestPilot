using System.Net;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class ProjectApiTests
{
    private readonly TestApiFactory _factory;

    public ProjectApiTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetProjects_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateAndGetProject_RoundTrips()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var name = $"Project-{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync("/api/projects",
            new { name, description = "integration test project" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(created);
        Assert.Equal(name, created!.Name);

        var detail = await client.GetAsync($"/api/projects/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var loaded = await detail.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.Equal(name, loaded!.Name);
    }

    [Fact]
    public async Task UpdateProject_ChangesName()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var created = await CreateProjectAsync(client, $"Project-{Guid.NewGuid():N}");

        var update = await client.PutAsJsonAsync($"/api/projects/{created.Id}",
            new { name = created.Name + "-updated", description = created.Description });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.Equal(created.Name + "-updated", updated!.Name);
    }

    [Fact]
    public async Task DeleteProject_WithTestCases_Returns409()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client, $"Project-{Guid.NewGuid():N}");
        await InsertTestCaseDirectlyAsync(project.Id);

        var response = await client.DeleteAsync($"/api/projects/{project.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteProject_WithoutTestCases_Returns204()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client, $"Project-{Guid.NewGuid():N}");

        var response = await client.DeleteAsync($"/api/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await client.GetAsync($"/api/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
    }

    [Fact]
    public async Task ListProjects_SupportsSearchAndPaging()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var name = $"Search-{Guid.NewGuid():N}";
        await CreateProjectAsync(client, name);

        var response = await client.GetAsync($"/api/projects?search={Uri.EscapeDataString(name)}&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<ProjectDto>>();
        Assert.NotNull(paged);
        Assert.NotEmpty(paged!.Items);
        Assert.All(paged.Items, p => Assert.Contains(name, p.Name));
    }

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name, description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private async Task InsertTestCaseDirectlyAsync(Guid projectId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        db.TestCases.Add(new TestCase { ProjectId = projectId, Name = "direct-insert", Type = TestType.Web });
        await db.SaveChangesAsync();
    }
}
