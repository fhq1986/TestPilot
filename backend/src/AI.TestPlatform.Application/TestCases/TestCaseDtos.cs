using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.TestCases;

/// <summary>
/// 用例列表「按最近执行结果筛选」的取值。
///
/// **为什么不直接用 <see cref="ExecutionStatus"/>**：两者不是一回事。
/// - 筛选里的「执行中」是**一个桶**，要同时含 Pending（排队）与 Running（在跑）——
///   对"哪些还在跑"这个问题来说，排队和在跑没有区别；
/// - 「未执行」在 <see cref="ExecutionStatus"/> 里**根本不存在**（没有执行记录时状态为 null）。
///
/// 取值是**最近一次执行**的结果，与列表「最近执行结果」列同源；
/// 不能用「历史上有过某个状态」来筛，否则会出现"列里显示通过、却被失败筛出来"。
/// </summary>
public enum CaseExecFilter
{
    /// <summary>从未执行过（没有任何执行记录）</summary>
    Never = 0,

    /// <summary>执行中：排队中 + 正在跑</summary>
    Running = 1,

    Passed = 2,
    Failed = 3,

    /// <summary>用例本身跑挂了（环境/脚本异常），与「断言不通过」的 Failed 分开</summary>
    Error = 4,

    Skipped = 5,
}

public record CreateTestStepRequest(
    int StepOrder, ActionType ActionType, StepConfig Config,
    string? AIInstruction, string? AIElementDescription,
    // 迭代 C：共享步骤引用（与 UpdateTestStepRequest 保持同构，前端可复用同一份表单模型）
    Guid? SharedGroupId = null,
    List<SharedVariableEntry>? SharedVariables = null);

public record CreateTestCaseRequest(
    Guid ProjectId, string Name, TestType Type, string? Description,
    string? Browser, int Timeout, int RetryCount, List<CreateTestStepRequest> Steps,
    string? BaseUrl, bool FailFast = false,
    string? CaseCode = null, string? Module = null,
    string? SourceSteps = null, string? ExpectedResult = null,
    string? Priority = null,
    // 迭代 B：视觉回归与参数化
    bool VisualEnabled = false, double? VisualThreshold = null, string? VisualIgnoreRegions = null, string? CustomFields = null, Guid? DataSetId = null,
    CaseReviewStatus ReviewStatus = CaseReviewStatus.None, DateTime? ReviewedAt = null, string? ReviewNote = null, string? ReviewedByName = null,
    // 需求覆盖：用例挂到需求（可空）
    Guid? RequirementId = null,
    /// <summary>用例级网络规则（JSON 数组，见 NetworkRuleSet）；传 null 表示不拦截</summary>
    string? NetworkRules = null);

public record UpdateTestCaseRequest(
    string Name, string? Description, TestCaseStatus Status,
    string? Browser, int Timeout, int RetryCount, string? BaseUrl, bool FailFast = false,
    string? CaseCode = null, string? Module = null,
    string? SourceSteps = null, string? ExpectedResult = null,
    string? Priority = null,
    bool VisualEnabled = false, double? VisualThreshold = null, string? VisualIgnoreRegions = null, string? CustomFields = null, Guid? DataSetId = null,
    CaseReviewStatus ReviewStatus = CaseReviewStatus.None, DateTime? ReviewedAt = null, string? ReviewNote = null, string? ReviewedByName = null,
    Guid? RequirementId = null,
    /// <summary>用例级网络规则（JSON 数组，见 NetworkRuleSet）；传 null 表示清空规则</summary>
    string? NetworkRules = null);

/// <summary>版本历史列表项。**刻意不带完整快照**——列表只需要"第几版、谁改的、改了什么"</summary>
public record TestCaseVersionSummaryDto(
    int Version, DateTime CreatedAt, string? OperatorName, string? ChangeSummary, int StepCount);

/// <summary>单个版本的完整快照（查看与对比用）</summary>
public record TestCaseVersionDetailDto(
    int Version, DateTime CreatedAt, string? OperatorName, string? ChangeSummary, TestCaseSnapshot Snapshot);

public record TestStepDto(
    Guid Id, int StepOrder, ActionType ActionType, StepConfig Config,
    string? AIInstruction, string? AIElementDescription,
    // 迭代 C：共享步骤引用。非空时本步骤是占位，运行时展开成组内步骤
    Guid? SharedGroupId = null,
    string? SharedGroupName = null,
    List<SharedVariableEntry>? SharedVariables = null);

