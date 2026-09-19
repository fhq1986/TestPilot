import { ref, watch } from 'vue'

const STORAGE_KEY = 'ui.sidebar.collapsed'

/**
 * 侧边栏「展开 / 收起」状态。
 *
 * 放在模块级 ref 里当单例用，而不是走 props/emits：
 * 需要读这个状态的有两处——MainLayout（决定 el-aside 的宽度）和 AppSidebar（决定菜单是否折叠），
 * 由谁持有、谁传递都不自然；而且菜单项多的页面（比如用例列表）想加个"收起以腾出宽度"的按钮时，
 * 也不想再往上捅一层事件。状态跟着 composable 走，谁需要谁读。
 *
 * 持久化到 localStorage：收起是一种**使用偏好**，不是一次性动作——
 * 刷新页面又弹回来会让人每次都重新收一遍。
 */
const collapsed = ref(readInitial())

function readInitial(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === '1'
  } catch {
    // 隐私模式下 localStorage 可能不可用，退回默认展开
    return false
  }
}

watch(collapsed, (value) => {
  try {
    localStorage.setItem(STORAGE_KEY, value ? '1' : '0')
  } catch {
    // 存不进去也不影响本次会话的折叠效果
  }
})

export function useSidebarCollapse() {
  return {
    collapsed,
    toggle: () => {
      collapsed.value = !collapsed.value
    },
  }
}

/** 侧边栏展开 / 收起时的宽度。用同一个常量，避免布局与菜单各写一个数 */
export const SIDEBAR_WIDTH = { expanded: 220, collapsed: 64 } as const
