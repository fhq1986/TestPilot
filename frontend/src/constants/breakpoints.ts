/**
 * 移动端适配的断点口径（单一来源）。
 *
 * ⚠️ CSS 变量**不能**用在媒体查询里（`@media (max-width: var(--bp-md))` 不生效），
 * 所以同一组数值必须在两处各写一遍字面量：
 *   - TS：本文件 —— 供 `useBreakpoint` 与「结构上必须换组件」的 JS 分支使用
 *   - CSS：`styles/responsive.css` 顶部注释块 —— 供纯 CSS 布局使用
 * 改任何一处都必须同步另一处。这是刻意接受的重复：
 * 消灭它需要引入 postcss-custom-media 构建插件，不值得为 3 个数加一个构建依赖。
 */

/** 平板上限起点：`>= 768px` 进入平板档 */
export const BP_TABLET_MIN = 768

/** 桌面起点：`>= 1024px` 完全沿用改造前的行为（本层所有规则都不作用于它） */
export const BP_DESKTOP_MIN = 1024

/**
 * L1 支持的最低宽度。
 * 360px 是仍在服役的老 Android 常见宽度，比 375px（iPhone SE/8 级）更窄，
 * 卡片内边距与字号按它取值才不会在小屏上挤爆。
 */
export const MIN_SUPPORTED_WIDTH = 360

/** 手机竖屏 / 横屏：`< 768px` */
export const MEDIA_MOBILE = `(max-width: ${BP_TABLET_MIN - 1}px)`

/** 平板：`768–1023px`（iPad 竖屏、小笔记本） */
export const MEDIA_TABLET = `(min-width: ${BP_TABLET_MIN}px) and (max-width: ${BP_DESKTOP_MIN - 1}px)`

/**
 * 窄屏：平板 + 手机。
 *
 * 布局上「侧栏转抽屉」「表格转卡片」的分界都是 1024（不是 768）——
 * 1023px 宽放一个 220px 侧栏 + 8 列宽表同样挤不下，所以这两种结构切换共用这一档。
 * 需要桌面语义时直接用 `!isNarrow`，不必再查一次 matchMedia（两者互补）。
 */
export const MEDIA_NARROW = `(max-width: ${BP_DESKTOP_MIN - 1}px)`
