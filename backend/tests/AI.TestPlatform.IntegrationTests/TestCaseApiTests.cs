using System.Net;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using Microsoft.AspNetCore.Http;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class TestCaseApiTests
{
    private readonly TestApiFactory _factory;

    public TestCaseApiTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CreateTestCase_WithSteps_RoundTripsConfig()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var create = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId = project.Id,
            name = "login-case",
            type = 0,
            description = (string?)null,
            browser = "chrome",
            timeout = 30000,
            retryCount = 0,
            steps = new object[]
            {
                new
                {
                    stepOrder = 0,
                    actionType = 2,
                    config = new { url = "https://example.com/login" },
                    aiInstruction = (string?)null,
                    aiElementDescription = (string?)null,
                },
                new
                {
                    stepOrder = 1,
                    actionType = 1,
                    config = new
                    {
                        selector = new { type = "ai", description = "username input", value = (string?)null },
                    },
                    aiInstruction = "input username",
                    aiElementDescription = (string?)null,
                },
            },
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<TestCaseDto>();
        Assert.NotNull(created);
        Assert.Equal(2, created!.Steps.Count);

        var detail = await client.GetAsync($"/api/testcases/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var loaded = await detail.Content.ReadFromJsonAsync<TestCaseDto>();
        Assert.Equal(2, loaded!.Steps.Count);
        Assert.Equal("https://example.com/login", loaded.Steps[0].Config.Url);
        Assert.Equal("username input", loaded.Steps[1].Config.Selector!.Description);
    }

    [Fact]
    public async Task CreateTestCase_WithUnknownProject_Returns400()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId = Guid.NewGuid(),
            name = "orphan-case",
            type = 0,
            description = (string?)null,
            browser = "chrome",
            timeout = 30000,
            retryCount = 0,
            steps = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTestCase_WithEmptyName_Returns400Validation()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId = project.Id,
            name = "   ",
            type = 0,
            description = (string?)null,
            browser = "chrome",
            timeout = 30000,
            retryCount = 0,
            steps = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("Name", problem!.Errors.Keys);
    }

    [Fact]
    public async Task DeleteTestCase_SoftDeletes_AndHidesFromList()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateTestCaseAsync(client, project.Id, "to-delete");

        var delete = await client.DeleteAsync($"/api/testcases/{testCase.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var detail = await client.GetAsync($"/api/testcases/{testCase.Id}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);

        var list = await client.GetAsync($"/api/testcases?projectId={project.Id}");
        var paged = await list.Content.ReadFromJsonAsync<PagedResult<TestCaseSummaryDto>>();
        Assert.DoesNotContain(paged!.Items, t => t.Id == testCase.Id);
    }

    [Fact]
    public async Task UpdateTestCase_ChangesStatusAndName()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateTestCaseAsync(client, project.Id, "before-update");

        var update = await client.PutAsJsonAsync($"/api/testcases/{testCase.Id}", new
        {
            name = "after-update",
            description = testCase.Description,
            status = 1,
            browser = testCase.Browser,
            timeout = 60000,
            retryCount = 1,
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<TestCaseDto>();
        Assert.Equal("after-update", updated!.Name);
        Assert.Equal(1, (int)updated.Status);
        Assert.Equal(60000, updated.Timeout);
    }

    [Fact]
    public async Task CreateTestCase_WithoutSteps_Returns400()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId = project.Id,
            name = "no-steps-case",
            type = 0,
            description = (string?)null,
            browser = "chrome",
            timeout = 30000,
            retryCount = 0,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("Steps", problem!.Errors.Keys);
    }

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private static async Task<TestCaseDto> CreateTestCaseAsync(HttpClient client, Guid projectId, string name)
    {
        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId,
            name,
            type = 0,
            description = (string?)null,
            browser = "chrome",
            timeout = 30000,
            retryCount = 0,
            steps = Array.Empty<object>(),
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestCaseDto>())!;
    }
}
