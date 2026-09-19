using AI.TestPlatform.Domain.Entities;
using FluentValidation;

namespace AI.TestPlatform.Application.Mocks;

public record CreateMockRequest(Guid ProjectId, string Name, string Spec);

public record MockDto(Guid Id, Guid ProjectId, string Name, int? Port, MockStatus Status, DateTime CreatedAt);

public class CreateMockRequestValidator : AbstractValidator<CreateMockRequest>
{
    private const int MaxSpecBytes = 2 * 1024 * 1024;

    public CreateMockRequestValidator()
    {
        RuleFor(x => x.ProjectId).NotEmpty().WithMessage("必须指定项目");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Mock 名称不能为空")
            .MaximumLength(200).WithMessage("Mock 名称不能超过200个字符");
        RuleFor(x => x.Spec).NotEmpty().WithMessage("Mock Spec 不能为空");
        RuleFor(x => x.Spec)
            .Must(s => System.Text.Encoding.UTF8.GetByteCount(s) <= MaxSpecBytes)
            .WithMessage("Mock Spec 不能超过2MB");
    }
}
