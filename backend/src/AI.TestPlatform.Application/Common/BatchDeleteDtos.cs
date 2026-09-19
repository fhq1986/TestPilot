using FluentValidation;

namespace AI.TestPlatform.Application.Common;

/// <summary>通用批量删除请求（项目 / 用例 / 执行记录共用）。</summary>
public record BatchDeleteRequest(List<Guid> Ids);

/// <summary>被跳过的条目及原因（例如项目下仍有用例、执行任务进行中）。</summary>
public record BatchDeleteSkippedItem(Guid Id, string? Name, string Reason);

public record BatchDeleteResultDto(int Deleted, List<BatchDeleteSkippedItem> Skipped);

public class BatchDeleteRequestValidator : AbstractValidator<BatchDeleteRequest>
{
    /// <summary>单次批量删除上限，避免一次请求锁表过久。</summary>
    public const int MaxIds = 200;

    public BatchDeleteRequestValidator()
    {
        RuleFor(x => x.Ids)
            .NotNull().WithMessage("必须指定要删除的记录")
            .NotEmpty().WithMessage("必须指定要删除的记录")
            .Must(ids => ids!.Count <= MaxIds).WithMessage($"单次批量删除不能超过 {MaxIds} 条");
    }
}
