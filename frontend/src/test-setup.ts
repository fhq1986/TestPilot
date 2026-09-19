// Node >= 25 enables experimental webstorage by default, whose global
// localStorage shadows jsdom's and is non-functional. When detected,
// replace it with a Map-backed in-memory implementation.
import { beforeEach } from 'vitest'
import { config } from '@vue/test-utils'

/**
 * shallow 挂载时，VTU 生成的桩**默认不渲染插槽内容**。
 *
 * 于是像 MainLayout 这样「根节点就是 `<el-container>`」的组件，整棵子树都渲染不出来，
 * `findComponent` 自然什么都找不到 —— 报错只是干巴巴的「元素不存在」，
 * 很难联想到是桩的默认行为在作祟。组件测试里要断言的就是内部结构，所以全局打开它。
 */
config.global.renderStubDefaultSlot = true

/**
 * jsdom 不实现 matchMedia（调用会直接抛 "not implemented"），而断点 composable 依赖它。
 *
 * 这里只放一个**惰性桩**：永远不匹配、监听器收不到通知 ——
 * 够让用到断点的组件在测试里安全加载（此时按桌面处理，与改造前的行为等价）。
 * 需要精确控制断点的测试自己用 vi.stubGlobal 覆盖，见 useBreakpoint.spec.ts。
 */
beforeEach(() => {
  if (typeof window === 'undefined') {
    return
  }
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: (query: string) =>
      ({
        media: query,
        matches: false,
        onchange: null,
        addEventListener: () => {},
        removeEventListener: () => {},
        addListener: () => {},
        removeListener: () => {},
        dispatchEvent: () => false,
      }) as unknown as MediaQueryList,
  })
})

beforeEach(() => {
  if (typeof localStorage.clear === 'function') {
    return
  }
  const store = new Map<string, string>()
  Object.defineProperty(globalThis, 'localStorage', {
    value: {
      get length() {
        return store.size
      },
      clear: () => {
        store.clear()
      },
      getItem: (key: string) => store.get(key) ?? null,
      key: (index: number) => [...store.keys()][index] ?? null,
      removeItem: (key: string) => {
        store.delete(key)
      },
      setItem: (key: string, value: string) => {
        store.set(key, String(value))
      },
    },
    configurable: true,
    writable: true,
  })
})
