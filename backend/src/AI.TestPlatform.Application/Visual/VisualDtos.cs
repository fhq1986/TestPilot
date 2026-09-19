namespace AI.TestPlatform.Application.Visual;

/// <summary>视觉比对结果（由 AI Worker /api/visual/compare 返回）</summary>
public record VisualCompareResultDto(
    bool Ok,
    int BaselineWidth, int BaselineHeight,
    int ActualWidth, int ActualHeight,
    bool SizeMismatch,
    long TotalPixels, long DiffPixels,
    double DiffRatio, double Threshold, bool Passed,
    string Severity,
    IReadOnlyList<VisualRegionDto> Regions,
    string? DiffImageBase64);

/// <summary>差异区块（像素坐标）</summary>
public record VisualRegionDto(int X, int Y, int Width, int Height, long ChangedPixels, double Ratio);

/// <summary>差异的语义化说明（由视觉模型生成）</summary>
public record VisualDescribeResultDto(string Summary, string Risk, string Suggestion);

/// <summary>基线列表项</summary>
public record VisualBaselineDto(
    Guid Id, Guid TestCaseId, string TestCaseName, string? Module, int StepOrder,
    string ImageUrl, int Width, int Height, int CompareCount,
    DateTime? LastComparedAt, Guid? SourceExecutionId, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>把某次执行的截图接受为新基线</summary>
public record AcceptBaselineRequest(Guid ExecutionResultId);

/// <summary>用例的视觉回归配置与基线数量</summary>
public record VisualCaseSettingDto(
    Guid TestCaseId, string Name, bool VisualEnabled, double VisualThreshold, int BaselineCount);

/// <summary>
/// 视觉忽略区域（百分比 0~100，相对基线图尺寸）：x,y 为左上角，w,h 为宽高。
/// 用百分比而不是像素——视口尺寸变化时按比例缩放，比像素坐标更抗截图尺寸漂移。
/// 解析自用例的 VisualIgnoreRegions jsonb；解析失败静默清空（屏蔽失败只是"多算差异"，
/// 不能让整次视觉比对失败）。
/// </summary>
public sealed record VisualIgnoreRegion(double X, double Y, double W, double H)
{
    public static IReadOnlyList<VisualIgnoreRegion>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(json);
            if (list is null || list.Count == 0) return null;
            var regions = new List<VisualIgnoreRegion>();
            foreach (var item in list)
            {
                double Get(string name) =>
                    item.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number
                        ? v.GetDouble() : double.NaN;
                var (x, y, w, h) = (Get("x"), Get("y"), Get("w"), Get("h"));
                // 非数值 / 宽高非正的直接跳过：区域配置是手工填的，坏一条不能废全部
                if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(w) || double.IsNaN(h)) continue;
                if (w <= 0 || h <= 0) continue;
                regions.Add(new VisualIgnoreRegion(
                    Math.Clamp(x, 0, 100), Math.Clamp(y, 0, 100),
                    Math.Clamp(w, 0, 100), Math.Clamp(h, 0, 100)));
            }
            return regions;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
