using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Auth;

namespace AI.TestPlatform.IntegrationTests;

/// <summary>
/// 以**内置超级管理员**登录的测试客户端。
///
/// 为什么需要它：`/api/settings` 需要 `ManageSettings` 权限，而按 <c>PermissionCatalog</c> 该权限
/// **只有 SuperAdmin 拥有**（Admin 被显式排除在 ManageSettings / ManageUsers 之外，属有意设计）。
/// 共享的 admin 客户端访问设置类端点必然 403 —— 所以设置相关测试必须用超管身份。
///
/// 密码优先取环境变量 <c>TEST_SUPERADMIN_PASSWORD</c>，缺省用测试库的种子默认值
/// （仅用于测试库；生产靠环境变量覆盖 Jwt/种子）。见 Task #122。
/// </summary>
internal static class SuperAdminClient
{
    private const string DefaultUserName = "superadmin";

    private static string Password =>
        Environment.GetEnvironmentVariable("TEST_SUPERADMIN_PASSWORD") ?? "super@135246";

    public static async Task<HttpClient> CreateAsync(TestApiFactory factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = DefaultUserName, password = Password });
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }
}
