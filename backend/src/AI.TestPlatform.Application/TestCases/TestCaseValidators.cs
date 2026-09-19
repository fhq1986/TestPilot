using AI.TestPlatform.Application.Common;
using FluentValidation;

namespace AI.TestPlatform.Application.TestCases;

public class CreateTestCaseRequestValidator : AbstractValidator<CreateTestCaseRequest>
{
    public CreateTestCaseRequestValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty().WithMessage("必须指定项目");
        RuleFor(x => x.Name).NotEmpty().WithMessage("用例名称不能为空")
            .MaximumLength(200).WithMessage("用例名称不能超过200个字符");
        RuleFor(x => x.Type).IsInEnum().WithMessage("用例类型无效");
        RuleFor(x => x.Browser).MaximumLength(50)
            .When(x => x.Browser != null).WithMessage("浏览器类型不能超过50个字符");
        RuleFor(x => x.BaseUrl).MaximumLength(500)
            .When(x => x.BaseUrl != null).WithMessage("BaseUrl 不能超过500个字符");
        RuleFor(x => x.Timeout).InclusiveBetween(1000, 600000)
            .WithMessage("超时时间必须在1000到600000毫秒之间");
        RuleFor(x => x.RetryCount).InclusiveBetween(0, 10)
            .WithMessage("重试次数必须在0到10之间");
        RuleFor(x => x.VisualThreshold)
            .Must(v => v is null || (v >= 0 && v <= 1)).WithMessage("视觉差异阈值必须在 0 到 1 之间");
        RuleFor(x => x.Steps).NotNull().WithMessage("步骤列表不能为空");
        RuleForEach(x => x.Steps).ChildRules(step =>
        {
            step.RuleFor(s => s.StepOrder).GreaterThanOrEqualTo(0)
                .WithMessage("步骤序号不能为负数");
            step.RuleFor(s => s.ActionType).IsInEnum().WithMessage("步骤动作类型无效");
        });
    }
}

public class UpdateTestCaseRequestValidator : AbstractValidator<UpdateTestCaseRequest>
{
    public UpdateTestCaseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("用例名称不能为空")
            .MaximumLength(200).WithMessage("用例名称不能超过200个字符");
        RuleFor(x => x.Status).IsInEnum().WithMessage("用例状态无效");
        RuleFor(x => x.Browser).MaximumLength(50)
            .When(x => x.Browser != null).WithMessage("浏览器类型不能超过50个字符");
        RuleFor(x => x.BaseUrl).MaximumLength(500)
            .When(x => x.BaseUrl != null).WithMessage("BaseUrl 不能超过500个字符");
        RuleFor(x => x.Timeout).InclusiveBetween(1000, 600000)
            .WithMessage("超时时间必须在1000到600000毫秒之间");
        RuleFor(x => x.RetryCount).InclusiveBetween(0, 10)
            .WithMessage("重试次数必须在0到10之间");
        RuleFor(x => x.VisualThreshold)
            .Must(v => v is null || (v >= 0 && v <= 1)).WithMessage("视觉差异阈值必须在 0 到 1 之间");
    }
}

public class UpdateTestCaseStepsRequestValidator : AbstractValidator<UpdateTestCaseStepsRequest>
{
    public UpdateTestCaseStepsRequestValidator()
    {
        RuleFor(x => x.Steps).NotNull().WithMessage("步骤列表不能为空");
        RuleFor(x => x.Steps)
            .Must(s => s == null || s.Select(x => x.StepOrder).Distinct().Count() == s.Count)
            .WithMessage("步骤序号不能重复");
        RuleForEach(x => x.Steps).ChildRules(step =>
        {
            step.RuleFor(s => s.StepOrder).GreaterThanOrEqualTo(0)
                .WithMessage("步骤序号不能为负数");
            step.RuleFor(s => s.ActionType).IsInEnum().WithMessage("步骤动作类型无效");
        });
    }
}


public class BatchUpdateRequestValidator : AbstractValidator<BatchUpdateRequest>
{
    private static readonly string[] Priorities = { "P0", "P1", "P2", "P3" };

    public BatchUpdateRequestValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().WithMessage("请选择要编辑的用例");
        // 全空 = 没有要改的字段：宁可 400 也别让一次"什么都没改"的请求污染版本历史
        RuleFor(x => x)
            .Must(x => x.Module != null || x.Priority != null || x.Status != null
                || x.ProjectId != null || x.RequirementId != null)
            .WithMessage("请至少填写一个要修改的字段");
        RuleFor(x => x.Module)
            .MaximumLength(50).WithMessage("模块名不能超过 50 个字符");
        RuleFor(x => x.Priority)
            .Must(v => v is null || Priorities.Contains(v))
            .WithMessage("优先级只能是 P0/P1/P2/P3");
    }
}
