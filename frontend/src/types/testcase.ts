export enum TestType {
  Web = 0,
  Api = 1,
  Mobile = 2,
}

/**
 * 用例生命周期状态。**只有两个值**：
 * Draft = 还没验证过能跑通；Active = 至少成功执行通过一次（后端自动提升）。
 * 原来的第三个值 Deprecated 是「删除标记」，已拆成独立的软删除字段——
 * 两者混用一个字段会让状态列永远显示不出"已删除"（那个分支是死代码）。
 */
export enum TestCaseStatus {
  Draft = 0,
  Active = 1,
}

/**
 * 用例列表「按最近执行结果筛选」的取值。
 *
 * **数值必须与后端 `CaseExecFilter` 一致**（前端传数字，后端按枚举绑定）。
 * 它不是 `ExecutionStatus` 的投影，别想当然地对齐：
 * - 「执行中」是一个**桶**，含 Pending（排队）+ Running（在跑）——对"哪些还在跑"没区别；
 * - 「未执行」在 `ExecutionStatus` 里**不存在**（没有任何执行记录时状态为 null）。
 */
export enum CaseExecFilter {
  Never = 0,
  Running = 1,
  Passed = 2,
  Failed = 3,
  Error = 4,
  Skipped = 5,
}

/**
 * 筛选下拉的选项。
 *
 * 文案刻意不复用 `EXECUTION_STATUS_LABELS`（那里 Pending 叫「等待中」）：
 * 筛选框里出现「等待中」会让人以为是**两个**筛选项，而这里它和「执行中」是一件事。
 */
export const CASE_EXEC_FILTER_OPTIONS: ReadonlyArray<{ value: CaseExecFilter; label: string }> = [
  { value: CaseExecFilter.Never, label: '未执行' },
  { value: CaseExecFilter.Running, label: '执行中' },
  { value: CaseExecFilter.Passed, label: '通过' },
  { value: CaseExecFilter.Failed, label: '失败' },
  { value: CaseExecFilter.Error, label: '错误' },
  { value: CaseExecFilter.Skipped, label: '跳过' },
]

export enum ActionType {
  Click = 0,
  Fill = 1,
  Navigate = 2,
  Wait = 3,
  Screenshot = 4,
  Scroll = 5,
  Request = 6,
  AssertResponse = 7,
  ExtractVariable = 8,
  AIAction = 9,
  AIAssert = 10,
  AssertVisible = 11,
  AssertText = 12,
  AssertUrl = 13,
  AssertTitle = 14,
  // 迭代 D：遍历 WCAG 规则集（内置 axe-core），config.value 为最低拦截级别
  AssertA11y = 15,

  // ---- 以下为追加项。**数值必须与后端 ActionType 一致**（按 int 落库）：
  // 历史步骤的动作类型是靠这些数值还原的，对不上会让老用例的动作显示成别的动作。

  /** 选择下拉项（HTML select），config.value 为要选中的 value/label */
  Select = 16,
  /** 上传文件，config.value 为文件路径（多个用换行或分号分隔） */
  UploadFile = 17,
  /** 按键，config.value 为按键名（Enter / Control+A）；配定位符时先聚焦该元素 */
  PressKey = 18,
  /** 悬浮（下拉菜单、tooltip 的常规前置） */
  Hover = 19,
  /** 断言元素属性：config.attribute 属性名 + config.value 期望片段 */
  AssertAttribute = 20,
  /** 断言匹配元素个数等于 config.value */
  AssertCount = 21,
  /** 断言输入框当前值包含 config.value */
  AssertValue = 22,
  /** 断言元素状态：config.value 取 visible/hidden/enabled/disabled/checked/unchecked/editable/readonly */
  AssertState = 23,
}