public record TestCaseSummaryDto(
    Guid Id, Guid ProjectId, string Name, TestType Type, string? Description,
    bool AIGenerated, string? Browser, int Timeout, int RetryCount,
    int Version, TestCaseStatus Status, DateTime CreatedAt, DateTime UpdatedAt,
    string? BaseUrl, bool FailFast = false,
    string? CaseCode = null, string? Module = null, string? Priority = null,
    // 迭代 A：不稳定用例标记（近 10 次执行结果抖动）
    bool IsFlaky = false, double FlakeRate = 0,
    // 迭代 B：视觉回归与参数化
    bool VisualEnabled = false, double VisualThreshold = 0.01, string? VisualIgnoreRegions = null, string? CustomFields = null, Guid? DataSetId = null,
    CaseReviewStatus ReviewStatus = CaseReviewStatus.None, DateTime? ReviewedAt = null, string? ReviewNote = null, string? ReviewedByName = null,
    // 需求覆盖
    Guid? RequirementId = null, string? RequirementTitle = null,
    /// <summary>所属项目名。列表默认是「全部项目」，跨项目看时得知道每条用例属于谁；
    /// 只给 ProjectId 的话前端还得再查一次项目</summary>
    string? ProjectName = null,
    /// <summary>最近一次执行的状态；**为 null 表示从未执行过**。
    /// 列表的「最近执行结果」列用它——比"草稿/启用"更能说明这条用例现在到底行不行</summary>
    ExecutionStatus? LatestExecutionStatus = null,
    /// <summary>最近一次执行的时间，与 <see cref="LatestExecutionStatus"/> 配套展示</summary>
    DateTime? LastExecutedAt = null,
    /// <summary>创建人显示名（M8 审计字段）</summary>
    string? CreatedByName = null);

public record TestCaseDto(
    Guid Id, Guid ProjectId, string Name, TestType Type, string? Description,
    bool AIGenerated, string? Browser, int Timeout, int RetryCount,
    int Version, TestCaseStatus Status, DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<TestStepDto> Steps, string? BaseUrl, bool FailFast = false,
    string? CaseCode = null, string? Module = null,
    string? SourceSteps = null, string? ExpectedResult = null,
    string? Priority = null,
    bool IsFlaky = false, double FlakeRate = 0, DateTime? FlakeCheckedAt = null,
    bool VisualEnabled = false, double VisualThreshold = 0.01, string? VisualIgnoreRegions = null, string? CustomFields = null, Guid? DataSetId = null,
    CaseReviewStatus ReviewStatus = CaseReviewStatus.None, DateTime? ReviewedAt = null, string? ReviewNote = null, string? ReviewedByName = null,
    Guid? RequirementId = null, string? RequirementTitle = null,
    /// <summary>所属项目名（详情页展示用；仅在调用方已加载 Project 导航时有值）</summary>
    string? ProjectName = null,
    /// <summary>用例级网络规则（JSON 数组）。列表里**不带**（体积大且列表用不上），只有详情带</summary>
    string? NetworkRules = null);

public static class TestCaseMappingExtensions
{
    public static TestCaseDto ToDto(this TestCase tc) => new(
        tc.Id, tc.ProjectId, tc.Name, tc.Type, tc.Description, tc.AIGenerated,
        tc.Browser, tc.Timeout, tc.RetryCount, tc.Version, tc.Status,
        tc.CreatedAt, tc.UpdatedAt,
        tc.Steps.Select(s => s.ToDto()).ToList(),
        tc.BaseUrl, tc.FailFast,
        tc.CaseCode, tc.Module, tc.SourceSteps, tc.ExpectedResult, tc.Priority,
        tc.IsFlaky, tc.FlakeRate, tc.FlakeCheckedAt,
        tc.VisualEnabled, tc.VisualThreshold, tc.VisualIgnoreRegions, tc.CustomFields, tc.DataSetId,
        tc.ReviewStatus, tc.ReviewedAt, tc.ReviewNote, tc.ReviewedBy?.DisplayName,
        tc.RequirementId, tc.Requirement?.Title,
        tc.Project?.Name,
        tc.NetworkRules);

    public static TestStepDto ToDto(this TestStep s) =>
        new(s.Id, s.StepOrder, s.ActionType, s.Config, s.AIInstruction, s.AIElementDescription,
            s.SharedGroupId, s.SharedGroup?.Name, s.SharedVariables);
}

public record UpdateTestCaseStepsRequest(List<UpdateTestStepRequest> Steps);

public record UpdateTestStepRequest(
    int StepOrder, ActionType ActionType, StepConfig Config,
    string? AIInstruction, string? AIElementDescription,
    // 引用共享步骤组（迭代 C）；传了就按共享步骤处理，忽略 ActionType/Config
    Guid? SharedGroupId = null,
    List<SharedVariableEntry>? SharedVariables = null);

/// <summary>手动设置 unstable 标记</summary>
public record SetFlakeRequest(bool IsFlaky);
