import { afterEach, describe, expect, it, vi } from 'vitest'
import { MEDIA_MOBILE, MEDIA_NARROW, MEDIA_TABLET } from '@/constants/breakpoints'

type ChangeListener = (event: MediaQueryListEvent) => void

/**
 * 可控的 matchMedia 桩。
 *
 * `useBreakpoint` 在**首次调用**时读取 `mql.matches` 并注册 change 监听，
 * 所以每个用例要先把各档的匹配结果摆好，再 `vi.resetModules()` 重新 import
 * —— 否则拿到的是上一个用例留下的模块级单例。
 */
function installMatchMedia(initial: Record<string, boolean>) {
  const matches = new Map(Object.entries(initial))
  const listeners = new Map<string, Set<ChangeListener>>()

  vi.stubGlobal('matchMedia', (query: string) => {
    if (!listeners.has(query)) listeners.set(query, new Set())
    return {
      media: query,
      get matches() {
        return matches.get(query) ?? false
      },
      onchange: null,
      addEventListener: (_type: string, cb: ChangeListener) => listeners.get(query)!.add(cb),
      removeEventListener: (_type: string, cb: ChangeListener) => listeners.get(query)!.delete(cb),
      dispatchEvent: () => false,
    }
  })

  return {
    /** 翻转某一档并通知监听器，模拟窗口尺寸变化 */
    change(query: string, value: boolean) {
      matches.set(query, value)
      for (const cb of listeners.get(query) ?? []) {
        cb({ matches: value } as MediaQueryListEvent)
      }
    },
    listenerCount: (query: string) => (listeners.get(query) ?? new Set()).size,
  }
}

async function loadComposable() {
  vi.resetModules()
  return await import('./useBreakpoint')
}

/** 桌面（>= 1024px）：三档都不命中 */
const DESKTOP = { [MEDIA_MOBILE]: false, [MEDIA_TABLET]: false, [MEDIA_NARROW]: false }
/** 手机 375px：mobile 与 narrow 命中 */
const MOBILE = { [MEDIA_MOBILE]: true, [MEDIA_TABLET]: false, [MEDIA_NARROW]: true }
/** 平板 900px：tablet 与 narrow 命中，mobile 不命中 */
const TABLET = { [MEDIA_MOBILE]: false, [MEDIA_TABLET]: true, [MEDIA_NARROW]: true }

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('useBreakpoint', () => {
  it('桌面宽度：只有 isDesktop 为真', async () => {
    installMatchMedia(DESKTOP)
    const { useBreakpoint } = await loadComposable()
    const bp = useBreakpoint()

    expect(bp.isMobile.value).toBe(false)
    expect(bp.isTablet.value).toBe(false)
    expect(bp.isNarrow.value).toBe(false)
    expect(bp.isDesktop.value).toBe(true)
  })

  it('手机宽度：isMobile 为真且不再是桌面', async () => {
    installMatchMedia(MOBILE)
    const { useBreakpoint } = await loadComposable()
    const bp = useBreakpoint()

    expect(bp.isMobile.value).toBe(true)
    expect(bp.isTablet.value).toBe(false)
    expect(bp.isDesktop.value).toBe(false)
  })

  it('平板宽度：isTablet 与 isNarrow 为真，isMobile 为假', async () => {
    installMatchMedia(TABLET)
    const { useBreakpoint } = await loadComposable()
    const bp = useBreakpoint()

    expect(bp.isMobile.value).toBe(false)
    expect(bp.isTablet.value).toBe(true)
    // 1023px 放侧栏 + 宽表同样挤不下，所以平板也算「窄屏」
    expect(bp.isNarrow.value).toBe(true)
    expect(bp.isDesktop.value).toBe(false)
  })

  it('isDesktop 与 isNarrow 互补（不额外查一次 matchMedia）', async () => {
    installMatchMedia(TABLET)
    const { useBreakpoint } = await loadComposable()
    const bp = useBreakpoint()

    expect(bp.isDesktop.value).toBe(!bp.isNarrow.value)
  })

  it('窗口跨过断点后跟着翻转', async () => {
    const media = installMatchMedia(DESKTOP)
    const { useBreakpoint } = await loadComposable()
    const bp = useBreakpoint()

    expect(bp.isNarrow.value).toBe(false)

    media.change(MEDIA_MOBILE, true)
    media.change(MEDIA_NARROW, true)

    expect(bp.isMobile.value).toBe(true)
    expect(bp.isNarrow.value).toBe(true)
    expect(bp.isDesktop.value).toBe(false)

    media.change(MEDIA_MOBILE, false)
    media.change(MEDIA_NARROW, false)

    expect(bp.isMobile.value).toBe(false)
    expect(bp.isDesktop.value).toBe(true)
  })

  it('多个调用点共享同一份状态（MainLayout 与 AppSidebar 要看到同一个值）', async () => {
    const media = installMatchMedia(DESKTOP)
    const { useBreakpoint } = await loadComposable()
    const first = useBreakpoint()
    const second = useBreakpoint()

    media.change(MEDIA_NARROW, true)
    expect(second.isNarrow.value).toBe(true)
    expect(first.isDesktop.value).toBe(false)
  })

  it('重复调用只注册一次监听（模块级单例，不重复绑定）', async () => {
    const media = installMatchMedia(DESKTOP)
    const { useBreakpoint } = await loadComposable()

    useBreakpoint()
    useBreakpoint()
    useBreakpoint()

    expect(media.listenerCount(MEDIA_MOBILE)).toBe(1)
    expect(media.listenerCount(MEDIA_NARROW)).toBe(1)
  })

  it('matchMedia 不可用时退回桌面，且不抛异常', async () => {
    vi.stubGlobal('matchMedia', undefined)
    const { useBreakpoint } = await loadComposable()
    const bp = useBreakpoint()

    expect(bp.isDesktop.value).toBe(true)
    expect(bp.isMobile.value).toBe(false)
  })

  it('matchMedia 抛异常时同样退回桌面（老浏览器里 query 语法可能不被接受）', async () => {
    vi.stubGlobal('matchMedia', () => {
      throw new Error('unsupported query')
    })
    const { useBreakpoint } = await loadComposable()
    const bp = useBreakpoint()

    expect(bp.isDesktop.value).toBe(true)
  })
})