/** 动作类型 → 中文名（脚本解析预览 / 录制器 / 共享步骤三处曾各自复制且文案漂移，收敛于此） */
export const ACTION_TYPE_LABELS: Record<number, string> = {
  [ActionType.Click]: '点击',
  [ActionType.Fill]: '输入',
  [ActionType.Navigate]: '打开页面',
  [ActionType.Wait]: '等待',
  [ActionType.Screenshot]: '截图',
  [ActionType.Scroll]: '滚动',
  [ActionType.Request]: '接口请求',
  [ActionType.AssertResponse]: '响应断言',
  [ActionType.ExtractVariable]: '提取变量',
  [ActionType.AIAction]: 'AI 动作',
  [ActionType.AIAssert]: 'AI 断言',
  [ActionType.AssertVisible]: '断言可见',
  [ActionType.AssertText]: '断言文本',
  [ActionType.AssertUrl]: '断言地址',
  [ActionType.AssertTitle]: '断言标题',
  [ActionType.AssertA11y]: '无障碍扫描',
  [ActionType.Select]: '选择下拉',
  [ActionType.UploadFile]: '上传文件',
  [ActionType.PressKey]: '按键',
  [ActionType.Hover]: '悬浮',
  [ActionType.AssertAttribute]: '断言属性',
  [ActionType.AssertCount]: '断言数量',
  [ActionType.AssertValue]: '断言输入值',
  [ActionType.AssertState]: '断言状态',
}

/** 可选浏览器（与后端 BrowserCatalog 对齐）；编辑表单如需更详细的内核说明可自行扩展 label */
export const BROWSER_OPTIONS = [
  { id: 'chromium', label: 'Chromium' },
  { id: 'firefox', label: 'Firefox' },
  { id: 'webkit', label: 'WebKit' },
] as const

export interface SelectorConfig {
  type: string
  description?: string | null
  value?: string | null
}

export interface HeaderEntry {
  name: string
  value: string
}

export interface StepConfig {
  url?: string | null
  selector?: SelectorConfig | null
  method?: string | null
  endpoint?: string | null
  headers?: HeaderEntry[] | null
  body?: string | null
  value?: string | null
  /** AssertAttribute 要读取的属性名（如 disabled、aria-label、href） */
  attribute?: string | null
}

// ------------------------------------------------------------ 用例级网络规则
// 用 // 行注释而不是 /** */ 块注释：下面要举 Playwright glob 的例子（含星号斜杠），
// 写在块注释里会**提前结束注释**，是个很难一眼看出的坑。

export type NetworkRuleAction = 'fulfill' | 'abort' | 'delay'

export interface NetworkRule {
  // URL 匹配（Playwright glob）。例：两个星号 + /api/pay + 两个星号 = 匹配任意域下的该路径
  pattern: string
  action: NetworkRuleAction
  // fulfill 的返回状态码，默认 200
  status?: number | null
  // fulfill 的 Content-Type，默认 application/json
  contentType?: string | null
  // fulfill 的响应体
  body?: string | null
  // delay 的延迟毫秒数
  delayMs?: number | null
}

export const NETWORK_RULE_ACTION_LABELS: Record<NetworkRuleAction, string> = {
  fulfill: '返回构造响应',
  abort: '让请求失败',
  delay: '延迟放行',
}

/**
 * 解析后端存下来的规则 JSON。
 *
 * 解析失败**返回空数组而不是抛异常**：规则是历史数据，一条坏 JSON 不该让整个编辑页打不开。
 * 真正的校验在服务端做，保存时会给出"哪条规则、哪个字段"的确切错误。
 */
export function parseNetworkRules(raw?: string | null): NetworkRule[] {
  if (!raw) return []
  try {
    const parsed = JSON.parse(raw)
    return Array.isArray(parsed) ? (parsed as NetworkRule[]) : []
  } catch {
    return []
  }
}

/** 序列化成后端要的形态；没有有效规则时返回 null（后端据此清空规则） */
export function serializeNetworkRules(rules: NetworkRule[]): string | null {
  const cleaned = rules.filter((rule) => rule.pattern?.trim())
  return cleaned.length === 0 ? null : JSON.stringify(cleaned)
}

