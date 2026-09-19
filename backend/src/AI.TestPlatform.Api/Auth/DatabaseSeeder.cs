using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Auth;

public static class DatabaseSeeder
{
    /// <summary>种子管理员账号密码（首次启动创建，之后不再改动）</summary>
    public const string SeedAdminPassword = "Admin@123456";

    public static async Task SeedAsync(TestDbContext db)
    {
        if (await db.Users.AnyAsync())
            return;

        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(SeedAdminPassword),
            DisplayName = "管理员",
            Role = UserRole.Admin,
            IsActive = true,
            TokenVersion = 0,
        });
        await db.SaveChangesAsync();
    }
}
