import dayjs from 'dayjs'

export function formatDateTime(value?: string | null): string {
  if (!value) return '—'
  return dayjs(value).format('YYYY-MM-DD HH:mm')
}

/** 精确到秒：给相对时间的 tooltip 用，悬停时补上"到底是几点" */
export function formatFullDateTime(value?: string | null): string {
  if (!value) return '—'
  return dayjs(value).format('YYYY-MM-DD HH:mm:ss')
}

/**
 * 相对时间。消息列表里「3 分钟前」比「2026-09-20 19:42」更能说明"新不新"——
 * 后者要用户自己拿当前时间做减法。
 *
 * 超过 7 天退回绝对日期：「8 天前」已经不比「09-12」更有信息量，反而多一步心算。
 */
export function formatRelativeTime(value?: string | null): string {
  if (!value) return '—'
  const target = dayjs(value)
  const diffSec = dayjs().diff(target, 'second')
  // 客户端时钟比服务端慢时会算出负数，显示"未来"很荒谬，退回绝对时间
  if (diffSec < 0) return target.format('YYYY-MM-DD HH:mm')
  if (diffSec < 60) return '刚刚'
  if (diffSec < 3600) return `${Math.floor(diffSec / 60)} 分钟前`
  if (diffSec < 86400) return `${Math.floor(diffSec / 3600)} 小时前`
  if (diffSec < 86400 * 7) return `${Math.floor(diffSec / 86400)} 天前`
  return target.format('YYYY-MM-DD')
}

export function formatDuration(ms?: number | null): string {
  if (ms === null || ms === undefined) return '—'
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(1)}s`
}
