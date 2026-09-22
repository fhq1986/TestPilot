<template>
  <div class="loadtest-run" v-loading="loading">
    <PageHeaderBar :title="run?.scenarioName ?? '压测运行'"
      :subtitle="run?.targetBaseUrl ?? ''" :back-to="backTo" sticky>
      <template #actions>
        <el-button :icon="Refresh" @click="load">刷新</el-button>
        <el-button :icon="Download" :disabled="!run?.hasSummary" :loading="downloadingSummary"
          @click="handleDownloadSummary">下载摘要</el-button>
        <el-button :icon="Document" :disabled="!run?.hasLog" :loading="downloadingLog"
          @click="handleDownloadLog">下载日志</el-button>
        <el-button v-if="isActive" type="danger" :icon="CircleClose" :loading="canceling" @click="handleCancel">
          取消
        </el-button>
      </template>
    </PageHeaderBar>

    <!-- 状态横幅：进页面第一眼就该知道这次跑成没成、阈值过没过 -->
    <el-alert v-if="run" :type="statusAlertType" :closable="false" class="status-banner" show-icon>
      <template #title>
        <span class="status-title">{{ EXECUTION_STATUS_LABELS[run.status] }}</span>
        <span class="status-meta">
          耗时 {{ formatDuration(run.durationMs) }}
          <template v-if="run.thresholdTotal > 0">
            · 阈值 {{ run.thresholdTotal - run.thresholdFailed }}/{{ run.thresholdTotal }} 通过
          </template>
        </span>
      </template>
      <div v-if="run.errorMessage" class="status-error">{{ run.errorMessage }}</div>
    </el-alert>

    <!-- 指标卡片。p95/p99 为 null 表示脚本没在 summaryTrendStats 里声明该分位，
         显示「未采集」而不是 0——0ms 会被误读成"快得离谱" -->
    <div class="metric-grid">
      <div class="metric-card">
        <div class="metric-label">RPS</div>
        <div class="metric-value">{{ run?.rps ?? '—' }}</div>
      </div>
      <div class="metric-card">
        <div class="metric-label">p95 耗时</div>
        <div class="metric-value">{{ formatMs(run?.p95Ms) }}</div>
      </div>
      <div class="metric-card">
        <div class="metric-label">p99 耗时</div>
        <div class="metric-value">{{ formatMs(run?.p99Ms) }}</div>
      </div>
      <div class="metric-card">
        <div class="metric-label">平均耗时</div>
        <div class="metric-value">{{ formatMs(run?.avgMs) }}</div>
      </div>
      <div class="metric-card">
        <div class="metric-label">错误率</div>
        <div class="metric-value" :class="{ 'is-bad': (run?.errorRate ?? 0) > 0 }">
          {{ formatRate(run?.errorRate) }}
        </div>
      </div>
      <div class="metric-card">
        <div class="metric-label">检查通过率</div>
        <div class="metric-value">{{ formatRate(run?.checksRate) }}</div>
      </div>
      <div class="metric-card">
        <div class="metric-label">请求数</div>
        <div class="metric-value">{{ run?.totalRequests ?? '—' }}</div>
      </div>
      <div class="metric-card">
        <div class="metric-label">最大 VU</div>
        <div class="metric-value">{{ run?.vusMax ?? '—' }}</div>
      </div>
    </div>

    <!-- 阈值结果：哪条没过一目了然 -->
    <el-card class="section-card">
      <template #header><span class="section-title">阈值结果</span></template>
      <el-table :data="run?.thresholdResults ?? []" size="small" empty-text="本次运行未设置阈值">
        <el-table-column label="表达式" min-width="260">
          <template #default="{ row }">
            <span class="threshold-expression">{{ row.expression }}</span>
          </template>
        </el-table-column>
        <el-table-column label="通过" width="100">
          <template #default="{ row }">
            <el-tag :type="row.ok ? 'success' : 'danger'" size="small">{{ row.ok ? '通过' : '未通过' }}</el-tag>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card class="section-card">
      <template #header><span class="section-title">运行信息</span></template>
      <el-descriptions :column="isMobile ? 1 : 3" border size="small">
        <el-descriptions-item label="目标地址">{{ run?.targetBaseUrl || '—' }}</el-descriptions-item>
        <el-descriptions-item label="k6 版本">{{ run?.k6Version || '—' }}</el-descriptions-item>
        <el-descriptions-item label="退出码">
          <span v-if="run?.exitCode !== null && run?.exitCode !== undefined">{{ run.exitCode }}</span>
          <span v-else>—</span>
        </el-descriptions-item>
        <el-descriptions-item label="开始时间">{{ formatFullDateTime(run?.startedAt) }}</el-descriptions-item>
        <el-descriptions-item label="结束时间">{{ formatFullDateTime(run?.endedAt) }}</el-descriptions-item>
        <el-descriptions-item label="耗时">{{ formatDuration(run?.durationMs) }}</el-descriptions-item>
      </el-descriptions>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { CircleClose, Document, Download, Refresh } from '@element-plus/icons-vue'
