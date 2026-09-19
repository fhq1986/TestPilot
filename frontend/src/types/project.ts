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
}

/** 项目新增/编辑的负载 */
export interface ProjectPayload {
  name: string;
  description?: string;
  managerId?: string | null;
  testOwnerId?: string | null;
  developerOwnerId?: string | null;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
