export interface Project {
  id: string;
  name: string;
  description?: string | null;
  createdById: string;
  createdAt: string;
  updatedAt: string;
  testCaseCount: number;
  /** 项目负责人 */
  managerId?: string | null;
  managerName?: string | null;
  /** 测试负责人：测试计划轮次完成后的验收邮件默认收件人 */
  testOwnerId?: string | null;
  testOwnerName?: string | null;
  /** 便于界面提示「定了负责人却没填邮箱，验收邮件发不出去」 */
  testOwnerEmail?: string | null;
  /** 开发负责人：对缺陷修复负责（测试负责人对用例质量负责）。目前仅登记与展示 */
  developerOwnerId?: string | null;
  developerOwnerName?: string | null;
  /** M8 Agent 自愈：项目级开关（须与系统级总开关同时开启才生效） */
  agentLoopEnabled?: boolean;
  /** M8 Agent 自愈：自愈"通过"是否计入达标判定 */
  treatAgentHealedAsPass?: boolean;
  /** 创建人显示名（M8 审计字段；历史行可能为空） */
  createdByName?: string | null;
}

/** 项目新增/编辑的负载 */
export interface ProjectPayload {
  name: string;
  description?: string;
  managerId?: string | null;
  testOwnerId?: string | null;
  developerOwnerId?: string | null;
  /** M8 Agent 自愈（项目级） */
  agentLoopEnabled?: boolean | null;
  treatAgentHealedAsPass?: boolean | null;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
