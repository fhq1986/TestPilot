using FluentValidation;

namespace AI.TestPlatform.Application.AI;

public class GenerateCasesRequestValidator : AbstractValidator<GenerateCasesRequest>
{
    public GenerateCasesRequestValidator()
    {
        RuleFor(x => x.Requirement).NotEmpty().WithMessage("需求描述不能为空")
            .MinimumLength(10).WithMessage("需求描述至少10个字符")
            .MaximumLength(20000).WithMessage("需求描述不能超过20000个字符");
        RuleFor(x => x.MinCases).InclusiveBetween(1, 20).WithMessage("生成数量必须在1到20之间");
    }
}

public class AdoptCasesRequestValidator : AbstractValidator<AdoptCasesRequest>
{
    public AdoptCasesRequestValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty().WithMessage("必须指定项目");
        RuleFor(x => x.Cases).NotNull().WithMessage("用例列表不能为空");
        RuleFor(x => x.Cases)
            .Must(c => c is { Count: > 0 }).WithMessage("用例列表不能为空");
        RuleForEach(x => x.Cases).ChildRules(c =>
        {
            c.RuleFor(x => x.Name).NotEmpty().WithMessage("用例名称不能为空")
                .MaximumLength(200);
            c.RuleFor(x => x.Priority).Must(p => p is "P0" or "P1" or "P2" or "P3")
                .WithMessage("优先级必须是 P0-P3");
        });
    }
}

public class ImportSwaggerRequestValidator : AbstractValidator<ImportSwaggerRequest>
{
    private const int MaxContentBytes = 2 * 1024 * 1024;

    public ImportSwaggerRequestValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty().WithMessage("必须指定项目");
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Content) || !string.IsNullOrWhiteSpace(x.Url))
            .WithMessage("必须提供 Swagger JSON 内容或 URL");
        RuleFor(x => x.Content)
            .Must(c => c is null || System.Text.Encoding.UTF8.GetByteCount(c) <= MaxContentBytes)
            .WithMessage("Swagger JSON 内容不能超过2MB");
    }
}

// WebhookTriggerRequestValidator 已迁移至 Application/CI/CiValidators.cs

public class AnalyzeApiFlowRequestValidator : AbstractValidator<AnalyzeApiFlowRequest>
{
    public AnalyzeApiFlowRequestValidator()
    {
        RuleFor(x => x.Endpoints).NotNull().WithMessage("接口清单不能为空");
        RuleFor(x => x.Endpoints)
            .Must(e => e is { Count: > 0 }).WithMessage("接口清单不能为空");
        RuleFor(x => x.Endpoints)
            .Must(e => e is { Count: <= 50 }).WithMessage("接口清单不能超过50个端点");
    }
}

public class ChatStreamRequestValidator : AbstractValidator<ChatStreamRequestDto>
{
    private static readonly string[] Roles = { "system", "user", "assistant" };

    public ChatStreamRequestValidator()
    {
        RuleFor(x => x.Messages)
            .NotNull().WithMessage("消息不能为空")
            .NotEmpty().WithMessage("消息不能为空")
            .Must(m => m!.Count <= 60).WithMessage("单次对话上下文不能超过 60 条消息");

        RuleForEach(x => x.Messages).ChildRules(message =>
        {
            message.RuleFor(m => m.Role)
                .Must(r => Roles.Contains(r)).WithMessage("消息角色只能是 system/user/assistant");
            message.RuleFor(m => m.Content)
                .MaximumLength(20000).WithMessage("单条消息不能超过 20000 字");
        });

        RuleFor(x => x.Images)
            .Must(images => images is null || images.Count <= 6)
            .WithMessage("单次最多上传 6 张截图");
    }
}
