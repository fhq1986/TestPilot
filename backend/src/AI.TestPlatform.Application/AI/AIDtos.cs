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
    int StepOrder, string ActionType, string? ErrorMessage, string? Log, StepConfig? StepSnapshot,
    /// <summary>M8：失败时的页面可交互元素快照（jsonb 原样带出），供归因产出具体定位符</summary>
    string? Elements = null);

public record SimilarCaseEvidence(int StepOrder, string ActionType, string ErrorMessage);

public record DiagnosisResultDto(
    string Category, string RootCause, float Confidence, string SuggestedFix, bool RetryRecommended);

// ---------------------------------------------------------------- M8 Agent 自愈闭环（见 docs/m8-agent-design.md）

/// <summary>
/// 一条结构化修复动作。ActionType 取自白名单
/// （update_locator / wait_strategy / step_config_patch / add_step / delete_step / reorder_step / relax_assert）。
/// </summary>
public record FixActionDto(
    string ActionType, int? StepOrder,
    Dictionary<string, object> Params, float Confidence);

/// <summary>
/// Attributer 的结构化归因结果。
/// ⚠ 下游判定**一律以 <see cref="FixCategory"/> 为准**；<see cref="Category"/> 仅作人类可读展示与兼容。
/// </summary>
public record AttributedResultDto(
    string Category, string RootCause, float Confidence,
    string SuggestedFix, bool RetryRecommended,
    FixCategory FixCategory, IReadOnlyList<FixActionDto> ProposedFixes,
    bool NeedsHumanApproval, string? ApprovalReason);

/// <summary>Planner（Phase 3）的输出：步骤序列 + 整体置信度 + 识别到的风险点。</summary>
public record PlanResultDto(
    IReadOnlyList<GeneratedCaseDto> Cases, float OverallConfidence, IReadOnlyList<string> Risks);

// WebhookTriggerRequest 已迁移至 Application/CI/CiDtos.cs（CI 集成扩展了项目/模块筛选与构建上下文）

/// <summary>语义向量化配置：透传给 AIWorker，再由其转发到 OpenAI 兼容的 /v1/embeddings。
/// 留空字段由 AIWorker 的 EMBEDDING_* 环境变量兜底。</summary>
public record EmbeddingConfigDto(string? BaseUrl, string? ApiKey, string? Model);

// ---------------------------------------------------------------- AI 聊天
public record ChatMessageDto(string Role, string Content);

public record ChatStreamRequestDto(List<ChatMessageDto> Messages, List<string>? Images = null);
