import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from './auth'
import { Permission } from '@/constants/permissions'
import type { UserInfo } from '@/types/auth'

const adminUser: UserInfo = {
  id: '1',
  username: 'admin',
  displayName: '管理员',
  role: 0,
  roleName: '管理员',
  permissions: 65535,
  permissionNames: ['ManageUsers', 'ViewAuditLog'],
}

const viewerUser: UserInfo = {
  id: '2',
  username: 'viewer',
  displayName: '访客',
  role: 2,
  roleName: '只读访客',
  permissions: 15,
  permissionNames: ['ViewProjects', 'ViewTestCases', 'ViewExecutions', 'ViewReports'],
}

vi.mock('@/api/auth', () => ({
  loginApi: vi.fn(async () => ({ token: 'test-token', user: adminUser })),
  meApi: vi.fn(async () => adminUser),
}))

// 需要在 mock 之后导入，取到被 mock 的 meApi
import { meApi } from '@/api/auth'

describe('auth store', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.mocked(meApi).mockReset()
    vi.mocked(meApi).mockResolvedValue(adminUser)
  })

  it('login stores token and user', async () => {
    const store = useAuthStore()
    await store.login('admin', 'password')

    expect(store.token).toBe('test-token')
    expect(store.isAuthenticated).toBe(true)
    expect(localStorage.getItem('auth_token')).toBe('test-token')
  })

  it('logout clears token and user', async () => {
    const store = useAuthStore()
    store.token = 'abc'
    store.user = adminUser

    store.logout()

    expect(store.token).toBe('')
    expect(store.isAuthenticated).toBe(false)
    expect(localStorage.getItem('auth_token')).toBeNull()
  })

  it('can() 判断权限位图（管理员全通过，访客只读通过、管理不通过）', () => {
    const store = useAuthStore()
    store.user = adminUser
    expect(store.can(Permission.ManageUsers)).toBe(true)
    expect(store.can(Permission.ViewProjects)).toBe(true)

    store.user = viewerUser
    expect(store.can(Permission.ViewReports)).toBe(true)
    expect(store.can(Permission.ManageUsers)).toBe(false)
    expect(store.can(Permission.ManageProjects)).toBe(false)
    // 组合权限需全部满足（与后端同语义）
    expect(store.can(Permission.ViewProjects | Permission.ManageProjects)).toBe(false)
  })

  it('isAdmin / roleName 派生自 user.role', () => {
    const store = useAuthStore()
    store.user = adminUser
    expect(store.isAdmin).toBe(true)
    expect(store.roleName).toBe('管理员')

    store.user = viewerUser
    expect(store.isAdmin).toBe(false)
    expect(store.roleName).toBe('只读访客')
  })

  it('未登录时 can() 一律 false', () => {
    const store = useAuthStore()
    store.user = null
    expect(store.can(Permission.ViewProjects)).toBe(false)
  })

  it('refreshMe 成功时用服务端权限覆盖本地', async () => {
    const store = useAuthStore()
    store.token = 'test-token'
    store.user = viewerUser
    vi.mocked(meApi).mockResolvedValue(adminUser)

    await store.refreshMe()

    expect(store.user?.username).toBe('admin')
    expect(store.can(Permission.ManageUsers)).toBe(true)
  })

  it('refreshMe 失败（会话失效 401）时清理本地状态', async () => {
    const store = useAuthStore()
    store.token = 'stale-token'
    store.user = adminUser
    vi.mocked(meApi).mockRejectedValue({ response: { status: 401 } })

    await store.refreshMe()

    expect(store.token).toBe('')
    expect(store.user).toBeNull()
    expect(localStorage.getItem('auth_token')).toBeNull()
  })

  it('refreshMe 遇到网络/服务端错误时不登出，保留本地状态', async () => {
    // 后端临时不可用、代理故障都不代表凭据失效。把用户踢下线只会让他在登录页
    // 反复失败，反而更难定位问题——保留状态、下次请求再试才是对的。
    const store = useAuthStore()
    store.token = 'valid-token'
    store.user = adminUser

    for (const rejection of [
      { response: { status: 500 } },
      { response: { status: 502 } },
      new Error('Network Error'),
    ]) {
      vi.mocked(meApi).mockRejectedValue(rejection)
      await store.refreshMe()
      expect(store.token).toBe('valid-token')
      expect(store.user).not.toBeNull()
    }
  })

  it('refreshMe 覆盖陈旧快照后，新增权限点的菜单立即可见', async () => {
    // 根因回归：localStorage 里的 permissions 是**登录当时**的快照。
    // 平台新增权限点（如 ViewTestPlans = 1<<14）后，老快照没有这一位，
    // 新菜单对已登录用户直接消失，而且路由守卫只在「本地无 user」时拉取，永远不会自愈。
    const store = useAuthStore()
    store.token = 'token-from-before-new-permission'
    // 模拟旧快照：管理员在只有 14 个权限点时的位图 16383
    store.user = { ...adminUser, permissions: 16383, permissionNames: [] }
    expect(store.can(Permission.ViewTestPlans)).toBe(false)

    vi.mocked(meApi).mockResolvedValue({
      ...adminUser,
      permissions: 65535,
      permissionNames: ['ViewTestPlans'],
    })
    await store.refreshMe()

    expect(store.can(Permission.ViewTestPlans)).toBe(true)
  })

  it('refreshMe 在无 token 时不发请求', async () => {
    const store = useAuthStore()
    await store.refreshMe()
    expect(meApi).not.toHaveBeenCalled()
  })

  it('localStorage 脏数据不会导致崩溃', () => {
    localStorage.setItem('auth_user', '{ not json')
    const store = useAuthStore()
    expect(store.user).toBeNull()
  })
})
