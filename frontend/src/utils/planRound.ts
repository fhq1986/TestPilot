import { PlanRoundStatus, TestPlanStatus } from '@/types/testPlan'
import type { PlanRoundCaseResult, PlanRoundSummary } from '@/types/testPlan'

/**
 * 测试计划轮次/状态的展示工具（纯函数）。
 *
 * 概览、轮次、明细抽屉三处共用——之前散在 TestPlanDetailView 一个文件里，
 * 拆分面板组件后收到这里，避免三份漂移。
 */

/** 计划状态 → tag 配色 */
export const planStatusTagType = (status: number) =>
  status === TestPlanStatus.Active ? 'primary'
    : status === TestPlanStatus.Completed ? 'success'
      : status === TestPlanStatus.Archived ? 'info' : 'warning'

/** 进行中轮次的完成百分比 */
export const roundPercent = (round: PlanRoundSummary) => {
  if (!round.stats.total) return 0
  const done = round.stats.passed + round.stats.failed + round.stats.error
  return Math.round((done / round.stats.total) * 100)
}

/** 轮次耗时；进行中的轮次算到当前时刻 */
export const roundDuration = (round: PlanRoundSummary) => {
  if (!round.startedAt) return '—'
  const start = new Date(round.startedAt).getTime()
  const end = round.completedAt ? new Date(round.completedAt).getTime() : Date.now()
  const seconds = Math.max(0, Math.round((end - start) / 1000))
  return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m${seconds % 60}s`
}

export const roundStatusLabel = (round: PlanRoundSummary) =>
  round.status === PlanRoundStatus.Running ? '进行中'
    : round.status === PlanRoundStatus.Completed ? '已完成' : '已中止'

export const roundStatusTag = (round: PlanRoundSummary) =>
  round.status === PlanRoundStatus.Running ? 'primary'
    : round.status === PlanRoundStatus.Completed ? 'success' : 'info'

export const triggerLabel = (round: PlanRoundSummary) =>
  ({ 0: '手动', 1: '定时', 2: 'CI', 3: 'AI 回归', 4: '测试计划' }[round.triggerType] ?? '未知')

export const execStatusLabel = (status: number) =>
  ({ 0: '等待', 1: '运行中', 2: '通过', 3: '失败', 4: '错误', 5: '跳过' }[status] ?? '未知')

export const execTagType = (status: number) =>
  status === 2 ? 'success' : status === 3 ? 'danger' : status === 4 ? 'warning' : 'info'

/** 一条用例多条执行时的最差状态（决定列表里显示什么） */
export const worstStatus = (row: PlanRoundCaseResult) =>
  row.executions.some((e) => e.status === 4) ? 4
    : row.executions.some((e) => e.status === 3) ? 3
      : row.executions.some((e) => e.status === 1 || e.status === 0) ? 1
        : row.executions.some((e) => e.status === 5) ? 5 : 2
