using AI.TestPlatform.Application.CI;
using FluentValidation;

namespace AI.TestPlatform.Application.CI;

public class WebhookTriggerRequestValidator : AbstractValidator<WebhookTriggerRequest>
{
    /// <summary>单次 CI 触发的用例上限，防止流水线误触发压垮执行队列</summary>
    public const int MaxCases = 200;

    public WebhookTriggerRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.TestCaseIds is { Count: > 0 } || x.ProjectId.HasValue)
            .WithMessage("必须指定用例列表（testCaseIds），或指定项目（projectId）以按模块/优先级筛选");

        RuleFor(x => x.TestCaseIds)
            .Must(ids => ids is null || ids.Count <= MaxCases)
            .WithMessage($"单次触发不能超过 {MaxCases} 条用例");

        RuleFor(x => x.Module)
            .Must(v => v is null || v.Length <= 100).WithMessage("模块不能超过100个字符");
        RuleFor(x => x.Priority)
            .Must(v => v is null || v.Length <= 10).WithMessage("优先级不能超过10个字符");
        RuleFor(x => x.CommitSha)
            .Must(v => v is null || v.Length <= 64).WithMessage("提交号不能超过64个字符");
        RuleFor(x => x.Branch)
            .Must(v => v is null || v.Length <= 200).WithMessage("分支名不能超过200个字符");
        RuleFor(x => x.BuildNumber)
            .Must(v => v is null || v.Length <= 100).WithMessage("构建号不能超过100个字符");
        RuleFor(x => x.Source)
            .Must(v => v is null || v.Length <= 100).WithMessage("触发来源不能超过100个字符");
    }
}
