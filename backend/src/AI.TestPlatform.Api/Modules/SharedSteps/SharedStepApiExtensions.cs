using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.SharedSteps;

// 共享步骤组（/api/shared-steps）：跨用例复用的公共步骤
public static class SharedStepApiExtensions
{
    public static RouteGroupBuilder MapSharedStepApi(this RouteGroupBuilder group)
    {
        // 分页 + 服务端筛选
        group.MapGet("/", async (
            Guid? projectId, string? search, int page, int pageSize,
            TestDbContext db, CancellationToken ct) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 200 ? 200 : pageSize;

            var query = db.SharedStepGroups.AsNoTracking().AsQueryable();
            if (projectId is not null) query = query.Where(g => g.ProjectId == projectId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim();
                query = query.Where(g => g.Name.Contains(keyword));
            }

            var total = await query.CountAsync(ct);
            var groups = await query
                .OrderBy(g => g.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(g => new
                {
                    g.Id, g.ProjectId, g.Name, g.Description,
                    ItemCount = g.Items.Count,
                    g.Variables, g.CreatedAt, g.UpdatedAt,
                })
                .ToListAsync(ct);

            // 引用统计：一次分组查询拿到本页全部，避免每行一次 COUNT（N+1）
            var ids = groups.Select(g => g.Id).ToList();
            var usages = await db.TestSteps.AsNoTracking()
                .Where(s => s.SharedGroupId != null && ids.Contains(s.SharedGroupId.Value))
                .GroupBy(s => s.SharedGroupId!.Value)
                .Select(gr => new { GroupId = gr.Key, Cases = gr.Select(x => x.TestCaseId).Distinct().Count() })
                .ToListAsync(ct);
            var usageMap = usages.ToDictionary(u => u.GroupId, u => u.Cases);

            var items = groups.Select(g => new SharedStepGroupView(
                g.Id, g.ProjectId, g.Name, g.Description, g.ItemCount,
                usageMap.GetValueOrDefault(g.Id),
                g.Variables.Select(v => new SharedVariableDto(v.Name, v.Value)).ToList(),
                g.CreatedAt, g.UpdatedAt)).ToList();

            return Results.Ok(new PagedResult<SharedStepGroupView>(items, total, page, pageSize));
        }).WithPermission(Permission.ManageSharedSteps);

        // 供「插入共享步骤」下拉用：只回 id/名称/步骤数，一次拿全（组数量天然有限）
        group.MapGet("/options", async (Guid? projectId, TestDbContext db, CancellationToken ct) =>
        {
            var query = db.SharedStepGroups.AsNoTracking().AsQueryable();
            if (projectId is not null) query = query.Where(g => g.ProjectId == projectId);
            return Results.Ok(await query
                .OrderBy(g => g.Name)
                .Select(g => new SharedStepOption(g.Id, g.Name, g.Items.Count))
                .ToListAsync(ct));
        }).WithPermission(Permission.ManageSharedSteps);

        group.MapGet("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var group = await db.SharedStepGroups.AsNoTracking()
                .Include(g => g.Items)
                .FirstOrDefaultAsync(g => g.Id == id, ct);
            return group is null ? Results.NotFound() : Results.Ok(ToDetail(group));
        }).WithPermission(Permission.ManageSharedSteps);

