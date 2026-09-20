import { describe, it, expect } from 'vitest'
import { Permission, PermissionLabels, expandPermissions, hasPermission } from './permissions'

/**
 * 这些断言的作用是「锁死前后端契约」。
 *
 * 权限位图在前端硬编码，如果只改一侧（例如后端插入了新的权限点把序号往后挤），
 * 会出现「界面给了入口、请求却被 403」这种很难查的错位。
 * 后端 IterationCPermissionTests 把同一组数值锁死，两边同时改错的概率极低。
 */
describe('Permission 位图', () => {
  it('每一位都必须与后端 Permission 枚举一致', () => {
    expect(Permission.ViewProjects).toBe(1 << 0)
    expect(Permission.ViewTestCases).toBe(1 << 1)
    expect(Permission.ViewExecutions).toBe(1 << 2)
    expect(Permission.ViewReports).toBe(1 << 3)
    expect(Permission.ManageProjects).toBe(1 << 4)
    expect(Permission.ManageTestCases).toBe(1 << 5)
    expect(Permission.RunExecutions).toBe(1 << 6)
    expect(Permission.ManageDataSets).toBe(1 << 7)
    expect(Permission.ManageBaselines).toBe(1 << 8)
    expect(Permission.ManageSharedSteps).toBe(1 << 9)
    expect(Permission.ManageSchedules).toBe(1 << 10)
    expect(Permission.ManageSettings).toBe(1 << 11)
    expect(Permission.ManageUsers).toBe(1 << 12)
    expect(Permission.ViewAuditLog).toBe(1 << 13)
  })

  it('权限点数量不超过 31（位图存 int，第 32 位会变负数）', () => {
    expect(Object.keys(Permission).length).toBeLessThanOrEqual(31)
  })

  it('每个权限点都有中文名（界面上不能出现英文标识）', () => {
    for (const name of Object.keys(Permission)) {
      expect(PermissionLabels[name], `权限点 ${name} 缺少中文名`).toBeTruthy()
    }
  })

  it('内置角色的权限位图与后端 PermissionCatalog 一致', () => {
    // 只读访客 = 5 个查看权限（含查看测试计划——计划与报告是验收材料，访客要能看）
    const viewer =
      Permission.ViewProjects | Permission.ViewTestCases |
      Permission.ViewExecutions | Permission.ViewReports | Permission.ViewTestPlans
    expect(viewer).toBe(16399)

    // 测试工程师 = 访客 + 业务管理权限（不含系统管理）
    const tester = viewer | Permission.ManageProjects | Permission.ManageTestCases |
      Permission.RunExecutions | Permission.ManageDataSets | Permission.ManageBaselines |
      Permission.ManageSharedSteps | Permission.ManageTestPlans
    expect(tester).toBe(50175)
    expect(tester & Permission.ManageUsers).toBe(0)
    expect(tester & Permission.ManageSettings).toBe(0)
    expect(tester & Permission.ViewAuditLog).toBe(0)

    // 超级管理员 = 全部 16 个权限点
    const all = Object.values(Permission).reduce((acc, v) => acc | v, 0)
    expect(all).toBe(65535)

    // 管理员 = 全集去掉「用户管理」与「系统设置」（这两块收归超级管理员）
    const admin = all & ~Permission.ManageUsers & ~Permission.ManageSettings
    expect(admin).toBe(65535 - (1 << 12) - (1 << 11))
  })
})

describe('hasPermission', () => {
  it('所需权限为 0 时恒为 true（仅要求登录）', () => {
    expect(hasPermission(0, 0)).toBe(true)
    expect(hasPermission(undefined, 0)).toBe(true)
  })

  it('授予值为空且需要权限时为 false', () => {
    expect(hasPermission(undefined, Permission.ViewProjects)).toBe(false)
    expect(hasPermission(null, Permission.ViewProjects)).toBe(false)
  })

  it('组合权限需要全部满足（与后端同语义）', () => {
    const granted = Permission.ViewProjects
    expect(hasPermission(granted, Permission.ViewProjects)).toBe(true)
    expect(hasPermission(granted, Permission.ViewProjects | Permission.ManageProjects)).toBe(false)
  })
})

describe('expandPermissions', () => {
  it('展开位图为权限点名称', () => {
    const names = expandPermissions(Permission.ViewProjects | Permission.ManageUsers)
    expect(names).toContain('ViewProjects')
    expect(names).toContain('ManageUsers')
    expect(names).toHaveLength(2)
  })

  it('0 展开为空数组', () => {
    expect(expandPermissions(0)).toEqual([])
  })
})