import PageHeaderBar from '@/components/common/PageHeaderBar.vue'
import {
  cancelLoadTestRun, downloadLoadTestRunLog, downloadLoadTestRunSummary, getLoadTestRun,
} from '@/api/loadtest'
import { formatDuration, formatFullDateTime } from '@/utils/formatter'
import { EXECUTION_STATUS_LABELS, ExecutionStatus } from '@/types/execution'
import { formatMs, formatRate, type LoadTestRunDetail } from '@/types/loadtest'
import { useBreakpoint } from '@/composables/useBreakpoint'

const route = useRoute()
const runId = route.params.runId as string
const { isMobile } = useBreakpoint()

const run = ref<LoadTestRunDetail | null>(null)
const loading = ref(false)
const canceling = ref(false)
const downloadingSummary = ref(false)
const downloadingLog = ref(false)

/** 返回场景详情；运行还没加载出来时退回列表，避免指向一个空 id */
const backTo = computed(() => (run.value ? `/loadtests/${run.value.scenarioId}` : '/loadtests'))

const isActive = computed(() =>
  run.value?.status === ExecutionStatus.Pending || run.value?.status === ExecutionStatus.Running)

const statusAlertType = computed(() => {
  switch (run.value?.status) {
    case ExecutionStatus.Passed: return 'success'
    case ExecutionStatus.Failed:
    case ExecutionStatus.Error: return 'error'
    case ExecutionStatus.Canceled: return 'warning'
    default: return 'info'
  }
})

const load = async () => {
  loading.value = true
  try {
    run.value = await getLoadTestRun(runId)
  } finally {
    loading.value = false
  }
}

const handleCancel = async () => {
  try {
    await ElMessageBox.confirm('只终止本次压测，不影响其它场景。确认取消？', '取消运行', { type: 'warning' })
  } catch {
    return
  }
  canceling.value = true
  try {
    await cancelLoadTestRun(runId)
    ElMessage.success('已请求取消')
    await load()
  } finally {
    canceling.value = false
  }
}

const handleDownloadSummary = async () => {
  if (!run.value) return
  downloadingSummary.value = true
  try {
    await downloadLoadTestRunSummary(runId, run.value.scenarioName)
  } finally {
    downloadingSummary.value = false
  }
}

const handleDownloadLog = async () => {
  if (!run.value) return
  downloadingLog.value = true
  try {
    await downloadLoadTestRunLog(runId, run.value.scenarioName)
  } finally {
    downloadingLog.value = false
  }
}

// ------------------------------ 运行中自动刷新
//
// 运行是异步的：发起后页面拿到的是 Pending，指标要等 k6 跑完才落库。
// 不轮询的话用户只能一直手动点刷新。这里每 5 秒静默重拉一次，跑完自动停。
const POLL_INTERVAL_MS = 5000
let pollTimer: ReturnType<typeof setInterval> | undefined

const stopPolling = () => {
  if (pollTimer !== undefined) {
    clearInterval(pollTimer)
    pollTimer = undefined
  }
}

const startPolling = () => {
  if (pollTimer !== undefined) return
  pollTimer = setInterval(async () => {
    if (!isActive.value) {
      stopPolling()
      return
    }
    try {
      run.value = await getLoadTestRun(runId)
    } catch {
      // 轮询失败不打断页面：下一轮会再试，避免网络抖动弹一堆错误提示
    }
  }, POLL_INTERVAL_MS)
}

onMounted(async () => {
  await load()
  if (isActive.value) startPolling()
})

onUnmounted(stopPolling)
</script>

<style scoped>
.loadtest-run {
  padding-bottom: 12px;
}

.status-banner {
  margin-bottom: 12px;
}

.status-title {
  margin-right: 10px;
  font-weight: 500;
}

.status-meta {
  font-size: 13px;
  opacity: 0.85;
}

.status-error {
  margin-top: 6px;
  font-size: 13px;
  line-height: 1.6;
  word-break: break-word;
}

.metric-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 12px;
  margin-bottom: 12px;
}

.metric-card {
  padding: 14px 16px;
  background: #fff;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  box-shadow: 0 1px 2px rgba(31, 59, 115, 0.04);
}

.metric-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.metric-value {
  margin-top: 6px;
  font-size: 22px;
  font-weight: 600;
  color: var(--el-text-color-primary);
  font-variant-numeric: tabular-nums;
}

/* 错误率 > 0 时标红：这是压测里最该被注意的指标 */
.metric-value.is-bad {
  color: var(--el-color-danger);
}

.section-card {
  margin-bottom: 12px;
}

.section-title {
  font-size: 15px;
  font-weight: 600;
}

.threshold-expression {
  font-family: Consolas, Monaco, monospace;
}
</style>
