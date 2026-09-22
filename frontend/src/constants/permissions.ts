/**
 * 权限点常量与工具。
 *
 * 取值与后端 `AI.TestPlatform.Domain.Entities.Permission`（[Flags] 位图）**必须逐位一致**；
 * 后端有单测锁住权限矩阵，前端这里用 `permissionCatalog.spec.ts` 锁住数值映射，
 * 两侧同时改错的概率极低。
 *
 * 为什么不从接口动态拉权限点定义：权限点随代码演进，每次新增都要发版；
 * 硬编码成常量能让菜单/按钮的可见性在编译期就有类型支持，且不必为渲染菜单多发一次请求。
 */
export const Permission = {
  ViewProjects: 1 << 0,
  ViewTestCases: 1 << 1,
  ViewExecutions: 1 << 2,
  ViewReports: 1 << 3,
  ManageProjects: 1 << 4,
  ManageTestCases: 1 << 5,
  RunExecutions: 1 << 6,
  ManageDataSets: 1 << 7,
  ManageBaselines: 1 << 8,
  ManageSharedSteps: 1 << 9,
  ManageSchedules: 1 << 10,
  ManageSettings: 1 << 11,
  ManageUsers: 1 << 12,
  ViewAuditLog: 1 << 13,
  ViewTestPlans: 1 << 14,
  ManageTestPlans: 1 << 15,
  ViewLoadTests: 1 << 16,
  ManageLoadTests: 1 << 17,
} as const

export type PermissionValue = (typeof Permission)[keyof typeof Permission]

/** 权限点英文名 → 中文名（与后端 PermissionCatalog.Describe 保持一致） */
export const PermissionLabels: Record<string, string> = {
  ViewProjects: '查看项目',
  ViewTestCases: '查看用例',
  ViewExecutions: '查看执行',
  ViewReports: '查看报告',
  ManageProjects: '管理项目',
  ManageTestCases: '管理用例',
  RunExecutions: '执行测试',
  ManageDataSets: '管理数据集',
  ManageBaselines: '管理基线',
  ManageSharedSteps: '管理共享步骤',
  ManageSchedules: '管理定时任务',
  ManageSettings: '管理系统设置',
  ManageUsers: '管理用户',
  ViewAuditLog: '查看审计日志',
  ViewTestPlans: '查看测试计划',
  ManageTestPlans: '管理测试计划',
  ViewLoadTests: '查看压测场景',
  ManageLoadTests: '管理压测场景',
}

/** 判断权限位图是否满足所需权限（全部满足才算通过，与后端 `PermissionCatalog.Has` 同语义） */
export function hasPermission(granted: number | undefined | null, required: number): boolean {
  if (!required) return true
  if (granted == null) return false
  return (granted & required) === required
}

/** 把位图展开成权限点名称列表（用于调试面板 / 用户详情） */
export function expandPermissions(granted: number): string[] {
  return Object.entries(Permission)
    .filter(([, value]) => (granted & value) === value)
    .map(([name]) => name)
}