        // 引用该组的用例清单：删除前给用户看清楚影响面
        group.MapGet("/{id:guid}/usages", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var usages = await db.TestSteps.AsNoTracking()
                .Where(s => s.SharedGroupId == id)
                .Select(s => new { s.TestCaseId, s.TestCase.Name, s.StepOrder })
                .Distinct()
                .ToListAsync(ct);

            var cases = usages
                .GroupBy(u => new { u.TestCaseId, u.Name })
                .Select(g => new { testCaseId = g.Key.TestCaseId, name = g.Key.Name, stepOrders = g.Select(x => x.StepOrder).ToList() })
                .ToList();
            return Results.Ok(cases);
        }).WithPermission(Permission.ManageSharedSteps);

        group.MapPost("/", async (SharedStepGroupRequest request, TestDbContext db,
            ICurrentUser current, CancellationToken ct) =>
        {
            var error = Validate(request);
            if (error is not null) return Results.ValidationProblem(error);
            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == request.ProjectId, ct))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["projectId"] = ["项目不存在"] });

            var group = new SharedStepGroup
            {
                ProjectId = request.ProjectId,
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                CreatedById = current.Id,
                Variables = ToVariables(request.Variables),
            };
            ApplyItems(group, request.Items);

            db.SharedStepGroups.Add(group);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/shared-steps/{group.Id}", ToDetail(group));
        }).WithPermission(Permission.ManageSharedSteps).WithAudit("Create", "SharedStep");

        group.MapPut("/{id:guid}", async (Guid id, SharedStepGroupRequest request,
            TestDbContext db, CancellationToken ct) =>
        {
            var error = Validate(request);
            if (error is not null) return Results.ValidationProblem(error);

            var group = await db.SharedStepGroups.Include(g => g.Items)
                .FirstOrDefaultAsync(g => g.Id == id, ct);
            if (group is null) return Results.NotFound();

            group.Name = request.Name.Trim();
            group.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            group.Variables = ToVariables(request.Variables);

            // 整体替换步骤：步骤数量少（通常 <20），全删重建比逐条 diff 简单且不容易漏
            group.Items.Clear();
            ApplyItems(group, request.Items);
            group.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDetail(group));
        }).WithPermission(Permission.ManageSharedSteps).WithAudit("Update", "SharedStep");

        group.MapDelete("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var group = await db.SharedStepGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
            if (group is null) return Results.NotFound();

            // 引用方不阻断删除：外键是 SetNull，引用步骤会退化为"失效引用"，
            // 运行时展开器会明确告警让用户去修。这里把影响面返回给前端提示。
            var usageCount = await db.TestSteps
                .Where(s => s.SharedGroupId == id)
                .Select(s => s.TestCaseId).Distinct().CountAsync(ct);

            db.SharedStepGroups.Remove(group);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new
            {
                deleted = true,
                affectedCaseCount = usageCount,
                message = usageCount == 0
                    ? "已删除"
                    : $"已删除，{usageCount} 个用例中引用它的步骤需要重新编辑",
            });
        }).WithPermission(Permission.ManageSharedSteps).WithAudit("Delete", "SharedStep", captureBody: false);

        // 批量删除：与单删一致的语义——引用方不阻断（外键 SetNull，运行时展开器告警）
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
            var groups = await db.SharedStepGroups.Where(g => ids.Contains(g.Id)).ToListAsync(ct);
            db.SharedStepGroups.RemoveRange(groups);

            var found = groups.Select(g => g.Id).ToHashSet();
            var skipped = ids.Where(id => !found.Contains(id))
                .Select(id => new BatchDeleteSkippedItem(id, null, "共享步骤不存在"))
                .ToList();

            await db.SaveChangesAsync(ct);
            return Results.Ok(new BatchDeleteResultDto(groups.Count, skipped));
        }).WithPermission(Permission.ManageSharedSteps).WithAudit("BatchDelete", "SharedStep", captureBody: false);

        return group;
    }

    private static void ApplyItems(SharedStepGroup group, List<SharedStepItemDto>? items)
    {
        var order = 0;
        foreach (var item in items ?? [])
        {
            group.Items.Add(new SharedStepItem
            {
                GroupId = group.Id,
                StepOrder = order++,
                ActionType = item.ActionType,
                Config = item.Config ?? new StepConfig(),
                AIInstruction = item.AIInstruction,
                AIElementDescription = item.AIElementDescription,
            });
        }
    }

    private static List<SharedVariableEntry> ToVariables(List<SharedVariableDto>? variables) =>
        (variables ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v.Name))
            .Select(v => new SharedVariableEntry { Name = v.Name.Trim(), Value = v.Value ?? string.Empty })
            .ToList();

    private static Dictionary<string, string[]>? Validate(SharedStepGroupRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors["name"] = ["名称不能为空"];
        else if (request.Name.Length > 200)
            errors["name"] = ["名称长度不能超过 200 位"];
        if (request.Items is { Count: > 200 })
            errors["items"] = ["单个共享步骤组最多 200 个步骤"];
        return errors.Count == 0 ? null : errors;
    }

    private static SharedStepGroupDetail ToDetail(SharedStepGroup group) => new(
        group.Id, group.ProjectId, group.Name, group.Description,
        group.Items.OrderBy(i => i.StepOrder).Select(i => new SharedStepItemDto(
            i.StepOrder, i.ActionType, i.Config, i.AIInstruction, i.AIElementDescription)).ToList(),
        group.Variables.Select(v => new SharedVariableDto(v.Name, v.Value)).ToList(),
        group.CreatedAt, group.UpdatedAt);
}

public record SharedVariableDto(string Name, string? Value);

public record SharedStepItemDto(
    int StepOrder, ActionType ActionType, StepConfig? Config,
    string? AIInstruction, string? AIElementDescription);

public record SharedStepGroupRequest(
    Guid ProjectId, string Name, string? Description,
    List<SharedStepItemDto>? Items, List<SharedVariableDto>? Variables);

public record SharedStepGroupView(
    Guid Id, Guid ProjectId, string Name, string? Description,
    int ItemCount, int UsedByCaseCount, IReadOnlyList<SharedVariableDto> Variables,
    DateTime CreatedAt, DateTime? UpdatedAt);

public record SharedStepGroupDetail(
    Guid Id, Guid ProjectId, string Name, string? Description,
    IReadOnlyList<SharedStepItemDto> Items, IReadOnlyList<SharedVariableDto> Variables,
    DateTime CreatedAt, DateTime? UpdatedAt);

/// <summary>下拉选项（插入共享步骤时用，字段刻意精简）</summary>
public record SharedStepOption(Guid Id, string Name, int ItemCount);
