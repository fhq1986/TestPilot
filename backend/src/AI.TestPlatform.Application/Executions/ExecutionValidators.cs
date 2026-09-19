using FluentValidation;

namespace AI.TestPlatform.Application.Executions;

public class CreateExecutionRequestValidator : AbstractValidator<CreateExecutionRequest>
{
    public CreateExecutionRequestValidator()
    {
        RuleFor(x => x.TestCaseId).NotEmpty().WithMessage("必须指定测试用例");
    }
}

public class BatchExecuteRequestValidator : AbstractValidator<BatchExecuteRequest>
{
    public BatchExecuteRequestValidator()
    {
        RuleFor(x => x.TestCaseIds)
            .NotNull().WithMessage("必须指定测试用例列表")
            .NotEmpty().WithMessage("必须指定测试用例列表")
            .Must(ids => ids!.Count <= 100).WithMessage("单次批量执行不能超过 100 条用例");
    }
}
