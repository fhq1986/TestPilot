namespace AI.TestPlatform.Application.SharedSteps;

/// <summary>引用某共享步骤组的用例（GET /shared-steps/{id}/usages）：删除前给用户看清楚影响面</summary>
public record SharedStepUsageDto(Guid TestCaseId, string Name, List<int> StepOrders);