export interface SharedVariableEntry {
  name: string
  value: string
}

export interface TestStep {
  id: string
  stepOrder: number
  actionType: ActionType
  config: StepConfig
  aiInstruction?: string | null
  aiElementDescription?: string | null
  /**
   * 迭代 C：引用共享步骤组。
   * 非空时本步骤是「占位」，运行时由后端展开成组内的真实步骤——
   * 此时 config / actionType 不参与执行，只用 sharedVariables 覆盖组内变量。
   */
  sharedGroupId?: string | null
  sharedGroupName?: string | null
  sharedVariables?: SharedVariableEntry[] | null
}

/** 步骤快照（版本历史用；config 是序列化后的 JSON 字符串） */
export interface TestStepSnapshot {
  stepOrder: number
  actionType: number
  config: string
  aiInstruction?: string | null
  aiElementDescription?: string | null
  sharedGroupId?: string | null
  sharedVariables?: { name: string; value: string }[] | null
}

/**
 * 用例内容快照。
 * 只含"内容"——不含状态/更新时间/flaky 标记那些由执行和治理过程自动写的字段，
 * 否则"什么都没改但状态被后台改了"也会算成一次版本变化。
 */
export interface TestCaseSnapshot {
  name: string
  description?: string | null
  type: number
  caseCode?: string | null
  module?: string | null
  priority?: string | null
  browser?: string | null
  timeout: number
  retryCount: number
  failFast: boolean
  baseUrl?: string | null
  expectedResult?: string | null
  sourceSteps?: string | null
  visualEnabled: boolean
  visualThreshold: number
  /** 视觉忽略区域（JSON 数组原文）：[{x,y,w,h}] 百分比 0~100。列表不返回，只有详情带 */
  visualIgnoreRegions?: string | null
  /** 评审状态（方案 A 标记层）：0 未纳入 / 1 待评审 / 2 已通过 / 3 已驳回 */
  reviewStatus: number
  reviewedAt?: string | null
  reviewNote?: string | null
  /** 评审人显示名（列表/详情） */
  reviewedByName?: string | null
  dataSetId?: string | null
  requirementId?: string | null
  /** 用例级网络规则（JSON 数组原文）。列表不返回，只有详情带 */
  networkRules?: string | null
  steps: TestStepSnapshot[]
}

/** 版本历史列表项 */
export interface TestCaseVersionSummary {
  version: number
  createdAt: string
  operatorName?: string | null
  /** 该版本到下一版之间改了什么（人话） */
  changeSummary?: string | null
  stepCount: number
}

/** 单个版本的完整内容 */
export interface TestCaseVersionDetail extends TestCaseVersionSummary {
  snapshot: TestCaseSnapshot
}

export interface TestCaseSummary {
  id: string
  projectId: string
  /** 所属项目名（后端 JOIN 带出，列表跨项目展示时用） */
  projectName?: string | null
  /** 最近一次执行的状态；**null 表示从未执行过** */
  latestExecutionStatus?: number | null
  /** 最近一次执行的时间 */
  lastExecutedAt?: string | null
  /** 创建人显示名（M8 审计字段；历史行可能为空） */
  createdByName?: string | null
  name: string
  type: TestType
  description?: string | null
  aiGenerated: boolean
  browser?: string | null
  timeout: number
  retryCount: number
  version: number
  status: TestCaseStatus
  createdAt: string
  updatedAt: string
  baseUrl?: string | null
  failFast?: boolean
  caseCode?: string | null
  module?: string | null
  /** 优先级 P0-P3 */
  priority?: string | null
  /** 不稳定用例：最近若干次执行结果既通过又失败（flaky） */
  isFlaky?: boolean
  /** 不稳定度 0-1，值越高越不稳定 */
  flakeRate?: number
  /** 视觉回归开关与差异阈值（0-1） */
  visualEnabled?: boolean
  visualThreshold?: number
  /** 绑定的数据集（参数化） */
  dataSetId?: string | null
  /** 关联需求（覆盖统计） */
  requirementId?: string | null
  /** 评审状态（方案 A 标记层）：0 未纳入 / 1 待评审 / 2 已通过 / 3 已驳回 */
  reviewStatus: number
  reviewedAt?: string | null
  reviewNote?: string | null
  /** 评审人显示名（列表/详情） */
  reviewedByName?: string | null
  requirementTitle?: string | null
}

