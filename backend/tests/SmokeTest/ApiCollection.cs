using Xunit;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Auth;

namespace AI.TestPlatform.IntegrationTests;

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<TestApiFactory>;

public static class TestClientHelper
{
    public static async Task<HttpClient> CreateAuthenticatedAsync(TestApiFactory factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Admin@123456" });
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResult>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }
}
