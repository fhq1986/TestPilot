namespace AI.TestPlatform.Application.Common;

/// <summary>评审动作：approve（批准，意见可选）/ reject（驳回，意见必填）。</summary>
public record ReviewActionRequest(string Action, string? Note);
