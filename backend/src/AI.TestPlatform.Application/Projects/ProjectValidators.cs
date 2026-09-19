using FluentValidation;

namespace AI.TestPlatform.Application.Projects;

public class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("项目名称不能为空")
            .MaximumLength(100).WithMessage("项目名称不能超过100个字符");
        RuleFor(x => x.Description).MaximumLength(2000)
            .When(x => x.Description != null).WithMessage("项目描述不能超过2000个字符");
    }
}

public class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("项目名称不能为空")
            .MaximumLength(100).WithMessage("项目名称不能超过100个字符");
        RuleFor(x => x.Description).MaximumLength(2000)
            .When(x => x.Description != null).WithMessage("项目描述不能超过2000个字符");
    }
}
