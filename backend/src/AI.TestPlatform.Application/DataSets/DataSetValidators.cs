using FluentValidation;

namespace AI.TestPlatform.Application.DataSets;

public class CreateDataSetRequestValidator : AbstractValidator<CreateDataSetRequest>
{
    public CreateDataSetRequestValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty().WithMessage("必须指定所属项目");
        DataSetRules.Apply(this, x => x.Name, x => x.Columns, x => x.Rows);
    }
}

public class UpdateDataSetRequestValidator : AbstractValidator<UpdateDataSetRequest>
{
    public UpdateDataSetRequestValidator()
    {
        DataSetRules.Apply(this, x => x.Name, x => x.Columns, x => x.Rows);
    }
}

/// <summary>绑定/解绑数据集：DataSetId 允许为空（解绑）</summary>
public class AttachDataSetRequestValidator : AbstractValidator<AttachDataSetRequest>
{
}

/// <summary>
/// 数据集内容规则（新增与修改共用）：列名合法性 + 行数/单元格长度上限。
/// 通过「传入访问器」的方式复用，避免为了共享规则把两个 DTO 硬凑成一个类型。
/// </summary>
internal static class DataSetRules
{
    public const int MaxColumns = 50;
    /// <summary>单个数据集最大行数（一次执行会展开成 N 条执行记录，需要克制）</summary>
    public const int MaxRows = 500;
    public const int MaxCellLength = 2000;

    public static void Apply<T>(AbstractValidator<T> validator,
        Func<T, string?> name,
        Func<T, List<string>?> columns,
        Func<T, List<Dictionary<string, string>>?> rows)
    {
        validator.RuleFor(x => name(x))
            .NotEmpty().WithMessage("数据集名称不能为空")
            .MaximumLength(200).WithMessage("数据集名称不能超过200个字符")
            .OverridePropertyName("name");

        validator.RuleFor(x => columns(x))
            .NotNull().WithMessage("必须定义列")
            .Must(c => c is { Count: > 0 }).WithMessage("必须至少定义一列")
            .Must(c => c is null || c.Count <= MaxColumns).WithMessage($"列数不能超过 {MaxColumns}")
            .OverridePropertyName("columns");

        validator.RuleFor(x => columns(x))
            .Must(c => c is null || c.Select(column => column?.Trim() ?? string.Empty).Distinct().Count() == c.Count)
            .WithMessage("列名不能重复")
            .OverridePropertyName("columns");

        validator.RuleFor(x => columns(x))
            .Must(c => c is null || c.All(IsValidColumnName))
            .WithMessage("列名不能为空、不能超过50个字符、不能包含 {{ }} 或逗号")
            .OverridePropertyName("columns");

        validator.RuleFor(x => rows(x))
            .NotNull().WithMessage("数据行不能为 null（无数据请传空数组）")
            .Must(r => r is null || r.Count <= MaxRows).WithMessage($"数据行数不能超过 {MaxRows}")
            .OverridePropertyName("rows");

        validator.RuleFor(x => rows(x))
            // 需要用同一条请求里的列定义来判断行内容，故用 (实例, 值) 重载
            .Must((instance, r) => IsRowContentValid(columns(instance), r))
            .WithMessage($"每行只能包含已定义的列，且单元格不能超过 {MaxCellLength} 个字符")
            .OverridePropertyName("rows");
    }

    private static bool IsValidColumnName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        if (trimmed.Length > 50) return false;
        if (trimmed.Contains('{') || trimmed.Contains('}') || trimmed.Contains(',')) return false;
        return true;
    }

    private static bool IsRowContentValid(List<string>? columns, List<Dictionary<string, string>>? rows)
    {
        if (rows is null) return true;
        var allowed = (columns ?? new List<string>())
            .Select(c => c?.Trim() ?? string.Empty)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row is null) continue;
            foreach (var (key, value) in row)
            {
                if (!string.IsNullOrWhiteSpace(key) && !allowed.Contains(key.Trim())) return false;
                if (value is not null && value.Length > MaxCellLength) return false;
            }
        }
        return true;
    }
}
