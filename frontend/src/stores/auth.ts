import { defineStore } from 'pinia'
import { loginApi, meApi } from '@/api/auth'
import { hasPermission } from '@/constants/permissions'
import { UserRole } from '@/types/auth'
import type { UserInfo } from '@/types/auth'

const TOKEN_KEY = 'auth_token'
const USER_KEY = 'auth_user'

function readUser(): UserInfo | null {
  try {
    const raw = localStorage.getItem(USER_KEY)
    return raw ? (JSON.parse(raw) as UserInfo) : null
  } catch {
    // localStorage 里是脏数据（例如历史版本格式不同）→ 当作未登录，避免整个应用白屏
    return null
  }
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    token: localStorage.getItem(TOKEN_KEY) ?? '',
    user: readUser(),
  }),
  getters: {
    isAuthenticated: (state) => !!state.token,
    role: (state) => state.user?.role ?? null,
    roleName: (state) => state.user?.roleName ?? '',
    permissions: (state) => state.user?.permissions ?? 0,
    // 超级管理员与管理员同级（用于头部角色标签配色等展示场景）
    isAdmin: (state) => state.user?.role === UserRole.Admin || state.user?.role === UserRole.SuperAdmin,
    /**
     * 权限判断入口。用法：`auth.can(Permission.ManageUsers)`
     *
     * 注意这只是**界面层过滤**——真正的拦截在后端 PermissionFilter。
     * 前端隐藏菜单的目的是「不给出点不通的入口」，而不是安全边界。
     */
    can: (state) => (required: number) => hasPermission(state.user?.permissions, required),
  },
  actions: {
    async login(username: string, password: string) {
      const res = await loginApi({ username, password })
      this.setSession(res.token, res.user)
    },

    /**
     * 向服务端核对会话有效性并拉取最新权限。
     *
     * 应用启动时会调用一次（见 main.ts），刷新页面后也会走这里，
     * 目的有两个：踢掉已失效的会话、以及让权限变更（改角色 / 平台新增权限点）及时生效。
     */
    async refreshMe() {
      if (!this.token) return
      try {
        const user = await meApi()
        this.setSession(this.token, user)
      } catch (error) {
        // 只有服务端明确说「会话无效」才登出。
        // 网络抖动、后端重启、代理故障都不代表凭据失效——那种情况下把用户踢下线，
        // 只会让他在登录页反复失败，问题反而更难定位。
        const status = (error as { response?: { status?: number } })?.response?.status
        if (status === 401 || status === 403) {
          this.logout()
        }
        // 其余情况保留本地状态：界面继续可用，下一次请求会再试
      }
    },

    setSession(token: string, user: UserInfo) {
      this.token = token
      this.user = user
      localStorage.setItem(TOKEN_KEY, token)
      localStorage.setItem(USER_KEY, JSON.stringify(user))
    },

    logout() {
      this.token = ''
      this.user = null
      localStorage.removeItem(TOKEN_KEY)
      localStorage.removeItem(USER_KEY)
    },
  },
})
