using System.Linq.Expressions;
using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestPlans;
using AI.TestPlatform.Api.TestPlans;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Projects;

public static class ProjectApiExtensions
{
    /// <summary>
    /// 实体 → DTO 的统一投影。
    /// 抽成表达式而不是在三处各写一遍：字段有十几项，抄三遍必然出现「列表有、详情没有」这种漂移。
    /// </summary>
    private static readonly Expression<Func<Project, ProjectDto>> ToDto = p => new ProjectDto(
        p.Id, p.Name, p.Description, p.CreatedById,
        p.CreatedAt, p.UpdatedAt, p.TestCases.Count,
        p.ManagerId,
        p.Manager != null ? (p.Manager.DisplayName ?? p.Manager.Username) : null,
        p.TestOwnerId,
        p.TestOwner != null ? (p.TestOwner.DisplayName ?? p.TestOwner.Username) : null,
        p.TestOwner != null ? p.TestOwner.Email : null,
        p.DeveloperOwnerId,
        p.DeveloperOwner != null ? (p.DeveloperOwner.DisplayName ?? p.DeveloperOwner.Username) : null);

    public static RouteGroupBuilder MapProjectApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            TestDbContext db,
            CancellationToken ct,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            var query = db.Projects.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(p => p.Name.Contains(search) ||
                    (p.Description != null && p.Description.Contains(search)));

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(p => p.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToDto)
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<ProjectDto>(items, total, page, pageSize));
        }).WithPermission(Permission.ViewProjects);

        group.MapGet("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(ToDto)
                .FirstOrDefaultAsync(ct);

            return project is null ? Results.NotFound() : Results.Ok(project);
        }).WithPermission(Permission.ViewProjects);

        group.MapPost("/", async (
            CreateProjectRequest request,
            IValidator<CreateProjectRequest> validator,
            HttpContext http,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            // 名称先 Trim 再查重：否则「Foo」与「Foo 」会被当成两个项目，
            // 在列表里几乎看不出差别，用户只会觉得系统没做校验
            var name = request.Name.Trim();
            if (await db.Projects.AsNoTracking().AnyAsync(p => p.Name == name, ct))
                return DuplicateName(name);

            var assigneeError = await ValidateAssigneesAsync(db, request.ManagerId, request.TestOwnerId, request.DeveloperOwnerId, ct);
            if (assigneeError is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["managerId"] = [assigneeError],
                });

            var project = new Project
            {
                Name = name,
                Description = request.Description,
                ManagerId = request.ManagerId,
                TestOwnerId = request.TestOwnerId,
                DeveloperOwnerId = request.DeveloperOwnerId,
                CreatedById = http.User.GetUserId(),
            };
            db.Projects.Add(project);
            // 上面那次查重与这里的写入之间存在竞态窗口：两个请求可以同时查到"名称不存在"。
            // 数据库唯一索引是最终防线，撞上时把 23505 转成与查重一致的 409 + 中文提示，
            // 而不是让用户看到一个"服务器内部错误"。
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return DuplicateName(name);
            }

            var dto = await LoadDtoAsync(db, project.Id, ct);
            return Results.Created($"/api/projects/{project.Id}", dto);
        }).WithPermission(Permission.ManageProjects).WithAudit("Create", "Project");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateProjectRequest request,
            IValidator<UpdateProjectRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null)
                return Results.NotFound();

            var name = request.Name.Trim();
            // 必须排除自身：否则「只改描述、名称不动」会被自己判为重复而保存失败
            if (await db.Projects.AsNoTracking().AnyAsync(p => p.Id != id && p.Name == name, ct))
                return DuplicateName(name);

            var assigneeError = await ValidateAssigneesAsync(db, request.ManagerId, request.TestOwnerId, request.DeveloperOwnerId, ct);
            if (assigneeError is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["managerId"] = [assigneeError],
                });

            project.Name = name;
            project.Description = request.Description;
            project.ManagerId = request.ManagerId;
            project.TestOwnerId = request.TestOwnerId;
            project.DeveloperOwnerId = request.DeveloperOwnerId;
            project.UpdatedAt = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return DuplicateName(name);
            }

            return Results.Ok(await LoadDtoAsync(db, id, ct));
        }).WithPermission(Permission.ManageProjects).WithAudit("Update", "Project");

        // 批量删除：仅删除没有任何用例的项目，其余跳过并给出原因
        group.MapPost("/batch-delete", async (
            BatchDeleteRequest request,
            IValidator<BatchDeleteRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var ids = request.Ids.Distinct().ToList();
            var projects = await db.Projects.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            // 只阻止存在未软删用例的项目——软删的用例前端也看不到，不应阻塞项目删除
            var blockedIds = await db.TestCases
                .Where(t => ids.Contains(t.ProjectId))
                .Select(t => t.ProjectId)
                .Distinct()
                .ToListAsync(ct);
            var blocked = blockedIds.ToHashSet();

            var skipped = new List<BatchDeleteSkippedItem>();
            var deleted = 0;
            foreach (var project in projects)
            {
                if (blocked.Contains(project.Id))
                {
                    skipped.Add(new BatchDeleteSkippedItem(project.Id, project.Name, "项目下存在测试用例"));
                    continue;
                }
                db.Projects.Remove(project);
                deleted++;
            }
            var found = projects.Select(p => p.Id).ToHashSet();
            foreach (var id in ids.Where(id => !found.Contains(id)))
                skipped.Add(new BatchDeleteSkippedItem(id, null, "项目不存在或已删除"));

            await db.SaveChangesAsync(ct);
            return Results.Ok(new BatchDeleteResultDto(deleted, skipped));
        }).WithPermission(Permission.ManageProjects).WithAudit("BatchDelete", "Project");

        group.MapDelete("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null)
                return Results.NotFound();

            // 只阻止存在未软删用例的项目——软删的用例前端也看不到，不应阻塞项目删除
            var hasCases = await db.TestCases
                .AnyAsync(t => t.ProjectId == id, ct);
            if (hasCases)
                return Results.Conflict(new { message = "项目下存在测试用例，无法删除" });

            db.Projects.Remove(project);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ManageProjects).WithAudit("Delete", "Project");

        return group;
    }

    /// <summary>写入后重新查一遍以拿到负责人姓名（比手工拼 DTO 更不容易漏字段）</summary>
    private static Task<ProjectDto?> LoadDtoAsync(TestDbContext db, Guid id, CancellationToken ct) =>
        db.Projects.AsNoTracking().Where(p => p.Id == id).Select(ToDto).FirstOrDefaultAsync(ct);

    /// <summary>
    /// 负责人必须是真实存在的用户。返回第一个错误信息，null 表示通过。
    /// 停用的用户**仍允许**被指定：停用只表示「不能登录」，
    /// 与「是否负责这个项目」无关；强行排除会让「离职交接前先停账号」这种正常流程卡住。
    /// </summary>
    private static async Task<string?> ValidateAssigneesAsync(
        TestDbContext db, Guid? managerId, Guid? testOwnerId, Guid? developerOwnerId, CancellationToken ct)
    {
        var ids = new[] { managerId, testOwnerId, developerOwnerId }
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return null;

        // 一次查完三个人：逐个查是 3 次往返，而且这里本来就只需要"存在与否"
        var found = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);

        if (managerId.HasValue && !found.Contains(managerId.Value)) return "指定的项目负责人不存在";
        if (testOwnerId.HasValue && !found.Contains(testOwnerId.Value)) return "指定的测试负责人不存在";
        if (developerOwnerId.HasValue && !found.Contains(developerOwnerId.Value)) return "指定的开发负责人不存在";
        return null;
    }

    /// <summary>项目重名的统一 409 响应（应用层查重与数据库唯一索引兜底共用同一句话术）</summary>
    private static IResult DuplicateName(string name) =>
        Results.Json(new { message = $"已存在同名项目「{name}」" },
            statusCode: StatusCodes.Status409Conflict);

    /// <summary>PostgreSQL 唯一约束冲突（23505）</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
