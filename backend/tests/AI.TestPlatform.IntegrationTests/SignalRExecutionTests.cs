using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;
using Microsoft.AspNetCore.SignalR.Client;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class SignalRExecutionTests
{
    private readonly TestApiFactory _factory;

    public SignalRExecutionTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Hub_ReceivesStepCompletedAndStatusChanged()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@123456" });
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResult>();
        var token = auth!.Token;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        try
        {
            await RunOnceAsync(client, token);
        }
        catch (TimeoutException)
        {
            // 偶发错过事件（执行先于 join 完成）时重试一次
            await RunOnceAsync(client, token);
        }
    }

    private async Task RunOnceAsync(HttpClient client, string token)
    {
        var stepReceived = new TaskCompletionSource<ExecutionResultDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var statusReceived = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/execution", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(token)!;
            })
            .Build();

        connection.On<ExecutionResultDto>("StepCompleted", dto => stepReceived.TrySetResult(dto));
        connection.On<int>("StatusChanged", status =>
        {
            if (status == (int)ExecutionStatus.Passed)
                statusReceived.TrySetResult(status);
        });

        await connection.StartAsync();
        try
        {
            var executionId = await PostExecutionAsync(client);

            await connection.InvokeAsync("JoinExecutionGroup", executionId.ToString("N"));

            var step = await stepReceived.Task.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(0, step.StepOrder);
            Assert.Equal(ExecutionStatus.Passed, step.Status);

            var final = await statusReceived.Task.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal((int)ExecutionStatus.Passed, final);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    private static async Task<Guid> PostExecutionAsync(HttpClient client)
    {
        var project = await CreateProjectAsync(client);
        var testCase = await CreateScreenshotCaseAsync(client, project.Id);

        var create = await client.PostAsJsonAsync("/api/executions", new { testCaseId = testCase.Id });
        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ExecutionSummaryDto>();
        return created!.Id;
    }

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    private static async Task<TestCaseDto> CreateScreenshotCaseAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId,
            name = $"web-{Guid.NewGuid():N}",
            type = 0,
            description = (string?)null,
            browser = "chromium",
            timeout = 15000,
            retryCount = 0,
            steps = new object[]
            {
                new { stepOrder = 0, actionType = 4, config = new { }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestCaseDto>())!;
    }
}
