using System.Text.Json.Serialization;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.AI;

public record GenerateCasesRequest(string Requirement, int MinCases);

public record GeneratedStepDto(
    int StepOrder, ActionType ActionType, StepConfig Config, string? AIElementDescription);

public record GeneratedCaseDto(
    string Name, string Priority, TestType Type, IReadOnlyList<GeneratedStepDto> Steps);

public record AdoptCaseRequest(
    string Name, string Priority, TestType Type, List<CreateTestStepRequest> Steps);

public record AdoptCasesRequest(Guid ProjectId, List<AdoptCaseRequest> Cases, string? AIPrompt);

public record AdoptCasesResponse(int Created, IReadOnlyList<Guid> CaseIds);

// Excel 用例导入：把「操作步骤 + 预期结果」文字批量转为可执行步骤
public record ImportCaseItemDto(
    string CaseCode, string Scenario, string? Module, string SourceSteps, string Expected);

public record ImportCaseStepsDto(string CaseCode, IReadOnlyList<GeneratedStepDto> Steps);

/// <summary>AI 定位候选元素。Placeholder 承载输入框/下拉的占位文本（如「默认当前用户」）——
/// el-select 的占位渲染在 span 里而非 input 的 placeholder 属性，分开收集才能让 LLM 匹配到。</summary>
public record InteractiveElement(int Index, string Tag, string? Id, string? Class, string Text, string? Aria, string? Placeholder = null);

public record LocateElementRequestDto(
    [property: JsonPropertyName("page_url")] string PageUrl, string Description, List<InteractiveElement> Elements);

public record LocateElementResultDto(int MatchedIndex, float Confidence, string Reasoning);

public record AIAssertResultDto(bool Passed, string Reason, float Confidence);

public record ImportSwaggerRequest(Guid ProjectId, string? Url, string? Content);

public record ImportSwaggerResponse(
    string ApiName, string? BaseUrl, int EndpointCount, int GeneratedCases,
    Guid ApiDefinitionId, List<GeneratedCaseDto> Cases,
    List<Dictionary<string, object>> EndpointSummaries);

public record AnalyzeApiFlowRequest(List<Dictionary<string, object>> Endpoints);

public record FailedStepEvidence(
    int StepOrder, string ActionType, string? ErrorMessage, string? Log, StepConfig? StepSnapshot);

public record SimilarCaseEvidence(int StepOrder, string ActionType, string ErrorMessage);

public record DiagnosisResultDto(
    string Category, string RootCause, float Confidence, string SuggestedFix, bool RetryRecommended);

// WebhookTriggerRequest 已迁移至 Application/CI/CiDtos.cs（CI 集成扩展了项目/模块筛选与构建上下文）

// ---------------------------------------------------------------- AI 聊天
public record ChatMessageDto(string Role, string Content);

public record ChatStreamRequestDto(List<ChatMessageDto> Messages, List<string>? Images = null);
