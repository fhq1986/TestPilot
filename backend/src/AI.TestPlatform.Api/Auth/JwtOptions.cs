namespace AI.TestPlatform.Api.Auth;

public class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "ai-test-platform";
    public string Audience { get; set; } = "ai-test-platform";
    public int ExpireMinutes { get; set; } = 480;
}
