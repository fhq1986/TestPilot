using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Modules.TestCases;
using AI.TestPlatform.Application.Scripts;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Scripts;

// 脚本双向通道（/api/scripts）：解析（codegen → 步骤，仅预览）、导入（创建用例）、导出（用例 → 脚本）
public static class ScriptApiExtensions
{
    public static RouteGroupBuilder MapScriptApi(this RouteGroupBuilder group)
    {
        // 解析预览：不落库，供前端展示映射结果与告警
        group.MapPost("/parse", (ParseScriptRequest request) =>
        {
            var result = PlaywrightScriptParser.Parse(request.Script);
            return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
        }).WithPermission(Permission.ManageTestCases);

        // 导入：解析并创建用例
        group.MapPost("/import", async (
            ImportScriptRequest request,
            TestDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Scripts.Import");
            var parsed = PlaywrightScriptParser.Parse(request.Script);
            if (!parsed.Ok)
                return Results.BadRequest(new { message = parsed.Error, warnings = parsed.Warnings });

            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == request.ProjectId, ct))
                return Results.BadRequest(new { message = "项目不存在" });

            // 可选：创建的用例自动加入该测试计划范围（必须属于同一项目，提前拦截给明确报错）
            if (request.TestPlanId is { } planId)
            {
                var planProjectId = await db.TestPlans.AsNoTracking()
                    .Where(p => p.Id == planId)
                    .Select(p => (Guid?)p.ProjectId)
                    .FirstOrDefaultAsync(ct);
                if (planProjectId is null)
                    return Results.BadRequest(new { message = "所选测试计划不存在" });
                if (planProjectId != request.ProjectId)
                    return Results.BadRequest(new { message = "测试计划与所选项目不匹配" });
            }

            var name = string.IsNullOrWhiteSpace(request.Name)
                ? parsed.SuggestedName ?? "导入的用例"
                : request.Name.Trim();

            var testCase = new TestCase
            {
                ProjectId = request.ProjectId,
                Name = name.Length > 200 ? name[..200] : name,
                Type = parsed.SuggestedType,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Module = string.IsNullOrWhiteSpace(request.Module) ? null : request.Module.Trim(),
                Priority = string.IsNullOrWhiteSpace(request.Priority) ? null : request.Priority.Trim(),
                Browser = "chromium",
                Timeout = 30000,
                Status = TestCaseStatus.Draft,
                // 未显式给地址时用脚本里第一个绝对 URL 的 origin 兜底
                BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? parsed.SuggestedBaseUrl : request.BaseUrl.Trim(),
            };

            foreach (var parsedStep in parsed.Steps)
            {
                testCase.Steps.Add(new TestStep
                {
                    StepOrder = parsedStep.StepOrder,
                    ActionType = parsedStep.ActionType,
                    Config = parsedStep.Config,
                    AIInstruction = parsedStep.Instruction,
                    AIElementDescription = parsedStep.Description,
                });
            }

            db.TestCases.Add(testCase);
            await db.SaveChangesAsync(ct);

            // 可选：把新建的用例追加关联到测试计划（失败不拖垮导入，数字体现在返回结果里）
            var (planLinked, planSkipped) = await TestPlanLinker.LinkAsync(
                db, logger, request.TestPlanId, new[] { testCase.Id }, null, ct);

            return Results.Created($"/api/testcases/{testCase.Id}",
                new ScriptImportResult(testCase.Id, testCase.Name, testCase.Steps.Count,
                    parsed.Warnings, testCase.Type, planLinked, planSkipped));
        }).WithPermission(Permission.ManageTestCases).WithAudit("Import", "TestCase", captureBody: false);

        // 导出：把用例反写成 Playwright 脚本
        group.MapGet("/export/{testCaseId:guid}", async (
            Guid testCaseId, TestDbContext db, CancellationToken ct) =>
        {
            var testCase = await db.TestCases.AsNoTracking()
                .Include(t => t.Steps)
                .FirstOrDefaultAsync(t => t.Id == testCaseId, ct);
            if (testCase is null) return Results.NotFound();

            var script = PlaywrightScriptExporter.Export(testCase, testCase.Steps);
            return Results.Ok(new ScriptExportDto(testCase.Id, testCase.Name, "typescript", script));
        }).WithPermission(Permission.ViewTestCases).Produces<ScriptExportDto>();

        return group;
    }
}
