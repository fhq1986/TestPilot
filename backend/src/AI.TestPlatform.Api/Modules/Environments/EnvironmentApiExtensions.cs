using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Application.Environments;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Environment = AI.TestPlatform.Domain.Entities.Environment;

namespace AI.TestPlatform.Api.Modules.Environments;

public static class EnvironmentApiExtensions
{
    /// <summary>浏览器归一化：空值表示跟随用例配置</summary>
    private static string? NormalizeBrowser(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Application.Executions.BrowserCatalog.Normalize(value);

    public static RouteGroupBuilder MapProjectEnvironmentsApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid projectId, TestDbContext db, CancellationToken ct) =>
        {
            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct))
                return Results.NotFound();

            var environments = await db.Environments.AsNoTracking()
                .Where(e => e.ProjectId == projectId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(ct);

            return Results.Ok(environments.Select(EnvironmentDtos.ToView).ToList());
        }).WithPermission(Permission.ViewProjects).Produces<List<EnvironmentView>>();

        group.MapPost("/", async (
            Guid projectId,
            CreateEnvironmentRequest request,
            IValidator<CreateEnvironmentRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct))
                return Results.BadRequest(new { message = "项目不存在" });

            var environment = new Environment
            {
                ProjectId = projectId,
                Name = request.Name,
                BaseUrl = request.BaseUrl,
                LoginUrl = request.LoginUrl,
                LoginUsername = request.LoginUsername,
                LoginPassword = request.LoginPassword,
                LoginSuccessIndicator = request.LoginSuccessIndicator,
                AutoLogin = request.AutoLogin,
                Browser = NormalizeBrowser(request.Browser),
            };
            db.Environments.Add(environment);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/environments/{environment.Id}", EnvironmentDtos.ToView(environment));
        }).WithPermission(Permission.ManageProjects).WithAudit("Create", "Environment");

        return group;
    }

    public static RouteGroupBuilder MapEnvironmentApi(this RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateEnvironmentRequest request,
            IValidator<UpdateEnvironmentRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var environment = await db.Environments.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (environment is null)
                return Results.NotFound();

            environment.Name = request.Name;
            environment.BaseUrl = request.BaseUrl;
            environment.LoginUrl = request.LoginUrl;
            environment.LoginUsername = request.LoginUsername;
            // 密码留空=保留原密码（null 与空字符串均视为未修改）
            if (!string.IsNullOrEmpty(request.LoginPassword))
                environment.LoginPassword = request.LoginPassword;
            environment.LoginSuccessIndicator = request.LoginSuccessIndicator;
            environment.AutoLogin = request.AutoLogin;
            environment.Browser = NormalizeBrowser(request.Browser);
            environment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(EnvironmentDtos.ToView(environment));
        }).WithPermission(Permission.ManageProjects).WithAudit("Update", "Environment");

        group.MapDelete("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var environment = await db.Environments.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (environment is null)
                return Results.NotFound();

            db.Environments.Remove(environment);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ManageProjects).WithAudit("Delete", "Environment");

        return group;
    }
}
