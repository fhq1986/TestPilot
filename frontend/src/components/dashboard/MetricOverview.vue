<template>
  <!-- 指标卡：固定两行各 5 张——第一行"资产盘点"，第二行"质量与健康"，
       每行都能按从左到右的阅读顺序讲完一件事 -->
  <div class="overview-grid">
    <el-card v-for="metric in metrics" :key="metric.key" class="metric-card"
      :class="[`tone-${metric.tone}`, { 'metric-active': metric.highlight }]" @click="router.push(metric.to)">
      <div class="metric-icon">
        <el-icon :size="20">
          <component :is="metric.icon" />
        </el-icon>
      </div>
      <div class="metric-body">
        <div class="metric-value">{{ metric.value }}</div>
        <div class="metric-label">{{ metric.label }}</div>
      </div>
      <div class="metric-hint">{{ metric.hint }}</div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import type { DashboardResponse } from '@/types/stats'

const props = defineProps<{ data: DashboardResponse | null }>()
const router = useRouter()

const passRateDisplay = computed(() =>
  props.data ? `${props.data.overview.passRate7d}%` : '—',
)

const runningCount = computed(() => props.data?.overview.runningCount ?? 0)
const flakyCount = computed(() => props.data?.overview.flakyCount ?? 0)
const activePlanCount = computed(() => props.data?.overview.activePlans ?? 0)
const scheduleErrorCount = computed(() => props.data?.scheduleHealth.withError ?? 0)

/** 毫秒转可读时长。0 显示成 "—" 而不是 "0 ms"——没数据时 0 会被误读成"很快" */
const formatDurationMs = (ms: number) => {
  if (!ms) return '—'
  if (ms < 1000) return `${ms} ms`
  if (ms < 60000) return `${(ms / 1000).toFixed(1)} s`
  return `${Math.floor(ms / 60000)} 分 ${Math.round((ms % 60000) / 1000)} 秒`
}

/** 概览指标：数值 + 图标 + 色调 + 跳转目标 */
const metrics = computed(() => [
  {
    key: 'projects',
    label: '项目总数',
    value: props.data?.overview.totalProjects ?? '—',
    icon: 'Folder',
    tone: 'blue',
    hint: '',
    to: { path: '/projects' },
  },
  {
    key: 'cases',
    label: '测试用例',
    value: props.data?.overview.totalCases ?? '—',
    icon: 'Document',
    tone: 'cyan',
    hint: '',
    to: { path: '/testcases' },
  },
  {
    key: 'executions',
    label: '累计执行',
    value: props.data?.overview.totalExecutions ?? '—',
    icon: 'VideoPlay',
    tone: 'violet',
    hint: '',
    to: { path: '/executions' },
  },
  {
    key: 'running',
    label: '执行中',
    value: props.data?.overview.runningCount ?? '—',
    icon: 'Loading',
    tone: 'amber',
    hint: '',
    highlight: runningCount.value > 0,
    // 执行记录列表默认按「执行中」筛选
    to: { path: '/executions', query: { status: '1' } },
  },
  {
    key: 'passRate',
    label: '近 7 天通过率',
    value: passRateDisplay.value,
    icon: 'CircleCheck',
    tone: 'green',
    hint: '',
    to: { path: '/executions', query: { days: '7' } },
  },
  {
    key: 'defects',
    label: '未闭环缺陷',
    value: props.data?.overview.openDefects ?? '—',
    icon: 'WarningFilled',
    tone: 'red',
    // 致命/严重未闭环 > 0 时高亮：达标门槛盯的就是这个数
    hint:
      props.data && props.data.overview.openDefects > 0
        ? `致命/严重 ${props.data.overview.openCriticalDefects}｜近 7 天新增 ${props.data.overview.newDefects7d} / 闭环 ${props.data.overview.closedDefects7d}`
        : '暂无未闭环缺陷',
    highlight: (props.data?.overview.openCriticalDefects ?? 0) > 0,
    to: { path: '/defects' },
  },
  {
    key: 'plans',
    label: '测试计划',
    value: props.data?.overview.totalPlans ?? '—',
    icon: 'Notebook',
    tone: 'violet',
    hint: activePlanCount.value > 0 ? `进行中 ${activePlanCount.value} 个` : '暂无进行中计划',
    highlight: activePlanCount.value > 0,
    to: { path: '/test-plans' },
  },
  {
    key: 'duration',
    label: '执行时长 P95',
    value: props.data ? formatDurationMs(props.data.overview.durationP95Ms) : '—',
    icon: 'Timer',
    tone: 'amber',
    // 平均值放在 hint 里对照：两者差距越大，说明长尾越严重
    hint:
      props.data && props.data.overview.durationAvgMs > 0
        ? `近 7 天平均 ${formatDurationMs(props.data.overview.durationAvgMs)}`
        : '近 7 天无执行',
    to: { path: '/executions', query: { days: '7' } },
  },
  {
    key: 'schedule',
    label: '定时任务异常',
    value: props.data?.scheduleHealth.withError ?? '—',
    icon: 'AlarmClock',
    tone: 'red',
    hint:
      (props.data?.scheduleHealth.enabled ?? 0) > 0
        ? `启用中 ${props.data?.scheduleHealth.enabled} 个`
        : '没有启用的任务',
    highlight: scheduleErrorCount.value > 0,
    to: { path: '/schedules' },
  },
  {
    key: 'flaky',
    label: '不稳定用例',
    value: props.data?.overview.flakyCount ?? '—',
    icon: 'WarnTriangleFilled',
    tone: 'red',
    hint: '查看 flaky 隔离视图',
    highlight: flakyCount.value > 0,
    to: { path: '/testcases', query: { flakyOnly: 'true' } },
  },
])
</script>

