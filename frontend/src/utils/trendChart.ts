import type { TrendPoint } from '@/types/stats'

/** 折线上的一个点，坐标是 viewBox 0–100 的百分比 */
export interface RatePoint {
  x: number
  y: number
}

/**
 * 第 index 根柱子的中心横坐标（百分比）。
 *
 * 前提是**列宽严格等分且没有 gap**——柱状图那边因此把 `gap` 换成了列内 `padding`。
 * 有 gap 的话每列真实中心会比这个公式偏一点，圆点就落不到柱子正上方。
 */
export const ratePointX = (index: number, count: number) => ((index + 0.5) / count) * 100

/** 通过率映射到纵向百分比（SVG 的 y 轴向下，所以要反过来） */
export const ratePointY = (passed: number, total: number) => 100 - (passed / total) * 100

/**
 * 把每日趋势折成折线坐标，**无执行的日期返回 null 表示断开**。
 *
 * 为什么必须断开：如果直接跨过去连线，一段空档会被拉成一条斜线，
 * 看上去像"通过率这几天平滑下滑"或者"稳步上升"，而实际上那几天根本没跑过。
 * 这是趋势图最容易骗人的地方，宁可断开留白。
 */
export function buildRatePoints(trend: TrendPoint[]): (RatePoint | null)[] {
  return trend.map((point, index) =>
    point.total <= 0
      ? null
      : { x: ratePointX(index, trend.length), y: ratePointY(point.passed, point.total) },
  )
}

/** 按「连续有数据」切段。单独一天也会成为一段（画不出线，由圆点兜住） */
export function buildRateSegments(trend: TrendPoint[]): RatePoint[][] {
  const segments: RatePoint[][] = []
  let current: RatePoint[] = []

  for (const point of buildRatePoints(trend)) {
    if (point === null) {
      if (current.length > 0) {
        segments.push(current)
        current = []
      }
      continue
    }
    current.push(point)
  }
  if (current.length > 0) segments.push(current)

  return segments
}

/** 所有有效点（画圆点用） */
export const buildRateDots = (trend: TrendPoint[]): RatePoint[] =>
  buildRatePoints(trend).filter((p): p is RatePoint => p !== null)

/** 转成 SVG polyline 的 points 字符串 */
export const toPolyline = (segment: RatePoint[]): string =>
  segment.map((p) => `${p.x.toFixed(3)},${p.y.toFixed(3)}`).join(' ')

/**
 * 绝对值序列（如每日缺陷数）折成 0–100 纵坐标的折线段。
 *
 * 与通过率（天生 0–100）不同，绝对值需要按整段窗口的最大值归一化；
 * 全程为 0 时返回空段——画一条贴底的直线只会让人以为"出了 0 个缺陷"是条数据，
 * 实际上直接不画更诚实。
 */
export function buildCountSegments(values: number[]): RatePoint[][] {
  const max = Math.max(0, ...values)
  if (max <= 0) return []

  const points = values.map((value, index) => ({
    x: ratePointX(index, values.length),
    y: 100 - (value / max) * 100,
  }))

  // 连续段切分：这里没有"无数据"语义（0 是真实数据），直接整段一条线
  return [points]
}
