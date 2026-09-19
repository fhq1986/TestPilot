using System.Net.Http.Json;
using AI.TestPlatform.IntegrationTests;
using Xunit;

namespace SmokeTest;

[Collection("api")]
public class FixtureSmokeTests
{
    private readonly TestApiFactory _factory;
    public FixtureSmokeTests(TestApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_endpoint_responds()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.True((int)resp.StatusCode < 500);
    }
}
