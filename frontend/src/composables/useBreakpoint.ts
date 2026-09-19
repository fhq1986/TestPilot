import { computed, readonly, ref, type Ref } from 'vue'
import { MEDIA_MOBILE, MEDIA_NARROW, MEDIA_TABLET } from '@/constants/breakpoints'

/**
 * 断点响应式状态。
 *
 * 为什么有了 CSS 媒体查询还需要它：**布局**一律交给纯 CSS 媒体查询
 * （不依赖 JS、无首屏闪烁、没有 hydration 时序问题）；但有两处是「结构上必须换组件」，
 * CSS 换不动，只能靠 JS 分支：
 *   - `<el-table>` ↔ 卡片列表（两套 DOM，不是同一棵树的样式差异）
 *   - 固定侧栏 ↔ 抽屉
 * 除此之外不要用这里的值写样式，否则等于把布局逻辑搬到 JS。
 *
 * 状态放在模块级：断点是**全局**的，多个组件各注册一份 matchMedia 监听没有意义，
 * 而且 `AppSidebar` 与 `MainLayout` 必须看到同一份值。
 */

const isMobile = ref(false)
const isTablet = ref(false)
const isNarrow = ref(false)

interface MediaBinding {
  query: string
  target: Ref<boolean>
}

const bindings: MediaBinding[] = [
  { query: MEDIA_MOBILE, target: isMobile },
  { query: MEDIA_TABLET, target: isTablet },
  { query: MEDIA_NARROW, target: isNarrow },
]

let initialized = false

/**
 * 取一次 matchMedia。环境缺失时返回 null ——
 * 老浏览器、SSR、以及没打桩的测试环境都会走到这里，
 * 此时按「桌面」处理（三个 ref 保持 false，isDesktop 为 true）：
 * 宁可多给功能，也不要因为取不到断点把页面渲染成空白。
 */
function safeMatchMedia(query: string): MediaQueryList | null {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return null
  try {
    return window.matchMedia(query)
  } catch {
    return null
  }
}

/**
 * 首次用到时才注册监听，而**不是**在模块加载时。
 *
 * 这不只是省一点初始化开销：测试里模块是在文件顶部被 import 的，
 * 那一刻 `window.matchMedia` 还没被 `test-setup.ts` 打上桩，
 * 若在加载期注册就会永久停在「桌面」这个默认值上，移动端分支再也测不到。
 */
function ensureInitialized() {
  if (initialized) return
  initialized = true

  for (const { query, target } of bindings) {
    const mql = safeMatchMedia(query)
    if (!mql) continue

    target.value = mql.matches

    const onChange = (event: MediaQueryListEvent) => {
      target.value = event.matches
    }

    // 老 Safari（< 14）只有 addListener/removeListener，没有 addEventListener。
    // 而移动端恰恰是老 Safari 的重灾区，所以这里保留兼容分支。
    if (typeof mql.addEventListener === 'function') {
      mql.addEventListener('change', onChange)
    } else if (typeof mql.addListener === 'function') {
      mql.addListener(onChange)
    }
  }
}

export function useBreakpoint() {
  ensureInitialized()

  return {
    /** 手机：`< 768px` */
    isMobile: readonly(isMobile),
    /** 平板：`768–1023px` */
    isTablet: readonly(isTablet),
    /** 窄屏（平板 + 手机）：`< 1024px` —— 侧栏转抽屉、表格转卡片的分界线 */
    isNarrow: readonly(isNarrow),
    /** 桌面：`>= 1024px`。与 isNarrow 互补，所以不再单独查一次 matchMedia */
    isDesktop: computed(() => !isNarrow.value),
  }
}
