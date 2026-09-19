import { readonly, ref } from 'vue'

/**
 * 移动端导航抽屉的开合状态。
 *
 * 为什么不并进 `useSidebarCollapse`：两者语义完全不同 ——
 *   - 侧栏「收起」是**使用偏好**：持久化到 localStorage，刷新后还保持收起；
 *   - 抽屉「打开」是**临时态**：点开看一眼菜单，选完就该关，刷新后不该自动弹出来。
 * 混在一起会让「抽屉开着」也被写进 localStorage，下次进页面直接盖住内容。
 *
 * 与 `useBreakpoint` 一样放在模块级：开合状态只有一份，
 * AppHeader（开）与 MainLayout（渲染抽屉、关）必须看到同一个值。
 */
const open = ref(false)

export function useMobileNav() {
  return {
    drawerOpen: readonly(open),
    /** 供 el-drawer 的 v-model 用（点遮罩、按 Esc 关闭时由组件回写） */
    setDrawerOpen: (value: boolean) => {
      open.value = value
    },
    openNav: () => {
      open.value = true
    },
    closeNav: () => {
      open.value = false
    },
  }
}
