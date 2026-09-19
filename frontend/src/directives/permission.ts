import type { App, Directive, DirectiveBinding } from 'vue'
import { useAuthStore } from '@/stores/auth'

/**
 * `v-permission` 指令：无权限时把元素从 DOM 中移除。
 *
 * 用法：
 *   <el-button v-permission="Permission.ManageProjects">新建项目</el-button>
 *   <div v-permission="[Permission.ManageUsers, Permission.ViewAuditLog]">…</div>  // 任满足其一
 *
 * 为什么用「移除」而不是 `disabled`：禁用按钮会让用户反复尝试并困惑；
 * 直接不给入口更符合「角色决定能力」的心智模型。需要保留可见但禁用时，
 * 请改用 `:disabled="!auth.can(...)"`。
 *
 * 这里同样只是**界面层**过滤，真正的权限断言在后端。
 */
function check(value: number | number[] | undefined, binding: DirectiveBinding): boolean {
  if (value === undefined) return true
  const auth = useAuthStore()
  const required = Array.isArray(value) ? value : [value]
  // 多个权限点默认「或」语义（满足任一即显示），与后端「与」语义不同——
  // 因为界面上多给一个权限点通常是「任一权限都能看到这个入口」。
  return required.some((item) => auth.can(item))
}

export const permissionDirective: Directive<HTMLElement, number | number[]> = {
  mounted(el, binding) {
    if (!check(binding.value, binding)) {
      el.parentNode?.removeChild(el)
    }
  },
}

export function registerPermissionDirective(app: App) {
  app.directive('permission', permissionDirective)
}
