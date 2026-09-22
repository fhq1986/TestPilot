using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Projects;

// 用例扩展字段定义：项目级 CRUD。
// 定义删除是硬删——用例值（jsonb）里残留的键由读取端忽略，不做级联清理（残留无害且可追溯）。
public static class CustomFieldApiExtensions
{
    public class CreateCustomFieldRequest
    {
        public string? Name { get; set; }
        public CustomFieldType FieldType { get; set; } = CustomFieldType.Text;
        public string? Options { get; set; }
    }

    public class CreateCustomFieldValidator : AbstractValidator<CreateCustomFieldRequest>
    {
        public CreateCustomFieldValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("字段名不能为空")
                .MaximumLength(50).WithMessage("字段名不能超过 50 个字符");
            RuleFor(x => x.FieldType).IsInEnum().WithMessage("字段类型无效");
            RuleFor(x => x)
                .Must(x => x.FieldType != CustomFieldType.Select || !string.IsNullOrWhiteSpace(x.Options))
                .WithMessage("下拉类型必须提供候选值");
        }
    }

    public static RouteGroupBuilder MapCustomFieldApi(this RouteGroupBuilder group)
    {
        group.MapGet("/{projectId:guid}/custom-fields", async (
            Guid projectId, TestDbContext db, CancellationToken ct) =>
        {
            var items = await db.CustomFieldDefs.AsNoTracking()
                .Where(f => f.ProjectId == projectId)
                .OrderBy(f => f.CreatedAt)
                .Select(f => new CustomFieldDto(f.Id, f.Name, f.FieldType, f.Options, f.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(items);
        }).WithPermission(Permission.ViewProjects).Produces<List<CustomFieldDto>>();

        group.MapPost("/{projectId:guid}/custom-fields", async (
            Guid projectId, CreateCustomFieldRequest request,
            IValidator<CreateCustomFieldRequest> validator,
            TestDbContext db, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct))
                return Results.NotFound(new { message = "项目不存在" });

            var name = request.Name!.Trim();
            // Select 的 Options 必须是字符串数组（前端下拉直接用），坏结构当场拒绝
            if (request.FieldType == CustomFieldType.Select)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(request.Options!);
                    if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array ||
                        doc.RootElement.GetArrayLength() == 0 ||
                        doc.RootElement.EnumerateArray().Any(i => i.ValueKind != System.Text.Json.JsonValueKind.String))
                        return Results.BadRequest(new { message = "候选值必须是非空字符串数组" });
                }
                catch (System.Text.Json.JsonException)
                {
                    return Results.BadRequest(new { message = "候选值必须是 JSON 字符串数组" });
                }
            }

            var exists = await db.CustomFieldDefs.AsNoTracking()
                .AnyAsync(f => f.ProjectId == projectId && f.Name == name, ct);
            if (exists)
                return Results.Json(new { message = $"字段名「{name}」已存在" }, statusCode: 409);

            var def = new CustomFieldDef
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                Name = name,
                FieldType = request.FieldType,
                Options = request.FieldType == CustomFieldType.Select ? request.Options : null,
            };
            db.CustomFieldDefs.Add(def);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // 查重与写入间的竞态由唯一索引兜底（两层唯一性的既有惯例）
                return Results.Json(new { message = $"字段名「{name}」已存在" }, statusCode: 409);
            }
            return Results.Created($"/api/projects/{projectId}/custom-fields/{def.Id}",
                new CustomFieldDto(def.Id, def.Name, def.FieldType, def.Options, def.CreatedAt));
        }).WithPermission(Permission.ManageProjects).WithAudit("Create", "CustomField");

        group.MapDelete("/{projectId:guid}/custom-fields/{id:guid}", async (
            Guid projectId, Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var def = await db.CustomFieldDefs
                .FirstOrDefaultAsync(f => f.Id == id && f.ProjectId == projectId, ct);
            if (def is null) return Results.NotFound(new { message = "字段不存在" });
            db.CustomFieldDefs.Remove(def);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "已删除（用例中已填写的值不再展示，但保留在数据里）" });
        }).WithPermission(Permission.ManageProjects).WithAudit("Delete", "CustomField");

        return group;
    }
}

public record CustomFieldDto(Guid Id, string Name, CustomFieldType FieldType, string? Options, DateTime CreatedAt);
