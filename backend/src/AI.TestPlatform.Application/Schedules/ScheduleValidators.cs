using FluentValidation;

namespace AI.TestPlatform.Application.Schedules;

public class CreateScheduleRequestValidator : AbstractValidator<CreateScheduleRequest>
{
    public CreateScheduleRequestValidator()
    {
        RuleFor(x => x.ProjectId)
            .NotEmpty().WithMessage("必须指定所属项目");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("任务名称不能为空")
            .MaximumLength(200).WithMessage("任务名称不能超过200个字符");
        RuleFor(x => x.CronExpression)
            .NotEmpty().WithMessage("Cron 表达式不能为空")
            .Must(CronUtils.IsValid).WithMessage("Cron 表达式非法，请填写 5 段格式（如 0 2 * * *）");
        RuleFor(x => x.Module)
            .MaximumLength(100).WithMessage("模块不能超过100个字符");
        RuleFor(x => x.Priority)
            .MaximumLength(10).WithMessage("优先级不能超过10个字符");
        RuleFor(x => x.TestCaseIds)
            .Must(ids => ids is null || ids.Count <= 500)
            .WithMessage("单个定时任务最多指定 500 个用例");
    }
}

public class UpdateScheduleRequestValidator : AbstractValidator<UpdateScheduleRequest>
{
    public UpdateScheduleRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("任务名称不能为空")
            .MaximumLength(200).WithMessage("任务名称不能超过200个字符");
        RuleFor(x => x.CronExpression)
            .NotEmpty().WithMessage("Cron 表达式不能为空")
            .Must(CronUtils.IsValid).WithMessage("Cron 表达式非法，请填写 5 段格式（如 0 2 * * *）");
        RuleFor(x => x.Module)
            .MaximumLength(100).WithMessage("模块不能超过100个字符");
        RuleFor(x => x.Priority)
            .MaximumLength(10).WithMessage("优先级不能超过10个字符");
        RuleFor(x => x.TestCaseIds)
            .Must(ids => ids is null || ids.Count <= 500)
            .WithMessage("单个定时任务最多指定 500 个用例");
    }
}

public class CronPreviewRequestValidator : AbstractValidator<CronPreviewRequest>
{
    public CronPreviewRequestValidator()
    {
        RuleFor(x => x.CronExpression)
            .NotEmpty().WithMessage("Cron 表达式不能为空")
            .Must(CronUtils.IsValid).WithMessage("Cron 表达式非法，请填写 5 段格式（如 0 2 * * *）");
        RuleFor(x => x.Count)
            .InclusiveBetween(1, 20).WithMessage("预览次数必须在1到20之间");
    }
}