export interface TestCase extends TestCaseSummary {
  sourceSteps?: string | null
  expectedResult?: string | null
  /** 视觉忽略区域（JSON 数组原文）：[{x,y,w,h}] 百分比 0~100 */
  visualIgnoreRegions?: string | null
  reviewedAt?: string | null
  reviewNote?: string | null
  /** 扩展字段值（JSON 对象原文：键 = 定义 Id） */
  customFields?: string | null
  /** 最近一次 flake 统计时间 */
  flakeCheckedAt?: string | null
  steps: TestStep[]
  /** 用例级网络规则（JSON 数组原文）。列表接口不返回，**只有详情带** */
  networkRules?: string | null
}

export interface CreateTestStepPayload {
  stepOrder: number
  actionType: ActionType
  config: StepConfig
  aiInstruction?: string | null
  aiElementDescription?: string | null
  /** 迭代 C：共享步骤引用（与后端 CreateTestStepRequest / UpdateTestStepRequest 同构） */
  sharedGroupId?: string | null
  sharedVariables?: SharedVariableEntry[] | null
}

export interface CreateTestCasePayload {
  projectId: string
  name: string
  type: TestType
  description?: string | null
  browser?: string | null
  timeout?: number
  retryCount?: number
  steps?: CreateTestStepPayload[]
  baseUrl?: string | null
  caseCode?: string | null
  module?: string | null
  sourceSteps?: string | null
  expectedResult?: string | null
  priority?: string | null
  /** 视觉回归与参数化 */
  visualEnabled?: boolean
  visualThreshold?: number
  visualIgnoreRegions?: string | null
  /** 扩展字段值（JSON 对象原文：键 = 定义 Id） */
  customFields?: string | null
  dataSetId?: string | null
  /** 关联需求（覆盖统计） */
  requirementId?: string | null
  /** 用例级网络规则（JSON 数组原文）；null 表示不拦截 */
  networkRules?: string | null
}

export interface UpdateTestCasePayload {
  name: string
  description?: string | null
  status: TestCaseStatus
  browser?: string | null
  timeout: number
  retryCount: number
  baseUrl?: string | null
  failFast?: boolean
  caseCode?: string | null
  module?: string | null
  sourceSteps?: string | null
  expectedResult?: string | null
  priority?: string | null
  /** 视觉回归与参数化 */
  visualEnabled?: boolean
  visualThreshold?: number
  visualIgnoreRegions?: string | null
  /** 扩展字段值（JSON 对象原文：键 = 定义 Id） */
  customFields?: string | null
  dataSetId?: string | null
  /** 关联需求（覆盖统计） */
  requirementId?: string | null
  /** 用例级网络规则（JSON 数组原文）；null 表示不拦截 */
  networkRules?: string | null
}

// ---------------------------------------------------------------- Excel 导入
export interface ImportModuleStat {
  module: string
  total: number
  imported: number
  skipped: number
  failed: number
}

export interface ImportRowError {
  module: string
  rowNumber: number
  caseCode?: string | null
  message: string
}

export interface TestCaseImportResult {
  totalRows: number
  imported: number
  updated: number
  skipped: number
  failed: number
  aiParsed: number
  aiCaseCount: number
  modules: ImportModuleStat[]
  errors: ImportRowError[]
  warnings: string[]
  /** 关联测试计划：本次新加入计划范围的用例数（选择了测试计划时才有值） */
  planLinked?: number
  /** 关联测试计划：已在计划范围内被跳过的用例数 */
  planSkipped?: number
}

export interface TestCaseModuleStat {
  module: string
  count: number
}
