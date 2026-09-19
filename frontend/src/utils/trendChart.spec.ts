import { describe, expect, it } from 'vitest'
import {
  buildCountSegments,
  buildRateDots,
  buildRatePoints,
  buildRateSegments,
  ratePointX,
  ratePointY,
  toPolyline,
} from './trendChart'
import type { TrendPoint } from '@/types/stats'

const day = (date: string, passed: number, failed: number): TrendPoint => ({
  date, passed, failed, total: passed + failed, defectsCreated: 0, defectsClosed: 0,
})
const empty = (date: string): TrendPoint => ({ date, passed: 0, failed: 0, total: 0, defectsCreated: 0, defectsClosed: 0 })

describe('trendChart 折线坐标', () => {
  it('横坐标取每根柱子的中心', () => {
    // 14 天的第 1 根与最后一根
    expect(ratePointX(0, 14)).toBeCloseTo((0.5 / 14) * 100, 6)
    expect(ratePointX(13, 14)).toBeCloseTo((13.5 / 14) * 100, 6)
  })

  it('通过率映射到纵向百分比，100% 在顶部', () => {
    expect(ratePointY(10, 10)).toBe(0)     // 全通过 → y=0（顶部）
    expect(ratePointY(0, 10)).toBe(100)    // 全失败 → y=100（底部）
    expect(ratePointY(1, 4)).toBe(75)
  })

  it('无执行的日期返回 null，不参与折线', () => {
    const points = buildRatePoints([day('09-01', 1, 1), empty('09-02'), day('09-03', 3, 1)])

    expect(points[0]).not.toBeNull()
    expect(points[1]).toBeNull()
    expect(points[2]).not.toBeNull()
  })

  it('空档把折线切成多段——这是最关键的一条，否则会把没跑的日子连成假趋势', () => {
    const segments = buildRateSegments([
      day('09-01', 1, 1), day('09-02', 2, 0),   // 连续两天
      empty('09-03'), empty('09-04'),           // 空档
      day('09-05', 0, 4),                        // 单独一天
    ])

    expect(segments).toHaveLength(2)
    expect(segments[0]).toHaveLength(2)
    expect(segments[1]).toHaveLength(1)
  })

  it('只有一个有效点时不产生跨全图的长线', () => {
    const segments = buildRateSegments([empty('09-01'), day('09-02', 1, 1), empty('09-03')])

    expect(segments).toHaveLength(1)
    expect(segments[0]).toHaveLength(1)
  })

  it('全为空时不产生任何线段与圆点', () => {
    const trend = [empty('09-01'), empty('09-02')]

    expect(buildRateSegments(trend)).toEqual([])
    expect(buildRateDots(trend)).toEqual([])
  })

  it('圆点数等于有执行的天数', () => {
    const trend = [day('09-01', 1, 1), empty('09-02'), day('09-03', 1, 0), day('09-04', 0, 2)]

    expect(buildRateDots(trend)).toHaveLength(3)
  })

  it('首尾都有数据时只有一段', () => {
    const segments = buildRateSegments([day('09-01', 1, 1), day('09-02', 1, 1)])

    expect(segments).toHaveLength(1)
    expect(toPolyline(segments[0])).toMatch(/^\d+\.\d{3},\d+\.\d{3} \d+\.\d{3},\d+\.\d{3}$/)
  })
})

describe('buildCountSegments 绝对值曲线', () => {
  it('按窗口最大值归一化：峰值到顶（y=0），0 贴底（y=100）', () => {
    const segments = buildCountSegments([2, 0, 4])

    expect(segments).toHaveLength(1)
    expect(segments[0][0].y).toBe(50)  // 2/4
    expect(segments[0][1].y).toBe(100) // 0
    expect(segments[0][2].y).toBe(0)   // 4 = 峰值
  })

  it('全程为 0 时不画线（空段）', () => {
    expect(buildCountSegments([0, 0, 0])).toHaveLength(0)
  })
})
