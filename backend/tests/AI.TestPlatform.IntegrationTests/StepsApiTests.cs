using System.Net;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class StepsApiTests
{
    private readonly TestApiFactory _factory;

    public StepsApiTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task UpdateSteps_ReplacesAllSteps()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateTestCaseAsync(client, project.Id);

        var update = await client.PutAsJsonAsync($"/api/testcases/{testCase.Id}/steps", new
        {
            steps = new object[]
            {
                new { stepOrder = 0, actionType = 2, config = new { url = "https://example.com" }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
                new { stepOrder = 1, actionType = 0, config = new { selector = new { type = "css", value = "#submit", description = (string?)null }, value = (string?)null }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            },
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<TestCaseDto>();
        Assert.NotNull(updated);
        Assert.Equal(2, updated!.Steps.Count);
        Assert.Equal(0, updated.Steps[0].StepOrder);
        Assert.Equal("https://example.com", updated.Steps[0].Config.Url);

        var detail = await client.GetAsync($"/api/testcases/{testCase.Id}");
        var loaded = await detail.Content.ReadFromJsonAsync<TestCaseDto>();
        Assert.Equal(2, loaded!.Steps.Count);
        Assert.Equal("#submit", loaded.Steps[1].Config.Selector!.Value);
    }

    [Fact]
    public async Task UpdateSteps_WithDuplicateOrder_Returns400()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateTestCaseAsync(client, project.Id);

        var response = await client.PutAsJsonAsync($"/api/testcases/{testCase.Id}/steps", new
        {
            steps = new object[]
            {
                new { stepOrder = 0, actionType = 3, config = new { value = "1000" }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
                new { stepOrder = 0, actionType = 4, config = new { }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSteps_WithEmptySteps_ReplacesWithEmpty()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateTestCaseAsync(client, project.Id);

        var response = await client.PutAsJsonAsync($"/api/testcases/{testCase.Id}/steps", new
        {
            steps = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TestCaseDto>();
        Assert.Empty(updated!.Steps);
    }

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private static async Task<TestCaseDto> CreateTestCaseAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId,
            name = $"case-{Guid.NewGuid():N}",
            type = 0,
            description = (string?)null,
            browser = "chromium",
            timeout = 30000,
            retryCount = 0,
            steps = new object[]
            {
                new { stepOrder = 0, actionType = 2, config = new { url = "https://example.com" }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestCaseDto>())!;
    }
}
