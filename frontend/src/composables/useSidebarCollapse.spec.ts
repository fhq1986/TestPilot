import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

const STORAGE_KEY = 'ui.sidebar.collapsed'

/**
 * 这个 composable 的初始值在**模块加载时**就从 localStorage 读，所以每个用例都要
 * 先把 localStorage 摆好、再 resetModules 重新 import，否则拿到的是上一个用例的单例。
 */
async function loadComposable() {
  vi.resetModules()
  return await import('./useSidebarCollapse')
}

describe('useSidebarCollapse', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('默认展开：没存过偏好时不折叠', async () => {
    const { useSidebarCollapse } = await loadComposable()
    expect(useSidebarCollapse().collapsed.value).toBe(false)
  })

  it('读回上次的折叠状态', async () => {
    localStorage.setItem(STORAGE_KEY, '1')
    const { useSidebarCollapse } = await loadComposable()
    expect(useSidebarCollapse().collapsed.value).toBe(true)
  })

  it('toggle 切换状态并持久化', async () => {
    const { useSidebarCollapse } = await loadComposable()
    const { collapsed, toggle } = useSidebarCollapse()

    toggle()
    await nextTick()
    expect(collapsed.value).toBe(true)
    expect(localStorage.getItem(STORAGE_KEY)).toBe('1')

    toggle()
    await nextTick()
    expect(collapsed.value).toBe(false)
    expect(localStorage.getItem(STORAGE_KEY)).toBe('0')
  })

  it('多个调用点共享同一个状态（MainLayout 与 AppSidebar 要看到同一份）', async () => {
    const { useSidebarCollapse } = await loadComposable()
    const first = useSidebarCollapse()
    const second = useSidebarCollapse()

    first.toggle()
    await nextTick()
    expect(second.collapsed.value).toBe(true)
  })

  it('localStorage 不可用时退回展开，且不抛异常', async () => {
    const getItem = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage disabled')
    })
    try {
      const { useSidebarCollapse } = await loadComposable()
      expect(useSidebarCollapse().collapsed.value).toBe(false)
    } finally {
      getItem.mockRestore()
    }
  })

  it('收起宽度与折叠态菜单宽度一致（菜单自动宽 = 图标 24 + 内边距 20×2）', async () => {
    const { SIDEBAR_WIDTH } = await loadComposable()
    expect(SIDEBAR_WIDTH.collapsed).toBe(64)
    expect(SIDEBAR_WIDTH.expanded).toBeGreaterThan(SIDEBAR_WIDTH.collapsed)
  })
})
