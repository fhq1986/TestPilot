using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Modules.TestCases;
using AI.TestPlatform.Application.Reports;
using AI.TestPlatform.Domain.Entities;
using FluentValidation;

namespace AI.TestPlatform.Api.Modules.Reports;

public static class ReportApiExtensions
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static RouteGroupBuilder MapReportApi(this RouteGroupBuilder group)
    {
        // 单次执行报告
        group.MapGet("/executions/{id:guid}", async (
            Guid id,
            TestCaseReportService reports,
            CancellationToken ct) =>
        {
            try
            {
                var file = await reports.GenerateForExecutionAsync(id, ct);
                return Results.File(file.Content, XlsxContentType, file.FileName);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        }).WithPermission(Permission.ViewReports);

        // 批量导出：为选中的执行记录生成一份报告
        group.MapPost("/executions", async (
            BatchReportRequest request,
            IValidator<BatchReportRequest> validator,
            TestCaseReportService reports,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            try
            {
                var file = await reports.GenerateForExecutionsAsync(request.ExecutionIds, ct);
                return Results.File(file.Content, XlsxContentType, file.FileName);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        }).WithPermission(Permission.ViewReports);

        // 项目汇总报告（可选时间范围；每个用例取区间内最新一次执行结果）
        // planId：只统计该测试计划范围内的用例，用于按验收批次导出
        group.MapGet("/projects/{projectId:guid}", async (
            Guid projectId,
            TestCaseReportService reports,
            CancellationToken ct,
            DateTime? from = null,
            DateTime? to = null,
            Guid? planId = null) =>
        {
            try
            {
                var file = await reports.GenerateForProjectAsync(projectId, from, to, planId, ct);
                return Results.File(file.Content, XlsxContentType, file.FileName);
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        }).WithPermission(Permission.ViewReports);

        return group;
    }
}
