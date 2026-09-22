using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.Settings;
using AI.TestPlatform.Application.TestCases;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class SettingsApiTests
{
    private const string OriginalWebhookToken = "dev-webhook-token-0123456789";

    private readonly TestApiFactory _factory;

    public SettingsApiTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetSettings_SeedsDefaults_ReturnsMasked()
    {
        var client = await SuperAdminClient.CreateAsync(_factory);

        var response = await client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = (await response.Content.ReadFromJsonAsync<SettingsView>())!;
        Assert.NotEmpty(view.AiBaseUrl);
        Assert.InRange(view.AiMaxTokens, 256, 32768);
        Assert.True(view.HasWebhookToken);
        Assert.NotEmpty(view.WebhookTokenMasked);
        Assert.Contains("***", view.WebhookTokenMasked);
        Assert.DoesNotContain(OriginalWebhookToken, view.WebhookTokenMasked);
        Assert.True(view.AllowPrivateNetworkImport);
    }

    [Fact]
    public async Task UpdateSettings_AllowPrivateNetworkImport_Persists()
    {
        var client = await SuperAdminClient.CreateAsync(_factory);
        try
        {
            var put = await client.PutAsJsonAsync("/api/settings",
                new { allowPrivateNetworkImport = false });

            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            var view = (await put.Content.ReadFromJsonAsync<SettingsView>())!;
            Assert.False(view.AllowPrivateNetworkImport);

            var get = await client.GetAsync("/api/settings");
            var reread = (await get.Content.ReadFromJsonAsync<SettingsView>())!;
            Assert.False(reread.AllowPrivateNetworkImport);
        }
        finally
        {
            var restore = await client.PutAsJsonAsync("/api/settings",
                new { allowPrivateNetworkImport = true });
            restore.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task UpdateSettings_PersistsAndMasks()
    {
        var client = await SuperAdminClient.CreateAsync(_factory);

        var put = await client.PutAsJsonAsync("/api/settings", new
        {
            aiApiKey = "sk-test1234567890",
            aiModel = "custom-model",
        });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var view = (await put.Content.ReadFromJsonAsync<SettingsView>())!;
        Assert.True(view.HasAiApiKey);
        Assert.Equal("sk-***7890", view.AiApiKeyMasked);
        Assert.Equal("custom-model", view.AiModel);

        var get = await client.GetAsync("/api/settings");
        var raw = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sk-test1234567890", raw);
        var reread = (await get.Content.ReadFromJsonAsync<SettingsView>())!;
        Assert.True(reread.HasAiApiKey);
        Assert.Equal("custom-model", reread.AiModel);
    }

    [Fact]
    public async Task UpdateSettings_EmptyKey_KeepsExisting()
    {
        var client = await SuperAdminClient.CreateAsync(_factory);

        var set = await client.PutAsJsonAsync("/api/settings", new { aiApiKey = "sk-test1234567890" });
        set.EnsureSuccessStatusCode();

        var put = await client.PutAsJsonAsync("/api/settings", new { aiModel = "other-model" });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var view = (await put.Content.ReadFromJsonAsync<SettingsView>())!;
        Assert.True(view.HasAiApiKey);
        Assert.Equal("sk-***7890", view.AiApiKeyMasked);
        Assert.Equal("other-model", view.AiModel);
    }

    [Fact]
    public async Task TestConnection_WithStub_ReturnsOk()
    {
        var client = await SuperAdminClient.CreateAsync(_factory);

        var put = await client.PutAsJsonAsync("/api/settings", new { aiModel = "ping-assert-model" });
        put.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/settings/ai/test", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<TestConnectionResult>())!;
        Assert.True(result.Ok);
        Assert.Equal("stub-model", result.Model);
        Assert.Equal(5, result.LatencyMs);

        Assert.True(AIWorkerStubHandler.LastLlmConfigs.TryGetValue("/api/ping", out var config));
        Assert.Contains("\"model\":\"ping-assert-model\"", config);
    }

    [Fact]
    public async Task Webhook_AfterTokenChange_OldToken401_NewToken202()
    {
        var auth = await SuperAdminClient.CreateAsync(_factory);
        try
        {
            var put = await auth.PutAsJsonAsync("/api/settings", new { webhookToken = "new-token-xxx" });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            var oldClient = _factory.CreateClient();
            oldClient.DefaultRequestHeaders.Add("X-Webhook-Token", OriginalWebhookToken);
            var oldResponse = await oldClient.PostAsJsonAsync("/api/webhooks/executions",
                new { testCaseIds = new[] { Guid.NewGuid() } });
            Assert.Equal(HttpStatusCode.Unauthorized, oldResponse.StatusCode);

            var project = await CreateProjectAsync(auth);
            var testCase = await CreateApiCaseAsync(auth, project.Id);

            var newClient = _factory.CreateClient();
            newClient.DefaultRequestHeaders.Add("X-Webhook-Token", "new-token-xxx");
            var newResponse = await newClient.PostAsJsonAsync("/api/webhooks/executions",
                new { testCaseIds = new[] { testCase.Id } });
            Assert.Equal(HttpStatusCode.Accepted, newResponse.StatusCode);
        }
        finally
        {
            var restore = await auth.PutAsJsonAsync("/api/settings",
                new { webhookToken = OriginalWebhookToken });
            restore.EnsureSuccessStatusCode();
        }
    }

    private static int GetDeadPort()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
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

    private static async Task<TestCaseDto> CreateApiCaseAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId,
            name = $"api-{Guid.NewGuid():N}",
            type = 1,
            description = (string?)null,
            browser = (string?)null,
            timeout = 5000,
            retryCount = 0,
            baseUrl = $"http://127.0.0.1:{GetDeadPort()}",
            steps = new object[]
            {
                new { stepOrder = 0, actionType = 6, config = new { method = "GET", endpoint = "/ping" } },
            },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestCaseDto>())!;
    }
}
