import { beforeEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import ElementPlus, { ElMenu } from 'element-plus'
import AppSidebar from './AppSidebar.vue'
import { useSidebarCollapse } from '@/composables/useSidebarCollapse'

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div />' } }],
  })
}

/**
 * 用 shallow 挂载：这里要验的是**结构契约**（该不该渲染某个按钮、菜单折不折叠），
 * 而不是 Element Plus 自己的渲染结果。深挂载会把 el-menu / el-input 整棵树拉进来，
 * 既慢又对 EP 内部实现敏感。
 */
async function mountSidebar(variant: 'aside' | 'drawer') {
  const router = makeRouter()
  await router.push('/')
  await router.isReady()

  return mount(AppSidebar, {
    props: { variant },
    shallow: true,
    global: { plugins: [createPinia(), router, ElementPlus] },
  })
}

describe('AppSidebar 的 aside / drawer 两种形态', () => {
  beforeEach(() => {
    localStorage.clear()
    // 折叠状态是模块级单例，用例之间会互相影响，显式归位
    useSidebarCollapse().collapsed.value = false
  })

  it('aside：有底部「收起菜单」按钮，并跟随折叠状态', async () => {
    const wrapper = await mountSidebar('aside')

    expect(wrapper.find('.app-sidebar-toggle').exists()).toBe(true)
    expect(wrapper.findComponent(ElMenu).props('collapse')).toBe(false)

    useSidebarCollapse().collapsed.value = true
    await wrapper.vm.$nextTick()

    expect(wrapper.findComponent(ElMenu).props('collapse')).toBe(true)
    // 收起态放不下输入框，退化成只有图标的按钮
    expect(wrapper.find('.app-sidebar-search-icon').exists()).toBe(true)
    expect(wrapper.find('.app-sidebar-search').exists()).toBe(false)
  })

  it('drawer：没有「收起菜单」按钮（抽屉本身就是展开全部菜单的语义）', async () => {
    const wrapper = await mountSidebar('drawer')

    expect(wrapper.find('.app-sidebar-toggle').exists()).toBe(false)
  })

  it('drawer：即使桌面侧栏正收起着，抽屉里也必须是展开的', async () => {
    useSidebarCollapse().collapsed.value = true

    const wrapper = await mountSidebar('drawer')

    expect(wrapper.findComponent(ElMenu).props('collapse')).toBe(false)
    // 要的是完整搜索框，不是那个退化形态
    expect(wrapper.find('.app-sidebar-search').exists()).toBe(true)
    expect(wrapper.find('.app-sidebar-search-icon').exists()).toBe(false)
  })
})
