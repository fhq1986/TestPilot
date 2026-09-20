using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Auth;

public static class DatabaseSeeder
{
    /// <summary>种子管理员账号密码（首次启动创建，之后不再改动）</summary>
    public const string SeedAdminPassword = "Admin@123456";

    /// <summary>内置超级管理员用户名（用户管理中不出现在他人列表、不可删除、不可降权）</summary>
    public const string SeedSuperAdminUsername = "superadmin";

    /// <summary>内置超级管理员密码（首次创建时写入，之后不再改动）</summary>
    public const string SeedSuperAdminPassword = "super@135246";

    public static async Task SeedAsync(TestDbContext db)
    {
        var changed = false;

        // 内置超级管理员：任何情况下都必须存在——老库升级时也要补建，
        // 否则「只有他能管用户 / 改系统设置」的权限模型会没人进得去。
        if (!await db.Users.AnyAsync(u => u.Username == SeedSuperAdminUsername))
        {
            db.Users.Add(new User
            {
                Username = SeedSuperAdminUsername,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(SeedSuperAdminPassword),
                DisplayName = "超级管理员",
                Role = UserRole.SuperAdmin,
                IsActive = true,
                TokenVersion = 0,
            });
            changed = true;
        }

        // 常规种子管理员：仅在「除超级管理员外一个用户都没有」时创建（等价于首次部署），
        // 已有用户则不动，避免覆盖线上真实的账号体系。
        if (!await db.Users.AnyAsync(u => u.Username != SeedSuperAdminUsername))
        {
            db.Users.Add(new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(SeedAdminPassword),
                DisplayName = "管理员",
                Role = UserRole.Admin,
                IsActive = true,
                TokenVersion = 0,
            });
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync();
    }
}
