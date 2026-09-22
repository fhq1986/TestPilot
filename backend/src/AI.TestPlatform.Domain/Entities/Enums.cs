namespace AI.TestPlatform.Domain.Entities;

/// <summary>
/// 用例类型。
///
/// ⚠ <see cref="Mobile"/> 是**保留值，当前未实现**：没有移动端执行器，
/// 执行侧会显式拒绝（见 ExecutionPlanner / ExecutionApiExtensions）。
/// 新建表单与 Excel 导入都不产出该类型，库里也没有存量数据。
///
/// 之所以不把这个值删掉：它按 int 落库，删掉会让序列化/反序列化的语义变模糊
/// （历史导出、外部脚本可能带着 2）。要接 Appium 时直接补执行器即可。
/// </summary>
public enum TestType { Web, Api, Mobile }

/// <summary>
/// 用例生命周期状态。
///
/// 刻意**只有两个值**：原来还有第三个 `Deprecated`，但它其实是「删除标记」，
/// 和生命周期不是一回事——两者共用一个字段会导致：
///   ① 全局查询过滤器把 Deprecated 藏了，状态列永远显示不出"已废弃"（前端那个分支是死代码）；
///   ② 有的地方拿它当"用例已删除"的哨兵值用（计划范围 DTO），把删除混进了状态语义。
/// 软删除已拆成 `TestCase.DeletedAt`。
/// </summary>
public enum TestCaseStatus { Draft, Active }

/// <summary>
/// 用例评审状态（方案 A：标记层，不影响执行与自动流转）。
/// None=未纳入评审；Pending=待评审；Approved=已通过；Rejected=已驳回（改后需重新提交）。
/// 内容变更时 Approved 自动回 Pending（评审结论绑定内容版本）。
/// </summary>
public enum CaseReviewStatus { None = 0, Pending = 1, Approved = 2, Rejected = 3 }

public enum ActionType
{
    Click, Fill, Navigate, Wait, Screenshot, Scroll,
    Request, AssertResponse, ExtractVariable,
    AIAction, AIAssert,
    AssertVisible, AssertText,
    AssertUrl, AssertTitle,
    // 迭代 D：无障碍（WCAG）扫描，基于内置 axe-core
    AssertA11y,

    // ---- 以下为追加项。**只允许追加在末尾**：ActionType 按 int 落库
    // （TestSteps.ActionType / ExecutionResults），改动已有数值会让历史数据的动作全部错位。

    /// <summary>选择下拉项（HTML select）。此前只能靠 AIAction 兜底，而不确定、慢、按调用计费的 AI
    /// 不该被用来顶替确定性动作</summary>
    Select,

    /// <summary>上传文件：config.value 为文件路径，多个用换行或分号分隔</summary>
    UploadFile,

    /// <summary>按键：config.value 为按键名（Enter / Control+A）；配定位符时先聚焦该元素，否则发给页面焦点</summary>
    PressKey,

    /// <summary>悬浮：下拉菜单、tooltip 的常规前置动作</summary>
    Hover,

    /// <summary>断言元素属性：config.attribute 指定属性名，config.value 为期望片段</summary>
    AssertAttribute,

    /// <summary>断言匹配元素个数等于 config.value</summary>
    AssertCount,

    /// <summary>断言输入框当前值（value 属性）包含期望片段</summary>
    AssertValue,

    /// <summary>断言元素状态：config.value 取 visible/hidden/enabled/disabled/checked/unchecked/editable/readonly</summary>
    AssertState,
}

/// <summary>
/// 执行状态。值已持久化到数据库，只能在末尾追加新值，不可改动既有序号。
/// Canceled（6）为用户手动终止：进行中的步骤中断、未执行的步骤标记 Skipped。
/// </summary>
public enum ExecutionStatus { Pending, Running, Passed, Failed, Error, Skipped, Canceled }

/// <summary>
/// 套件失败策略（执行编排）。
///
/// Continue 是**升级前的既有行为**（一套跑完，失败不影响后面的用例），所以默认值取 0，
/// 存量套件升级后语义不变；需要「冒烟不通过就别再浪费机器跑回归」时显式切到 StopOnFailure。
/// </summary>
public enum SuiteFailurePolicy
{
    /// <summary>继续：某条失败后其余用例照跑（默认）</summary>
    Continue = 0,

    /// <summary>快停：本轮出现失败/错误后，未开始的用例全部跳过并记录原因</summary>
    StopOnFailure = 1,
}

public enum TriggerType { Manual, Scheduled, CIWebhook, AIRegression, TestPlan }

/// <summary>
/// 定时任务的执行范围类型（迭代 E）。
///
/// 为什么需要这个显式判别：原来范围是**隐式**的（靠「指定用例 > 模块/优先级 > 全项目」推断），
/// 于是「模块与优先级都空且未指定用例」既等于「全项目用例」，
/// 又没法与「按测试计划执行」区分开——加一种范围就必须先把它显式化。
/// </summary>
public enum ScheduleScopeKind { Cases = 0, TestPlan = 1 }

