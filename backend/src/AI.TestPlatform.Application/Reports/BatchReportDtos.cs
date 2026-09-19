using FluentValidation;

namespace AI.TestPlatform.Application.Reports;

/// <summary>批量导出选中执行记录的测试报告</summary>
public record BatchReportRequest(List<Guid> ExecutionIds);

public class BatchReportRequestValidator : AbstractValidator<BatchReportRequest>
{
    public BatchReportRequestValidator()
    {
        RuleFor(x => x.ExecutionIds)
            .NotNull().WithMessage("必须选择要导出的执行记录")
            .NotEmpty().WithMessage("必须选择要导出的执行记录")
            .Must(ids => ids!.Count <= 200).WithMessage("单次最多导出 200 条执行记录");
    }
}
