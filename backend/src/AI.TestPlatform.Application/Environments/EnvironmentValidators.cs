using FluentValidation;

namespace AI.TestPlatform.Application.Environments;

public class CreateEnvironmentRequestValidator : AbstractValidator<CreateEnvironmentRequest>
{
    public CreateEnvironmentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("环境名称不能为空")
            .MaximumLength(100).WithMessage("环境名称不能超过100个字符");
        RuleFor(x => x.BaseUrl).NotEmpty().WithMessage("被测系统地址不能为空")
            .MaximumLength(500).WithMessage("被测系统地址不能超过500个字符")
            .Must(v => Uri.TryCreate(v, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"))
            .WithMessage("被测系统地址必须是 http 或 https 的合法 URL");
        RuleFor(x => x.LoginUrl)
            .Must(v => v is null || v.Length <= 500)
            .WithMessage("登录页地址不能超过500个字符")
            .Must(v => v is null || string.IsNullOrEmpty(v) ||
                Uri.TryCreate(v, UriKind.RelativeOrAbsolute, out _))
            .WithMessage("登录页地址必须是合法 URL");
        RuleFor(x => x.LoginUsername)
            .Must(v => v is null || v.Length <= 200)
            .WithMessage("登录账号不能超过200个字符");
        RuleFor(x => x.LoginPassword)
            .Must(v => v is null || v.Length <= 200)
            .WithMessage("登录密码不能超过200个字符");
        RuleFor(x => x.LoginSuccessIndicator)
            .Must(v => v is null || v.Length <= 300)
            .WithMessage("登录成功标志不能超过300个字符");
    }
}

public class UpdateEnvironmentRequestValidator : AbstractValidator<UpdateEnvironmentRequest>
{
    public UpdateEnvironmentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("环境名称不能为空")
            .MaximumLength(100).WithMessage("环境名称不能超过100个字符");
        RuleFor(x => x.BaseUrl).NotEmpty().WithMessage("被测系统地址不能为空")
            .MaximumLength(500).WithMessage("被测系统地址不能超过500个字符")
            .Must(v => Uri.TryCreate(v, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"))
            .WithMessage("被测系统地址必须是 http 或 https 的合法 URL");
        RuleFor(x => x.LoginUrl)
            .Must(v => v is null || v.Length <= 500)
            .WithMessage("登录页地址不能超过500个字符")
            .Must(v => v is null || string.IsNullOrEmpty(v) ||
                Uri.TryCreate(v, UriKind.RelativeOrAbsolute, out _))
            .WithMessage("登录页地址必须是合法 URL");
        RuleFor(x => x.LoginUsername)
            .Must(v => v is null || v.Length <= 200)
            .WithMessage("登录账号不能超过200个字符");
        RuleFor(x => x.LoginPassword)
            .Must(v => v is null || v.Length <= 200)
            .WithMessage("登录密码不能超过200个字符");
        RuleFor(x => x.LoginSuccessIndicator)
            .Must(v => v is null || v.Length <= 300)
            .WithMessage("登录成功标志不能超过300个字符");
    }
}
