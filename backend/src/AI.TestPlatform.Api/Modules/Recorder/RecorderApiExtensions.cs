using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Recorder;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Recorder;

// 脚本录制（/api/recorder）：创建会话 → 轮询步骤 → 停止 → 保存为用例
public static class RecorderApiExtensions
{
    public static RouteGroupBuilder MapRecorderApi(this RouteGroupBuilder group)
    {
        // 录制能力探测：前端据此决定是否显示入口 / 给出环境缺失提示
        group.MapGet("/capabilities", () =>
        {
            var (ok, reason) = RecorderService.CheckAvailability();
            return Results.Ok(new { available = ok, reason, browsers = BrowserCatalog.All });
        }).WithPermission(Permission.ManageTestCases);

        group.MapGet("/sessions", async (
            Guid? projectId, RecorderService recorder, CancellationToken ct) =>
            Results.Ok(await recorder.ListAsync(projectId, ct)))
            .WithPermission(Permission.ManageTestCases);

        group.MapGet("/sessions/{id:guid}", async (
            Guid id, RecorderService recorder, CancellationToken ct) =>
        {
            try { return Results.Ok(await recorder.GetAsync(id, ct)); }
            catch (RecorderNotFoundException) { return Results.NotFound(); }
        }).WithPermission(Permission.ManageTestCases);

        // 创建并拉起浏览器
        group.MapPost("/sessions", async (
            CreateRecorderSessionRequest request,
            RecorderService recorder,
            TestDbContext db,
            ICurrentUser current,
            CancellationToken ct) =>
        {
            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == request.ProjectId, ct))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["projectId"] = ["项目不存在"],
                });

            try
            {
                var session = await recorder.StartAsync(
                    request.ProjectId, request.Name ?? string.Empty, request.BaseUrl,
                    request.Browser, current.Id, current.Username, ct);
                return Results.Created($"/api/recorder/sessions/{session.Id}", session);
            }
            catch (RecorderUnavailableException ex)
            {
                // 环境不具备录制条件 / 已达并发上限：是可预期的业务拒绝，用 409 而不是 500
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status409Conflict);
            }
        }).WithPermission(Permission.ManageTestCases).WithAudit("StartRecord", "Recorder");

        // 轮询：返回脚本原文 + 解析后的步骤。Changed=false 时前端可跳过重渲染
        group.MapGet("/sessions/{id:guid}/steps", async (
            Guid id, RecorderService recorder, CancellationToken ct) =>
        {
            try { return Results.Ok(await recorder.SnapshotAsync(id, ct)); }
            catch (RecorderNotFoundException) { return Results.NotFound(); }
        }).WithPermission(Permission.ManageTestCases);

        group.MapPost("/sessions/{id:guid}/stop", async (
            Guid id, RecorderService recorder, CancellationToken ct) =>
        {
            try { return Results.Ok(await recorder.StopAsync(id, ct)); }
            catch (RecorderNotFoundException) { return Results.NotFound(); }
        }).WithPermission(Permission.ManageTestCases).WithAudit("StopRecord", "Recorder", captureBody: false);

        // 保存为用例：复用脚本解析链路，产出与「粘贴脚本导入」完全一致的步骤模型
        group.MapPost("/sessions/{id:guid}/save", async (
            Guid id, SaveRecorderRequest request,
            RecorderService recorder, CancellationToken ct) =>
        {
            try
            {
                var (testCase, error) = await recorder.SaveAsync(id, request, ct);
                if (testCase is null)
                    return Results.Json(new { message = error }, statusCode: StatusCodes.Status400BadRequest);
                return Results.Ok(new
                {
                    testCaseId = testCase.Id,
                    name = testCase.Name,
                    stepCount = testCase.Steps.Count,
                    message = $"已保存为用例「{testCase.Name}」，共 {testCase.Steps.Count} 个步骤",
                });
            }
            catch (RecorderNotFoundException) { return Results.NotFound(); }
        }).WithPermission(Permission.ManageTestCases).WithAudit("SaveRecord", "Recorder");

        group.MapDelete("/sessions/{id:guid}", async (
            Guid id, RecorderService recorder, CancellationToken ct) =>
            await recorder.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .WithPermission(Permission.ManageTestCases).WithAudit("Delete", "Recorder", captureBody: false);

        return group;
    }

    /// <summary>创建会话请求；BaseUrl 可为空（打开空白页由用户自行导航）</summary>
    public record CreateRecorderSessionRequest(
        Guid ProjectId, string? Name, string? BaseUrl, string? Browser);
}
