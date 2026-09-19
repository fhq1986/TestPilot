using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Application.Visual;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Visual;

/// <summary>
/// 视觉回归：把步骤截图与基线比对，判定「无变化 / 有变化 / 首次建立基线」。
///
/// 关键取舍：
/// - 基线以「用例 + 步骤」为粒度，只有通过状态的截图才可能成为基线（失败界面不该被固化）；
/// - 比对失败（Worker 未启动 / 图片损坏）只记为 Skipped，绝不因此把用例判失败；
/// - 判定为「变化」时把步骤置为 Failed（可在界面上一键接受变化、更新基线）；
/// - 与执行主流程解耦：服务自建 DI 作用域，避免把执行器正在跟踪的实体提前落库。
/// </summary>
public class VisualRegressionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScreenshotStorage _screenshots;
    private readonly AIClient _aiClient;
    private readonly ExecutionOptions _options;
    private readonly ILogger<VisualRegressionService> _logger;

    public VisualRegressionService(
        IServiceScopeFactory scopeFactory,
        ScreenshotStorage screenshots,
        AIClient aiClient,
        Microsoft.Extensions.Options.IOptions<ExecutionOptions> options,
        ILogger<VisualRegressionService> logger)
    {
        _scopeFactory = scopeFactory;
        _screenshots = screenshots;
        _aiClient = aiClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>对一个步骤结果执行视觉比对（原地修改 result；异常只记录，不抛出）</summary>
    public async Task ApplyAsync(global::AI.TestPlatform.Domain.Entities.Execution execution,
        TestCase testCase, TestStep step, ExecutionResult result, CancellationToken ct)
    {
        var threshold = testCase.VisualThreshold <= 0 ? 0.01 : testCase.VisualThreshold;
        result.VisualThreshold = threshold;

        var actualBase64 = await _screenshots.ReadBase64Async(result.ScreenshotUrl);
        if (actualBase64 is null)
        {
            result.VisualStatus = VisualStatus.Skipped;
            result.VisualNote = "未取得步骤截图，跳过视觉比对";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            // 基线按浏览器分存：优先取当前浏览器的基线，没有再兜底历史无浏览器基线
            var browser = execution.BrowserName;
            var baseline = await db.VisualBaselines
                .Where(b => b.TestCaseId == testCase.Id && b.StepOrder == step.StepOrder)
                .OrderBy(b => b.Browser == browser ? 0 : 1)
                .FirstOrDefaultAsync(ct);

            var baselineBase64 = baseline is null ? null : await _screenshots.ReadBase64Async(baseline.ImagePath);
            if (baseline is null || baselineBase64 is null || baseline.Width == 0)
            {
                // 建立基线前必须先过 BaselinePolicy：失败/报错的界面不能固化成基准。
                // 这条校验曾经缺失，导致"用例首次执行就失败"时错误页被存成基线，
                // 之后每次都跟错误页比、一直显示"无变化"，反而掩盖了真实问题。
                if (!BaselinePolicy.CanBecomeBaseline(result.Status, result.VisualStatus))
                {
                    result.VisualStatus = VisualStatus.Skipped;
                    result.VisualNote = baseline is null
                        ? $"{BaselinePolicy.DescribeRejection(result.Status)}，本次不建立视觉基线（避免把异常界面固化成基准）；该步骤通过后会自动建立"
                        : $"原基线文件缺失，且{BaselinePolicy.DescribeRejection(result.Status)}，暂不重建基线（避免把异常界面固化成基准）";
                    return;
                }

                // 首次执行或基线文件丢失：把本次截图固化为基线
                var created = CreateOrRefreshBaseline(db, testCase, step.StepOrder, result, actualBase64, execution.Id, browser, ct);
                result.VisualStatus = VisualStatus.BaselineCreated;
                result.VisualNote = baseline is null
                    ? "首次执行该步骤，已把本次截图存为视觉基线"
                    : "原基线文件缺失，已用本次截图重建基线";
                result.BaselineImageUrl = ScreenshotStorage.BaselineUrl(testCase.Id, step.StepOrder, browser);
                await created;
                return;
            }

            var comparison = await _aiClient.CompareImagesAsync(
                baselineBase64, actualBase64, threshold,
                _options.VisualPixelTolerance, _options.VisualMaxRegions,
                VisualIgnoreRegion.Parse(testCase.VisualIgnoreRegions), ct);

            result.BaselineImageUrl = baseline.ImagePath;
            result.VisualDiffRatio = comparison.DiffRatio;

            if (comparison.Passed)
            {
                result.VisualStatus = VisualStatus.Unchanged;
                if (comparison.SizeMismatch)
                    result.VisualNote = $"与基线一致（截图尺寸由 {baseline.Width}×{baseline.Height} 变为 " +
                                        $"{comparison.ActualWidth}×{comparison.ActualHeight}，已缩放比对）";
            }
            else
            {
                result.VisualStatus = VisualStatus.Changed;
                if (comparison.DiffImageBase64 is not null)
                {
                    try
                    {
                        result.DiffImageUrl = await _screenshots.SaveDiffAsync(
                            execution.Id, step.StepOrder, Convert.FromBase64String(comparison.DiffImageBase64), ct);
                    }
                    catch (FormatException ex)
                    {
                        _logger.LogWarning(ex, "差异图解码失败 步骤 {Order}", step.StepOrder);
                    }
                }

                var note = BuildNote(comparison, baseline, step);
                if (_options.VisualAiDescribe)
                    note = await AppendAiDescriptionAsync(note, testCase, step, baselineBase64, actualBase64,
                        comparison, ct);
                result.VisualNote = note;

                // 视觉差异视为步骤失败（保留原始错误信息，便于区分是视觉还是功能失败）
                if (result.Status == ExecutionStatus.Passed)
                {
                    result.Status = ExecutionStatus.Failed;
                    result.ErrorMessage =
                        $"视觉回归失败：与基线差异 {comparison.DiffRatio:P2}，超过阈值 {threshold:P2}";
                }
            }

            baseline.CompareCount++;
            baseline.LastComparedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // 视觉比对是增强能力，任何失败都不应影响主流程
            result.VisualStatus = VisualStatus.Skipped;
            result.VisualNote = $"视觉比对未完成：{ex.Message}";
            _logger.LogWarning(ex, "视觉比对失败 用例 {TestCaseId} 步骤 {Order}", testCase.Id, step.StepOrder);
        }
    }

    /// <summary>
    /// 把某次执行的步骤截图接受为新基线（界面上「接受变化」）。
    /// 返回 <c>(Dto, Error)</c>：成功时 Error 为空，失败时 Dto 为空并给出可读原因
    /// （原来是统一返回 null，端点只能回一句"结果不存在或没有截图"，把"为什么不让接受"吞掉了）。
    /// </summary>
    public async Task<(VisualBaselineDto? Baseline, string? Error)> AcceptAsync(
        Guid executionResultId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var result = await db.ExecutionResults.AsNoTracking()
            .Include(r => r.Execution).ThenInclude(e => e!.TestCase)
            .FirstOrDefaultAsync(r => r.Id == executionResultId, ct);
        if (result?.Execution?.TestCase is null || string.IsNullOrWhiteSpace(result.ScreenshotUrl))
            return (null, "该步骤结果不存在或没有可用截图");

        // 与自动建立基线共用同一条规矩：失败/报错的界面不能固化成基准。
        // 注意"视觉差异"导致的 Failed 是放行的（见 BaselinePolicy），否则这个按钮永远点不动。
        if (!BaselinePolicy.CanBecomeBaseline(result.Status, result.VisualStatus))
            return (null, $"{BaselinePolicy.DescribeRejection(result.Status)}，"
                          + "不接受为基线（失败界面固化成基准后，后续执行会与错误页比对，反而掩盖问题）");

        var testCase = result.Execution.TestCase;
        var browser = result.Execution?.BrowserName;
        if (!await _screenshots.PromoteToBaselineAsync(testCase.Id, result.StepOrder, result.ScreenshotUrl, browser, ct))
            return (null, "基线图片写入失败，请检查产物存储是否可用");

        var baselineBytes = await _screenshots.ReadBytesAsync(
            ScreenshotStorage.BaselineUrl(testCase.Id, result.StepOrder, browser), ct);
        var dimensions = baselineBytes is null
            ? (0, 0)
            : PngDimensions.Read(baselineBytes.AsSpan(0, Math.Min(24, baselineBytes.Length)));
        var baseline = await db.VisualBaselines
            .FirstOrDefaultAsync(b => b.TestCaseId == testCase.Id && b.StepOrder == result.StepOrder
                                      && b.Browser == browser, ct);
        if (baseline is null)
        {
            baseline = new VisualBaseline
            {
                TestCaseId = testCase.Id,
                StepOrder = result.StepOrder,
                Browser = browser,
            };
            db.VisualBaselines.Add(baseline);
        }
        baseline.ImagePath = ScreenshotStorage.BaselineUrl(testCase.Id, result.StepOrder, browser);
        baseline.FilePath = _screenshots.BaselineKey(testCase.Id, result.StepOrder, browser);
        baseline.Width = dimensions.Item1;
        baseline.Height = dimensions.Item2;
        baseline.SourceExecutionId = result.ExecutionId;
        baseline.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return (new VisualBaselineDto(baseline.Id, testCase.Id, testCase.Name, testCase.Module,
            baseline.StepOrder, baseline.ImagePath, baseline.Width, baseline.Height,
            baseline.CompareCount, baseline.LastComparedAt, baseline.SourceExecutionId,
            baseline.CreatedAt, baseline.UpdatedAt), null);
    }

    private async Task CreateOrRefreshBaseline(TestDbContext db, TestCase testCase, int stepOrder,
        ExecutionResult result, string actualBase64, Guid executionId, string? browser, CancellationToken ct)
    {
        if (!await _screenshots.PromoteToBaselineAsync(testCase.Id, stepOrder, result.ScreenshotUrl, browser, ct))
            return;

        var baselineBytes = await _screenshots.ReadBytesAsync(
            ScreenshotStorage.BaselineUrl(testCase.Id, stepOrder, browser), ct);
        var dimensions = baselineBytes is null
            ? (0, 0)
            : PngDimensions.Read(baselineBytes.AsSpan(0, Math.Min(24, baselineBytes.Length)));
        var baseline = await db.VisualBaselines
            .FirstOrDefaultAsync(b => b.TestCaseId == testCase.Id && b.StepOrder == stepOrder
                                      && b.Browser == browser, ct);
        if (baseline is null)
        {
            baseline = new VisualBaseline { TestCaseId = testCase.Id, StepOrder = stepOrder, Browser = browser };
            db.VisualBaselines.Add(baseline);
        }
        baseline.ImagePath = ScreenshotStorage.BaselineUrl(testCase.Id, stepOrder, browser);
        baseline.FilePath = _screenshots.BaselineKey(testCase.Id, stepOrder, browser);
        baseline.Width = dimensions.Item1;
        baseline.Height = dimensions.Item2;
        baseline.SourceExecutionId = executionId;
        baseline.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static string BuildNote(VisualCompareResultDto comparison, VisualBaseline baseline, TestStep step)
    {
        var parts = new List<string>
        {
            $"与基线差异 {comparison.DiffRatio:P2}（阈值 {comparison.Threshold:P2}，严重度 {TranslateSeverity(comparison.Severity)}）",
        };
        if (comparison.SizeMismatch)
            parts.Add($"截图尺寸由 {baseline.Width}×{baseline.Height} 变为 {comparison.ActualWidth}×{comparison.ActualHeight}");

        if (comparison.Regions.Count > 0)
        {
            var top = comparison.Regions[0];
            parts.Add($"变化 {comparison.Regions.Count} 处，最大区域位于 ({top.X},{top.Y}) {top.Width}×{top.Height}");
        }
        var description = step.AIElementDescription;
        if (!string.IsNullOrWhiteSpace(description))
            parts.Add($"步骤关注元素：{description}");
        return string.Join("；", parts);
    }

    private async Task<string> AppendAiDescriptionAsync(string note, TestCase testCase, TestStep step,
        string baselineBase64, string actualBase64, VisualCompareResultDto comparison, CancellationToken ct)
    {
        try
        {
            var described = await _aiClient.DescribeVisualDiffAsync(
                baselineBase64, actualBase64, comparison.DiffImageBase64,
                testCase.Name, DescribeStep(step), comparison.DiffRatio, comparison.Regions, ct);
            if (!string.IsNullOrWhiteSpace(described.Summary))
                note += $"；AI 判断：{described.Summary}";
            if (!string.IsNullOrWhiteSpace(described.Suggestion) &&
                !string.Equals(described.Risk, "low", StringComparison.OrdinalIgnoreCase))
                note += $"（建议：{described.Suggestion}）";
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "AI 视觉差异说明生成失败（不影响比对结论）");
        }
        return note;
    }

    private static string DescribeStep(TestStep step)
    {
        var target = step.Config?.Selector?.Description
                     ?? step.Config?.Selector?.Value
                     ?? step.Config?.Url
                     ?? step.Config?.Endpoint;
        return string.IsNullOrWhiteSpace(target)
            ? $"{step.StepOrder + 1}. {step.ActionType}"
            : $"{step.StepOrder + 1}. {step.ActionType} → {target}";
    }

    private static string TranslateSeverity(string severity) => severity switch
    {
        "high" => "高",
        "medium" => "中",
        _ => "低",
    };
}

/// <summary>从 PNG 的 IHDR 块读取宽高（避免为了拿尺寸引入图像库依赖）</summary>
public static class PngDimensions
{
    public static (int Width, int Height) ReadFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return (0, 0);
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24];
            if (stream.Read(header) < 24) return (0, 0);
            return Read(header);
        }
        catch (IOException)
        {
            return (0, 0);
        }
    }

    public static (int Width, int Height) Read(ReadOnlySpan<byte> header)
    {
        // PNG 签名(8) + 长度(4) + "IHDR"(4) + 宽(4) + 高(4)
        if (header.Length < 24) return (0, 0);
        if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47) return (0, 0);
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        return (width, height);
    }
}
