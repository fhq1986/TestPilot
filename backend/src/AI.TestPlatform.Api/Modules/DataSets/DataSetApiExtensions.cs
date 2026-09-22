using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.DataSets;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AI.TestPlatform.Api.Common;

namespace AI.TestPlatform.Api.Modules.DataSets;

// 数据集端点（/api/datasets）：CRUD + Excel/CSV 导入 + 引用面查询 + 变量匹配校验
public static class DataSetApiExtensions
{
    public static RouteGroupBuilder MapDataSetApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            TestDbContext db,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null,
            [FromQuery] string? keyword = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            var query = db.DataSets.AsNoTracking();
            if (projectId.HasValue) query = query.Where(d => d.ProjectId == projectId.Value);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                query = query.Where(d => EF.Functions.ILike(d.Name, $"%{k}%"));
            }

            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(d => d.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new
                {
                    d.Id, d.ProjectId, ProjectName = d.Project.Name,
                    d.Name, d.Description, d.Columns,
                    d.RowCount, d.CreatedAt, d.UpdatedAt,
                    d.CreatedById,
                })
                .ToListAsync(ct);

            // 创建人显示名（M8 审计字段）：本页一次批量解析
            var creatorNames = await UserNameResolver.ResolveAsync(db, rows.Select(r => r.CreatedById), ct);

            var ids = rows.Select(r => r.Id).ToList();
            // 引用数：用例表按 DataSetId 聚合（软删除的用例已被全局过滤器排除）
            var usage = await db.TestCases.AsNoTracking()
                .Where(t => t.DataSetId != null && ids.Contains(t.DataSetId.Value))
                .GroupBy(t => t.DataSetId!.Value)
                .Select(g => new { DataSetId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.DataSetId, x => x.Count, ct);

            var items = rows.Select(d => new DataSetSummaryDto(
                d.Id, d.ProjectId, d.ProjectName, d.Name, d.Description,
                d.Columns.Count, d.RowCount,
                usage.TryGetValue(d.Id, out var count) ? count : 0,
                d.CreatedAt, d.UpdatedAt,
                creatorNames.GetName(d.CreatedById))).ToList();

            return Results.Ok(new PagedResult<DataSetSummaryDto>(items, total, page, pageSize));
        }).WithPermission(Permission.ViewTestCases).Produces<PagedResult<DataSetSummaryDto>>();

        group.MapGet("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var dataSet = await db.DataSets.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dataSet is null) return Results.NotFound();

            var usedBy = await db.TestCases.AsNoTracking()
                .Where(t => t.DataSetId == id)
                .OrderBy(t => t.Name)
                .Select(t => new DataSetUsageDto(t.Id, t.Name, t.Module))
                .ToListAsync(ct);

            return Results.Ok(ToDto(dataSet, usedBy));
        }).WithPermission(Permission.ViewTestCases).Produces<DataSetDto>();

        group.MapPost("/", async (
            CreateDataSetRequest request,
            IValidator<CreateDataSetRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == request.ProjectId, ct))
                return Results.BadRequest(new { message = "项目不存在" });

            var dataSet = new DataSet
            {
                ProjectId = request.ProjectId,
                Name = request.Name.Trim(),
                Description = Trim(request.Description),
                FirstRowIsSample = request.FirstRowIsSample,
            };
            Normalize(request.Columns, request.Rows, dataSet);

            db.DataSets.Add(dataSet);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/datasets/{dataSet.Id}", ToDto(dataSet, new List<DataSetUsageDto>()));
        }).WithPermission(Permission.ManageDataSets).WithAudit("Create", "DataSet");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDataSetRequest request,
            IValidator<UpdateDataSetRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var dataSet = await db.DataSets.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dataSet is null) return Results.NotFound();

            dataSet.Name = request.Name.Trim();
            dataSet.Description = Trim(request.Description);
            dataSet.FirstRowIsSample = request.FirstRowIsSample;
            Normalize(request.Columns, request.Rows, dataSet);
            dataSet.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var usedBy = await db.TestCases.AsNoTracking()
                .Where(t => t.DataSetId == id)
                .Select(t => new DataSetUsageDto(t.Id, t.Name, t.Module))
                .ToListAsync(ct);
            return Results.Ok(ToDto(dataSet, usedBy));
        }).WithPermission(Permission.ManageDataSets).WithAudit("Update", "DataSet");

        group.MapDelete("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var dataSet = await db.DataSets.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (dataSet is null) return Results.NotFound();
            db.DataSets.Remove(dataSet);
            // 用例上的 DataSetId 由外键 SetNull 处理（用例保留，只是不再参数化）
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ManageDataSets).WithAudit("Delete", "DataSet");

        // 批量删除：引用它的用例不受影响（外键 SetNull，只是不再参数化）
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
            var dataSets = await db.DataSets.Where(d => ids.Contains(d.Id)).ToListAsync(ct);
            db.DataSets.RemoveRange(dataSets);

            var found = dataSets.Select(d => d.Id).ToHashSet();
            var skipped = ids.Where(id => !found.Contains(id))
                .Select(id => new BatchDeleteSkippedItem(id, null, "数据集不存在"))
                .ToList();

            await db.SaveChangesAsync(ct);
            return Results.Ok(new BatchDeleteResultDto(dataSets.Count, skipped));
        }).WithPermission(Permission.ManageDataSets).WithAudit("BatchDelete", "DataSet");

        // Excel / CSV 导入：解析后回传预览，由前端确认后再调 PUT/POST 保存
        group.MapPost("/import", async (
            HttpRequest request,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { message = "请以 multipart/form-data 上传文件" });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { message = "未收到上传文件（字段名应为 file）" });
            if (file.Length > 5 * 1024 * 1024)
                return Results.BadRequest(new { message = "文件不能超过 5MB" });

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await DataSetImportParser.ParseAsync(stream, file.FileName, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                // 文件损坏/格式不符/加密等都由解析库抛异常，统一转为可读提示
                return Results.BadRequest(new { message = $"解析失败：{ex.Message}" });
            }
        }).WithPermission(Permission.ManageDataSets).WithAudit("Import", "DataSet");

        // 变量匹配校验：用例步骤里引用的 {{x}} 是否都能在数据集里找到
        group.MapGet("/check/{testCaseId:guid}", async (
            Guid testCaseId, TestDbContext db, CancellationToken ct) =>
        {
            var testCase = await db.TestCases.AsNoTracking()
                .Include(t => t.Steps)
                .Include(t => t.DataSet)
                .FirstOrDefaultAsync(t => t.Id == testCaseId, ct);
            if (testCase is null) return Results.NotFound();

            var used = new List<string>();
            foreach (var step in testCase.Steps.OrderBy(s => s.StepOrder))
                used.AddRange(StepVariableResolver.CollectKeys(step.Config));

            var distinctUsed = used.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            var columns = testCase.DataSet?.Columns ?? new List<string>();
            var missing = distinctUsed
                .Where(k => !columns.Contains(k, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var unused = columns
                .Where(c => !distinctUsed.Contains(c, StringComparer.OrdinalIgnoreCase))
                .ToList();

            return Results.Ok(new CaseVariableCheckDto(
                testCase.Id, testCase.Name,
                testCase.DataSetId, testCase.DataSet?.Name,
                distinctUsed, columns, missing, unused,
                testCase.DataSet?.Rows.FirstOrDefault() ?? new Dictionary<string, string>()));
        }).WithPermission(Permission.ViewTestCases).Produces<CaseVariableCheckDto>();

        // 用例绑定/解绑数据集
        group.MapPost("/attach/{testCaseId:guid}", async (
            Guid testCaseId,
            AttachDataSetRequest request,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var testCase = await db.TestCases.FirstOrDefaultAsync(t => t.Id == testCaseId, ct);
            if (testCase is null) return Results.NotFound();

            if (request.DataSetId.HasValue)
            {
                var dataSet = await db.DataSets.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == request.DataSetId.Value, ct);
                if (dataSet is null) return Results.BadRequest(new { message = "数据集不存在" });
                if (dataSet.ProjectId != testCase.ProjectId)
                    return Results.BadRequest(new { message = "数据集与用例不属于同一个项目" });
            }

            testCase.DataSetId = request.DataSetId;
            testCase.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { testCase.Id, testCase.DataSetId });
        }).WithPermission(Permission.ManageDataSets).WithAudit("Attach", "TestCase");

        return group;
    }

    /// <summary>列名去重/去空、行内容只保留已定义列（与校验器口径一致）</summary>
    private static void Normalize(List<string> columns, List<Dictionary<string, string>> rows, DataSet target)
    {
        var normalizedColumns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            var trimmed = column?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmed) || !seen.Add(trimmed)) continue;
            normalizedColumns.Add(trimmed);
        }

        var allowed = normalizedColumns.ToHashSet(StringComparer.Ordinal);
        var normalizedRows = new List<Dictionary<string, string>>();
        foreach (var row in rows ?? new List<Dictionary<string, string>>())
        {
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var column in normalizedColumns)
            {
                var value = row is not null && row.TryGetValue(column, out var v) ? v : string.Empty;
                data[column] = value ?? string.Empty;
            }
            normalizedRows.Add(data);
        }

        target.Columns = normalizedColumns;
        target.Rows = normalizedRows;
        target.RowCount = normalizedRows.Count;
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DataSetDto ToDto(DataSet d, List<DataSetUsageDto> usedBy) => new(
        d.Id, d.ProjectId, d.Name, d.Description,
        d.Columns, d.Rows, d.FirstRowIsSample,
        usedBy.Count, usedBy, d.CreatedAt, d.UpdatedAt);
}