/// <summary>
/// 用户角色（迭代 C）：固定 RBAC，超级管理员 / 管理员 / 测试工程师 / 只读访客。
/// 数值刻意从 0 开始且 Admin=0，与数据库列的默认值一致——迁移回填时老用户自动成为管理员，
/// 与「升级前任何登录用户都能访问一切」的既有行为保持等价，不会因升级而失去权限。
/// </summary>
public enum UserRole
{
    Admin = 0,
    Tester = 1,
    Viewer = 2,

    /// <summary>
    /// 内置超级管理员：拥有全部权限（含用户管理 / 系统设置）。
    /// 不参与常规用户管理——不出现在他人的用户列表里、不可被删除、不可被降权，
    /// 由种子数据创建且仅此一个。值追加在末尾：Role 按 int 落库，改动既有序号会让存量用户角色错位。
    /// </summary>
    SuperAdmin = 3,
}

/// <summary>
/// 权限点（迭代 C）：位图，一个角色对应若干个权限位的并集。
/// 权限写入 JWT 的 <c>perm</c> 声明（整数），服务端逐端点用 <see cref="Permission"/> 做按位与判断，
/// 因此新增权限点只需在矩阵里补一行，不必逐个改策略定义。
/// 上限 32 位（int），当前使用 18 位，余量充足。
/// </summary>
[Flags]
public enum Permission
{
    None            = 0,

    // ---- 读取类（三种角色都有）
    ViewProjects    = 1 << 0,
    ViewTestCases   = 1 << 1,
    ViewExecutions  = 1 << 2,
    ViewReports     = 1 << 3,
    ViewTestPlans   = 1 << 14,
    ViewLoadTests   = 1 << 16,

    // ---- 业务写入类（管理员 + 测试工程师）
    ManageProjects  = 1 << 4,
    ManageTestCases = 1 << 5,
    RunExecutions   = 1 << 6,
    ManageDataSets  = 1 << 7,
    ManageBaselines = 1 << 8,
    ManageSharedSteps = 1 << 9,
    // 编排测试计划是测试的日常工作，与 ManageTestCases 同级
    ManageTestPlans = 1 << 15,
    // 压测能打垮共享环境，值得独立授权（而不是蹭 ManageTestCases）
    ManageLoadTests = 1 << 17,

    // ---- 系统管理类（仅管理员）
    ManageSchedules = 1 << 10,
    ManageSettings  = 1 << 11,
    ManageUsers     = 1 << 12,
    ViewAuditLog    = 1 << 13,
}

/// <summary>
/// M8 Agent：失败归因给出的**修复类别**，决定"自动应用 / 需人工审批 / 不可修复"。
///
/// ⚠ 按 int 落库（AgentAttempts.FixCategory）：**只能追加末尾**，禁止改动既有序号，
/// 否则历史归因记录会整体错位。数值与前端 `types/agent.ts` 必须同步（有单测钉住）。
/// </summary>
public enum FixCategory
{
    /// <summary>选择器更新——自动可修复（先走既有元素自愈链，耗尽才升级到本类）</summary>
    LocatorUpdate = 0,
    /// <summary>等待策略（加等待 / 改 WaitUntil）——自动可修复</summary>
    WaitStrategy = 1,
    /// <summary>步骤配置微调——自动可修复</summary>
    StepConfigPatch = 2,

    /// <summary>插入新步骤——改步骤结构，需人工审批</summary>
    StepInsertion = 3,
    /// <summary>删除步骤——破坏性，任何情况都需确认</summary>
    StepDeletion = 4,
    /// <summary>重排步骤顺序——可能掩盖 bug，需确认</summary>
    StepReorder = 5,
    /// <summary>放宽断言——降低测试质量，需确认</summary>
    AssertRelaxation = 6,

    /// <summary>目标应用本身有 bug——不修复</summary>
    AppBug = 7,
    /// <summary>环境问题（服务未起 / 网络）——不修复</summary>
    EnvironmentIssue = 8,
    /// <summary>测试数据问题——不修复</summary>
    DataIssue = 9,
    /// <summary>无法判断——不修复</summary>
    Unknown = 10,
}

/// <summary>
/// M8 Agent：一次修复尝试的最终结果。同样按 int 落库，只允许末尾追加。
/// </summary>
public enum AgentAttemptResult
{
    /// <summary>修复成功，后续步骤全部通过</summary>
    Fixed = 0,
    /// <summary>修好了这个步骤，但后续出现新的失败</summary>
    Partial = 1,
    /// <summary>修复动作应用后重跑仍失败</summary>
    Failed = 2,
    /// <summary>预算耗尽</summary>
    BudgetExhausted = 3,
    /// <summary>人工拒绝</summary>
    Rejected = 4,
    /// <summary>跳过（FixCategory 不可修复，或动作未通过安全校验）</summary>
    Skipped = 5,
}

/// <summary>
/// 压测场景的用例来源（迭代 F·P2-9）。按 int 落库，只允许末尾追加。
/// </summary>
public enum LoadTestSource
{
    /// <summary>从平台已有的接口用例（TestType.Api）多选生成脚本</summary>
    Cases = 0,
    /// <summary>从导入的 OpenAPI/Swagger 文档选操作生成脚本</summary>
    OpenApi = 1,
}
