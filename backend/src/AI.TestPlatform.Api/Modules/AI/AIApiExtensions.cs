using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Settings;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Application.ApiTesting;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.AI;

public static class AIApiExtensions
{
    public static RouteGroupBuilder MapAIApi(this RouteGroupBuilder group)
    {
        // 上传需求文档（.txt/.md/.docx）→ 提取纯文本，供前端填入「需求描述」后再生成用例
        group.MapPost("/extract-document", async (HttpRequest request, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { message = "请以 multipart/form-data 上传文档" });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { message = "未收到上传文件（字段名应为 file）" });

            await using var stream = file.OpenReadStream();
            try
            {
                var doc = DocumentTextExtractor.Extract(stream, file.FileName);
                return Results.Ok(new
                {
                    fileName = doc.FileName,
                    charCount = doc.CharCount,
                    text = doc.Text,
                    warnings = doc.Warnings,
                    supportedExtensions = DocumentTextExtractor.SupportedExtensions,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).WithPermission(Permission.ManageTestCases);

        group.MapPost("/generate-cases", async (
            GenerateCasesRequest request,
            IValidator<GenerateCasesRequest> validator,
            AIClient aiClient,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            try
            {
                var cases = await aiClient.GenerateAsync(request.Requirement, request.MinCases, ct);
                return Results.Ok(cases);
            }
            catch (AIWorkerException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: 502);
            }
        }).WithPermission(Permission.ManageTestCases);

        group.MapPost("/analyze-api-flow", async (
            AnalyzeApiFlowRequest request,
            IValidator<AnalyzeApiFlowRequest> validator,
            AIClient aiClient,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            try
            {
                var cases = await aiClient.AnalyzeApiFlowAsync(request, ct);
                return Results.Ok(cases);
            }
            catch (AIWorkerException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: 502);
            }
        }).WithPermission(Permission.ManageTestCases);

        group.MapPost("/adopt", async (
            AdoptCasesRequest request,
            IValidator<AdoptCasesRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var projectExists = await db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct);
            if (!projectExists)
                return Results.BadRequest(new { message = "项目不存在" });

            var cases = request.Cases.Select(c => new TestCase
            {
                ProjectId = request.ProjectId,
                Name = c.Name,
                Type = c.Type,
                Priority = c.Priority,
                AIGenerated = true,
                AIPrompt = request.AIPrompt,
                Status = TestCaseStatus.Active,
                Steps = c.Steps.Select(s => new TestStep
                {
                    StepOrder = s.StepOrder,
                    ActionType = s.ActionType,
                    Config = s.Config,
                    AIInstruction = s.AIInstruction,
                    AIElementDescription = s.AIElementDescription,
                }).ToList(),
            }).ToList();

            db.TestCases.AddRange(cases);
            await db.SaveChangesAsync(ct);

            return Results.Created("/api/ai/adopt",
                new AdoptCasesResponse(cases.Count, cases.Select(c => c.Id).ToList()));
        }).WithPermission(Permission.ManageTestCases).WithAudit("AdoptAI", "TestCase");

        group.MapPost("/import-swagger", async (
            ImportSwaggerRequest request,
            IValidator<ImportSwaggerRequest> validator,
            TestDbContext db,
            SwaggerImporter importer,
            SettingsService settingsService,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var projectExists = await db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct);
            if (!projectExists)
                return Results.BadRequest(new { message = "项目不存在" });

            string specJson;
            if (!string.IsNullOrWhiteSpace(request.Content))
            {
                specJson = request.Content;
            }
            else if (!string.IsNullOrWhiteSpace(request.Url))
            {
                if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url) || string.IsNullOrWhiteSpace(url.Host))
                    return Results.BadRequest(new { message = "无效的 Swagger URL" });
                var settings = await settingsService.GetAsync(ct);
                if (!settings.AllowPrivateNetworkImport && UrlSafety.IsPrivateHost(url.Host))
                    return Results.BadRequest(new { message = "不允许访问内网地址（系统配置中已禁用内网导入）" });

                try
                {
                    using var fetch = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    var bytes = await fetch.GetByteArrayAsync(request.Url, ct);
                    if (bytes.Length > 2 * 1024 * 1024)
                        return Results.BadRequest(new { message = "抓取的 Swagger 超过 2MB 限制" });
                    specJson = System.Text.Encoding.UTF8.GetString(bytes);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { message = $"抓取 Swagger 失败: {ex.Message}" });
                }
            }
            else
            {
                return Results.BadRequest(new { message = "请提供 Swagger JSON 内容或 URL" });
            }

            List<ApiEndpointSpec> endpoints;
            string apiName; string? baseUrl;
            try
            {
                (apiName, baseUrl, endpoints) = importer.Parse(specJson);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            if (endpoints.Count == 0)
                return Results.BadRequest(new { message = "未解析到任何端点" });

            var definition = new ApiDefinition
            {
                ProjectId = request.ProjectId,
                Name = apiName,
                BaseUrl = baseUrl,
                Spec = specJson,
                EndpointCount = endpoints.Count,
            };
            db.ApiDefinitions.Add(definition);
            await db.SaveChangesAsync(ct);

            var generated = endpoints
                .Take(30)
                .SelectMany(e => BoundaryCaseGenerator.Generate(e))
                .Take(100)
                .Select(g => new GeneratedCaseDto(
                    g.Name, g.Priority, TestType.Api,
                    g.Steps.Select(s => new GeneratedStepDto(s.StepOrder, s.ActionType, s.Config, s.AIElementDescription)).ToList()))
                .ToList();

            var summaries = endpoints.Take(30).Select(e => new Dictionary<string, object>
            {
                ["method"] = e.Method,
                ["path"] = e.Path,
                ["summary"] = e.Path,
                ["response_codes"] = e.ResponseCodes,
            }).ToList();

            return Results.Ok(new ImportSwaggerResponse(
                apiName, baseUrl, endpoints.Count, generated.Count, definition.Id, generated, summaries));
        }).WithPermission(Permission.ManageTestCases).WithAudit("ImportSwagger", "ApiDefinition");

        return group;
    }
}
