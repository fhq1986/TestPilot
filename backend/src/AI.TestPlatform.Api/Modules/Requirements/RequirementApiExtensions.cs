using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.Requirements;
using AI.TestPlatform.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace AI.TestPlatform.Api.Modules.Requirements;

/// <summary>
/// 需求覆盖端点（/api/requirements）。
///
/// 权限：与用例保持一致——查看挂 ViewTestCases；增删改挂 ManageTestCases
/// （需求是测试范围的输入，能管用例的人才能管需求）。
/// </summary>
public static class RequirementApiExtensions
{
    public static RouteGroupBuilder MapRequirementApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            RequirementService requirements,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
            Results.Ok(await requirements.ListAsync(projectId, search, page, pageSize, ct)))
            .WithPermission(Permission.ViewTestCases);

        // 项目覆盖统计卡：覆盖率 + 未覆盖缺口清单
        group.MapGet("/coverage", async (
            RequirementService requirements,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null) =>
            Results.Ok(await requirements.CoverageAsync(projectId, ct)))
            .WithPermission(Permission.ViewTestCases);

        group.MapGet("/{id:guid}", async (
            Guid id, RequirementService requirements, CancellationToken ct) =>
        {
            var requirement = await requirements.GetAsync(id, ct);
            return requirement is null ? Results.NotFound() : Results.Ok(requirement);
        }).WithPermission(Permission.ViewTestCases);

        group.MapPost("/", async (
            CreateRequirementRequest request,
            RequirementService requirements,
            CancellationToken ct) =>
        {
            try
            {
                var created = await requirements.CreateAsync(request, ct);
                return Results.Created($"/api/requirements/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { message = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).WithPermission(Permission.ManageTestCases).WithAudit("Create", "Requirement");

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateRequirementRequest request,
            RequirementService requirements, CancellationToken ct) =>
        {
            try
            {
                var updated = await requirements.UpdateAsync(id, request, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { message = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).WithPermission(Permission.ManageTestCases).WithAudit("Update", "Requirement");

        group.MapDelete("/{id:guid}", async (
            Guid id, RequirementService requirements, CancellationToken ct) =>
            await requirements.DeleteAsync(id, ct)
                ? Results.NoContent()
                : Results.NotFound())
            .WithPermission(Permission.ManageTestCases).WithAudit("Delete", "Requirement");

        // 批量删除：需求 ↔ 用例的映射由级联清理，与单删一致
        group.MapPost("/batch-delete", async (
            BatchDeleteRequest request,
            IValidator<BatchDeleteRequest> validator,
            RequirementService requirements,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            return Results.Ok(await requirements.DeleteManyAsync(request.Ids, ct));
        }).WithPermission(Permission.ManageTestCases).WithAudit("BatchDelete", "Requirement");

        return group;
    }
}