<style scoped>
/* ---------------------------------------------------------------- 指标卡 */
/* 固定 5 列（两行各 5 张）；窄屏放不下 5 列时退回 auto-fit 自动换列 */
.overview-grid {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 16px;
  flex-shrink: 0;
}

/* 1280px 是**桌面端的密度断点**（5 列放不下时退回自动换列），不属于移动端那套
   767 / 1023 分档 —— 两者目的不同，各自保留即可，不必强行合并。 */
@media (max-width: 1280px) {
  .overview-grid {
    grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  }
}

@media (max-width: 1023px) {
  .overview-grid {
    /* 指标卡不再按行铺满；两列读起来还是"从左到右"的顺序 */
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 12px;
  }
}

.metric-card {
  position: relative;
  cursor: pointer;
  overflow: hidden;
  transition: transform 0.18s ease, box-shadow 0.18s ease, border-color 0.18s ease;
}

/* 左侧色条：区分指标类别 */
.metric-card::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  width: 3px;
  background: var(--metric-color);
  opacity: 0.85;
}

.metric-card :deep(.el-card__body) {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 16px 18px;
}

.metric-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 8px 20px rgba(31, 59, 115, 0.12);
  border-color: var(--metric-color);
}

.metric-icon {
  flex-shrink: 0;
  width: 42px;
  height: 42px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--metric-color);
  background: var(--metric-bg);
}

.metric-body {
  min-width: 0;
}

.metric-value {
  font-size: 26px;
  font-weight: 600;
  line-height: 1.15;
  color: #1f2d3d;
  font-variant-numeric: tabular-nums;
}

.metric-label {
  margin-top: 2px;
  font-size: 13px;
  color: #7a8699;
}

.metric-hint {
  position: absolute;
  right: 14px;
  bottom: 2px;
  font-size: 12px;
  color: var(--metric-color);
  opacity: 0;
  transition: opacity 0.18s ease;
}

.metric-card:hover .metric-hint {
  opacity: 1;
}

.metric-active .metric-value {
  color: var(--metric-color);
}

/* 各指标色调 */
.tone-blue {
  --metric-color: #1f3b73;
  --metric-bg: #eaf1fb;
}

.tone-cyan {
  --metric-color: #0e7490;
  --metric-bg: #e6f6fa;
}

.tone-violet {
  --metric-color: #6d28d9;
  --metric-bg: #f1ecfe;
}

.tone-green {
  --metric-color: #1f7a33;
  --metric-bg: #e9f6ec;
}

.tone-amber {
  --metric-color: #b26a00;
  --metric-bg: #fdf3e3;
}

.tone-red {
  --metric-color: #b3261e;
  --metric-bg: #fbecea;
}
</style>
