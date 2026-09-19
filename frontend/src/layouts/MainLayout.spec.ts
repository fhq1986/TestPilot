import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import ElementPlus, { ElAside, ElDrawer } from 'element-plus'
import MainLayout from './MainLayout.vue'
import { useMobileNav } from '@/composables/useMobileNav'
import { MEDIA_MOBILE, MEDIA_NARROW, MEDIA_TABLET } from '@/constants/breakpoints'

type ChangeListener = (event: MediaQueryListEvent) => void

/** 900px：平板档，同属「窄屏」（侧栏要让位给抽屉） */
const NARROW = { [MEDIA_MOBILE]: false, [MEDIA_TABLET]: true, [MEDIA_NARROW]: true }
/** 1440px 桌面 */
const DESKTOP = { [MEDIA_MOBILE]: false, [MEDIA_TABLET]: false, [MEDIA_NARROW]: false }

/**
 * 可控的 matchMedia 桩。
 *
 * 两个关键点，都是被坑出来的：
 *
 * 1. `matches` / `listeners` 放在模块级、跨用例复用，而 `matchMedia` 本身每个用例重装一次 ——
 *    因为 `test-setup.ts` 的 beforeEach 会装一个惰性桩，装完会被它盖掉，所以必须重装。
 * 2. 但**不能**每个用例换一份新的 map：断点 composable 是模块级单例，
 *    它在首次调用时就把监听绑到了当时那批 MediaQueryList 上，之后换新桩也换不掉。
 *    复用同一份 map，后续用例靠 change 事件把状态翻过去 —— 这也正是真实浏览器的行为。
 */
const matches = new Map<string, boolean>()
const listeners = new Map<string, Set<ChangeListener>>()

function installMediaStub() {
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
}

/** 把断点状态推到目标档，并通知已注册的监听 */
function applyBreakpoint(state: Record<string, boolean>) {
  for (const [query, value] of Object.entries(state)) {
    matches.set(query, value)
  }
  for (const [query, value] of Object.entries(state)) {
    for (const cb of listeners.get(query) ?? []) {
      cb({ matches: value } as MediaQueryListEvent)
    }
  }
}

beforeEach(() => {
  installMediaStub()
  // 抽屉开合是模块级单例，用例之间显式归位
  useMobileNav().setDrawerOpen(false)
})

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/other', component: { template: '<div />' } },
    ],
  })
}

/** 同样用 shallow：要验的是「哪套导航结构被渲染出来」，不是 Element Plus 自己的 DOM */
async function mountLayout(state = NARROW) {
  applyBreakpoint(state)

  const router = makeRouter()
  await router.push('/')
  await router.isReady()

  const wrapper = mount(MainLayout, {
    shallow: true,
    global: { plugins: [createPinia(), router, ElementPlus] },
  })

  return { wrapper, router }
}

describe('MainLayout 的导航结构', () => {
  it('窄屏：渲染抽屉，不渲染固定侧栏', async () => {
    const { wrapper } = await mountLayout(NARROW)

    expect(wrapper.findComponent(ElDrawer).exists()).toBe(true)
    expect(wrapper.findComponent(ElAside).exists()).toBe(false)
  })

  it('桌面：渲染固定侧栏，不渲染抽屉（改造前就是这个行为）', async () => {
    const { wrapper } = await mountLayout(DESKTOP)

    expect(wrapper.findComponent(ElAside).exists()).toBe(true)
    expect(wrapper.findComponent(ElDrawer).exists()).toBe(false)
  })

  it('打开菜单后抽屉打开', async () => {
    const { wrapper } = await mountLayout(NARROW)

    expect(wrapper.findComponent(ElDrawer).props('modelValue')).toBe(false)

    useMobileNav().openNav()
    await wrapper.vm.$nextTick()

    expect(wrapper.findComponent(ElDrawer).props('modelValue')).toBe(true)
  })

  it('选中菜单（路由变化）后自动关闭抽屉', async () => {
    const { wrapper, router } = await mountLayout(NARROW)
    useMobileNav().openNav()
    await wrapper.vm.$nextTick()
    expect(wrapper.findComponent(ElDrawer).props('modelValue')).toBe(true)

    await router.push('/other')
    await wrapper.vm.$nextTick()

    expect(wrapper.findComponent(ElDrawer).props('modelValue')).toBe(false)
  })

  it('从窄屏切回桌面：抽屉让位给固定侧栏，且开合状态被重置', async () => {
    const { wrapper } = await mountLayout(NARROW)
    useMobileNav().openNav()
    await wrapper.vm.$nextTick()
    expect(wrapper.findComponent(ElDrawer).props('modelValue')).toBe(true)

    applyBreakpoint(DESKTOP)
    await wrapper.vm.$nextTick()

    expect(wrapper.findComponent(ElDrawer).exists()).toBe(false)
    expect(wrapper.findComponent(ElAside).exists()).toBe(true)
    // 不重置的话，下次再变窄抽屉会自己弹出来，像是误触
    expect(useMobileNav().drawerOpen.value).toBe(false)
  })
})
