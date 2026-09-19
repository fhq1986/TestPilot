using AI.TestPlatform.Application.Executions;
using FluentValidation;

namespace AI.TestPlatform.Application.Suites;

public class CreateSuiteRequestValidator : AbstractValidator<CreateSuiteRequest>
{
    public CreateSuiteRequestValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty().WithMessage("必须指定所属项目");
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("套件名称不能为空")
            .MaximumLength(200).WithMessage("套件名称不能超过200个字符");
        RuleFor(x => x.Description).MaximumLength(1000).WithMessage("描述不能超过1000个字符");
        RuleFor(x => x.Kind).IsInEnum().WithMessage("套件类型不合法");
        RuleFor(x => x.FailurePolicy).IsInEnum().WithMessage("失败策略不合法");
        RuleFor(x => x.Cases)
            .Must(cases => cases is null || cases.Count <= MaxCases)
            .WithMessage($"单个套件最多包含 {MaxCases} 条用例");
    }

    private const int MaxCases = 500;
}

public class UpdateSuiteRequestValidator : AbstractValidator<UpdateSuiteRequest>
{
    public UpdateSuiteRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("套件名称不能为空")
            .MaximumLength(200).WithMessage("套件名称不能超过200个字符");
        RuleFor(x => x.Description).MaximumLength(1000).WithMessage("描述不能超过1000个字符");
        RuleFor(x => x.Kind).IsInEnum().WithMessage("套件类型不合法");
        RuleFor(x => x.FailurePolicy).IsInEnum().WithMessage("失败策略不合法");
        RuleFor(x => x.Cases)
            .Must(cases => cases is null || cases.Count <= MaxCases)
            .WithMessage($"单个套件最多包含 {MaxCases} 条用例");
    }

    private const int MaxCases = 500;
}

public class RunSuiteRequestValidator : AbstractValidator<RunSuiteRequest>
{
    public RunSuiteRequestValidator()
    {
        RuleForEach(x => x.Browsers)
            .Must(BrowserCatalog.IsRecognized)
            .When(x => x.Browsers is { Count: > 0 })
            .WithMessage("浏览器只能是 chromium / firefox / webkit（或其别名）");
        RuleFor(x => x.Browsers)
            .Must(browsers => browsers is null || browsers.Count <= BrowserCatalog.All.Count)
            .WithMessage("浏览器矩阵最多 3 个引擎");
    }
}
