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

// ==================== 项目成员（迭代 E·① 项目级授权） ====================

/** 项目级角色：数值与后端 ProjectRole 一致（越大权限越高）。 */
export type ProjectRole = 0 | 1 | 2 | 3

export const PROJECT_ROLE_OPTIONS: { value: ProjectRole; label: string; desc: string }[] = [
  { value: 3, label: '所有者', desc: '项目内最高权限，可管理成员与项目设置' },
  { value: 2, label: '管理员', desc: '可改项目设置、环境、定时任务' },
  { value: 1, label: '测试工程师', desc: '可管理用例、跑执行、管数据集/基线/计划' },
  { value: 0, label: '只读', desc: '仅可查看项目及其子资源' },
]

export function projectRoleLabel(role: number): string {
  return PROJECT_ROLE_OPTIONS.find((o) => o.value === role)?.label ?? '未知'
}

export function projectRoleTagType(role: number): 'danger' | 'warning' | 'primary' | 'info' {
  switch (role) {
    case 3:
      return 'danger'
    case 2:
      return 'warning'
    case 1:
      return 'primary'
    default:
      return 'info'
  }
}

export interface ProjectMember {
  id: string
  userId: string
  username: string
  displayName?: string | null
  role: ProjectRole
  createdAt: string
}

/** 加成员时的用户候选（只含 id/用户名/显示名）。 */
export interface UserCandidate {
  id: string
  username: string
  displayName?: string | null
}
