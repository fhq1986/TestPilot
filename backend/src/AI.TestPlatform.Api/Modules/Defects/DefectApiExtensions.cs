using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace AI.TestPlatform.Api.Modules.Defects;

/// <summary>
/// 缺陷管理端点（/api/defects）。
///
/// 权限：查看/统计挂 ViewTestCases（三角色都有——缺陷是项目质量的公共记录）；
/// 写操作挂 ManageTestCases（提交缺陷的是工程师/管理员，「测试负责人」同样持有该权限，
/// 满足「提交人或测试负责人可验证」的规则；只读访客不可提交）。
/// </summary>
public static class DefectApiExtensions
{
    public static RouteGroupBuilder MapDefectApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            DefectService defects,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null,
            [FromQuery] DefectStatus? status = null,
            [FromQuery] DefectSeverity? severity = null,
            [FromQuery] Guid? assignedToId = null,
            // 执行详情页「该执行关联的缺陷」用
            [FromQuery] Guid? executionId = null,
            // 用例详情页「该用例暴露的缺陷」用
            [FromQuery] Guid? testCaseId = null,
            [FromQuery] string? search = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
            Results.Ok(await defects.ListAsync(projectId, status, severity, assignedToId,
                executionId, testCaseId, search, page, pageSize, ct)))
            .WithPermission(Permission.ViewTestCases);

        // 仪表盘统计卡 + 趋势
        group.MapGet("/stats", async (
            DefectService defects,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null) =>
            Results.Ok(await defects.StatsAsync(projectId, ct)))
            .WithPermission(Permission.ViewTestCases);

        group.MapGet("/{id:guid}", async (
            Guid id, DefectService defects, CancellationToken ct) =>
        {
            var defect = await defects.GetAsync(id, ct);
            return defect is null ? Results.NotFound() : Results.Ok(defect);
        }).WithPermission(Permission.ViewTestCases);

        // 创建。FoundInExecutionId 提供时即「一键转缺陷」：服务端快照错误/诊断/截图等证据。
        group.MapPost("/", async (
            CreateDefectRequest request,
            DefectService defects,
            ICurrentUser current,
            CancellationToken ct) =>
        {
            try
            {
                var created = await defects.CreateAsync(request, current.Id, ct);
                return Results.Created($"/api/defects/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { message = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).WithPermission(Permission.ManageTestCases).WithAudit("Create", "Defect");

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateDefectRequest request,
            DefectService defects, CancellationToken ct) =>
        {
            try
            {
                var updated = await defects.UpdateAsync(id, request, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { message = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).WithPermission(Permission.ManageTestCases).WithAudit("Update", "Defect");

        // 删除缺陷。子表（关联用例 / 复现流水）由数据库级联清理，见 DefectService.DeleteAsync
        group.MapDelete("/{id:guid}", async (
            Guid id, DefectService defects, CancellationToken ct) =>
        {
            var removed = await defects.DeleteAsync(id, ct);
            return removed ? Results.NoContent() : Results.NotFound();
        }).WithPermission(Permission.ManageTestCases).WithAudit("Delete", "Defect");

        // 批量删除：子表（关联用例 / 复现流水）由数据库级联清理，与单删一致
        group.MapPost("/batch-delete", async (
            BatchDeleteRequest request,
            IValidator<BatchDeleteRequest> validator,
            DefectService defects,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            return Results.Ok(await defects.DeleteManyAsync(request.Ids, ct));
        }).WithPermission(Permission.ManageTestCases).WithAudit("BatchDelete", "Defect");

        // 状态流转：assign / fix / verify / close / reject / defer / reopen
        group.MapPost("/{id:guid}/transition", async (
            Guid id, DefectTransitionRequest request,
            DefectService defects,
            ICurrentUser current,
            CancellationToken ct) =>
        {
            try
            {
                var updated = await defects.TransitionAsync(id, request, current.Id, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { message = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).WithPermission(Permission.ManageTestCases).WithAudit("Transition", "Defect");

        // 缺陷 ↔ 用例 关联
        group.MapPost("/{id:guid}/cases", async (
            Guid id, LinkDefectCaseRequest request,
            DefectService defects, CancellationToken ct) =>
        {
            try
            {
                return await defects.LinkCaseAsync(id, request.TestCaseId, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { message = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).WithPermission(Permission.ManageTestCases).WithAudit("LinkCase", "Defect");

        group.MapDelete("/{id:guid}/cases/{testCaseId:guid}", async (
            Guid id, Guid testCaseId, DefectService defects, CancellationToken ct) =>
            await defects.UnlinkCaseAsync(id, testCaseId, ct)
                ? Results.NoContent()
                : Results.NotFound())
            .WithPermission(Permission.ManageTestCases).WithAudit("UnlinkCase", "Defect");

        // 认领：把执行里的失败步骤记到该缺陷名下（同一执行同一步骤重复认领会被幂等吸收）
        group.MapPost("/{id:guid}/occurrences", async (
            Guid id, DefectOccurrenceRequest request,
            DefectService defects, CancellationToken ct) =>
        {
            try
            {
                return await defects.AddOccurrenceAsync(id, request.ExecutionId, request.StepOrder, ct)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { message = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).WithPermission(Permission.ManageTestCases).WithAudit("ClaimOccurrence", "Defect");

        // ------------------------------ 外部缺陷系统对接（配置开关默认关闭）

        // 已启用的外部缺陷系统（前端推送入口与跳转链接构造用）。查看权限即可读取。
        group.MapGet("/external-providers", (ExternalDefectPusher pusher) =>
            Results.Ok(pusher.EnabledProviders()))
            .WithPermission(Permission.ViewTestCases);

        // 推送缺陷到外部系统（单向创建 + 记录 ExternalRef）。重复推送会被拒绝。
        group.MapPost("/{id:guid}/push-external", async (
            Guid id, PushExternalRequest request,
            ExternalDefectPusher pusher, DefectService defects, CancellationToken ct) =>
        {
            if (!pusher.IsEnabled(request.Provider))
                return Results.Json(new { message = $"外部缺陷系统 {request.Provider} 未启用" },
                    statusCode: StatusCodes.Status400BadRequest);

            var result = await defects.PushToExternalAsync(id, request.Provider, pusher, ct);
            return result.Ok
                ? Results.Ok(new { externalRef = result.ExternalRef, externalUrl = result.ExternalUrl })
                : Results.Json(new { message = result.Error },
                    statusCode: StatusCodes.Status502BadGateway);
        }).WithPermission(Permission.ManageTestCases).WithAudit("PushExternal", "Defect");

        return group;
    }
}

/// <summary>推送缺陷到外部系统（POST /defects/{id}/push-external）</summary>
public sealed record PushExternalRequest(string Provider);
