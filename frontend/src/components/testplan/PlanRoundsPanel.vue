<template>
  <div>
    <el-empty v-if="rounds.length === 0" description="还没有执行过任何轮次" />
    <el-table v-else :data="rounds" size="small" class="round-table"
      @row-click="openRound">
      <el-table-column label="轮次" width="80">
        <template #default="{ row }">第 {{ row.roundNo }} 轮</template>
      </el-table-column>
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="roundStatusTag(row)" size="small" effect="light">
            {{ roundStatusLabel(row) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="结果" width="380">
        <template #default="{ row }">
          <!-- 用例总数用开轮时确定的 createdCount：运行中的轮次四类结果还是 0，求和会显示「共0个用例」 -->
          <span class="stat-line">
            共 {{ row.createdCount }} 个用例，其中
            通过 <b class="ok">{{ row.stats.passed }}</b> /
            失败 <b class="bad">{{ row.stats.failed }}</b> /
            错误 <b class="bad">{{ row.stats.error }}</b> /
            跳过 <b class="muted">{{ row.stats.skipped }}</b>
          </span>
        </template>
      </el-table-column>
      <el-table-column label="通过率" width="130">
        <template #default="{ row }">
          <el-tag v-if="row.stats.total > 0"
            :type="row.stats.passRate >= targetPassRate ? 'success' : 'danger'"
            size="small" effect="plain">
            {{ (row.stats.passRate * 100).toFixed(1) }}%
          </el-tag>
          <span v-else class="muted">—</span>
        </template>
      </el-table-column>
      <el-table-column label="开始时间" width="170">
        <template #default="{ row }">{{ formatDateTime(row.startedAt) }}</template>
      </el-table-column>
      <el-table-column label="耗时" width="120">
        <template #default="{ row }">{{ roundDuration(row) }}</template>
      </el-table-column>
      <el-table-column label="操作" >
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click.stop="openRound(row)">明细</el-button>
          <el-button v-if="row.status === 0" link type="danger" size="small"
            @click.stop="emit('abort', row)">中止</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 轮次明细抽屉 -->
    <el-drawer v-model="roundDrawer" :title="roundDetail ? `第 ${roundDetail.round.roundNo} 轮明细` : '轮次明细'"
      size="80%">
      <template v-if="roundDetail">
        <el-descriptions :column="2" border size="small" class="round-desc">
          <el-descriptions-item label="状态">
            <el-tag :type="roundStatusTag(roundDetail.round)" size="small">
              {{ roundStatusLabel(roundDetail.round) }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="触发方式">{{ triggerLabel(roundDetail.round) }}</el-descriptions-item>
          <el-descriptions-item label="通过率">
            {{ (roundDetail.round.stats.passRate * 100).toFixed(1) }}%
          </el-descriptions-item>
          <el-descriptions-item label="耗时">{{ roundDuration(roundDetail.round) }}</el-descriptions-item>
        </el-descriptions>

        <el-table :data="roundDetail.cases" size="small" row-key="testCaseId" class="round-cases"
          :expand-row-keys="expandedKeys" @row-click="toggleExpand" @expand-change="onExpandChange">
          <el-table-column type="expand">
            <template #default="{ row }">
              <div class="case-execs">
                <div v-for="exec in row.executions" :key="exec.executionId" class="exec-line">
                  <el-tag :type="execTagType(exec.status)" size="small" effect="light">
                    {{ execStatusLabel(exec.status) }}
                  </el-tag>
                  <span class="exec-browser">{{ exec.browserName || '—' }}</span>
                  <span v-if="exec.dataSetRowLabel" class="exec-browser">{{ exec.dataSetRowLabel }}</span>
                  <span class="exec-duration">{{ exec.durationMs ?? 0 }}ms</span>
                  <span v-if="exec.errorMessage" class="exec-error">{{ exec.errorMessage }}</span>
                  <el-link v-if="exec.traceUrl" type="primary" class="exec-link"
                    @click="handleTraceDownload(exec.executionId)">trace</el-link>
                  <el-link v-if="exec.screenshotUrl" type="primary" :href="exec.screenshotUrl"
                    target="_blank" rel="noopener" class="exec-link">截图</el-link>
                </div>
              </div>
            </template>
          </el-table-column>
          <el-table-column prop="testCaseName" label="用例名称" min-width="240">
            <template #default="{ row, $index }">{{ $index + 1 }}.&nbsp;{{ row.testCaseName }}</template>
          </el-table-column>
          <el-table-column label="模块" width="140">
            <template #default="{ row }">{{ row.module || '—' }}</template>
          </el-table-column>
          <el-table-column label="执行数" width="90">
            <template #default="{ row }">{{ row.executions.length }}</template>
          </el-table-column>
          <el-table-column label="状态" width="120">
            <template #default="{ row }">
              <el-tag :type="execTagType(worstStatus(row))" size="small" effect="plain">
                {{ execStatusLabel(worstStatus(row)) }}
              </el-tag>
            </template>
          </el-table-column>
        </el-table>
      </template>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { onUnmounted, ref, watch } from 'vue'
import { getPlanRoundApi } from '@/api/testPlan'
import { downloadExecutionTrace } from '@/api/execution'
import { saveBlobAsFile } from '@/api/report'
import { formatDateTime } from '@/utils/formatter'
import type { PlanRoundCaseResult, PlanRoundSummary } from '@/types/testPlan'
import { PlanRoundStatus } from '@/types/testPlan'
import {
  execStatusLabel, execTagType, roundDuration, roundStatusLabel, roundStatusTag,
  triggerLabel, worstStatus,
} from '@/utils/planRound'

defineProps<{
  rounds: PlanRoundSummary[]
  /** 目标通过率：通过率 tag 的达标判定线 */
  targetPassRate: number
}>()

const emit = defineEmits<{ abort: [round: PlanRoundSummary] }>()

const roundDrawer = ref(false)
const roundDetail = ref<{ round: PlanRoundSummary; cases: PlanRoundCaseResult[] } | null>(null)

/** 受控展开的行 key：行点击与小箭头两条路径都汇到这里，状态始终一致 */
const expandedKeys = ref<string[]>([])

/** 点行任意位置切换展开/收起（不只点前面的小箭头） */
function toggleExpand(row: PlanRoundCaseResult) {
  expandedKeys.value = expandedKeys.value.includes(row.testCaseId)
    ? expandedKeys.value.filter((k) => k !== row.testCaseId)
    : [...expandedKeys.value, row.testCaseId]
}

/** 用户点小箭头时 Element Plus 自己改了内部展开态，同步回来避免 row-click 用旧数组算错 */
function onExpandChange(_row: PlanRoundCaseResult, expandedRows: PlanRoundCaseResult[]) {
  expandedKeys.value = expandedRows.map((r) => r.testCaseId)
}

async function openRound(row: PlanRoundSummary) {
  roundDetail.value = await getPlanRoundApi(row.id)
  expandedKeys.value = [] // 每次打开明细从全部收起开始
  roundDrawer.value = true
  startDetailPolling()
}

// ------------------------------ 执行中明细自动刷新
//
// 抽屉打开时若该轮还在跑，明细里的用例状态/耗时也在变，原来必须关掉重开才更新。
// 这里在「抽屉打开且该轮仍为进行中」时每 5 秒静默重拉一次明细；轮次结束或抽屉关闭即停。
const DETAIL_POLL_INTERVAL_MS = 5000
let detailTimer: ReturnType<typeof setInterval> | undefined

function stopDetailPolling() {
  if (detailTimer !== undefined) {
    clearInterval(detailTimer)
    detailTimer = undefined
  }
}

function startDetailPolling() {
  stopDetailPolling()
  if (roundDetail.value?.round.status !== PlanRoundStatus.Running) return
  detailTimer = setInterval(async () => {
    // 抽屉已关、或该轮已跑完/中止 → 停止轮询
    if (!roundDrawer.value || roundDetail.value?.round.status !== PlanRoundStatus.Running) {
      stopDetailPolling()
      return
    }
    try {
      roundDetail.value = await getPlanRoundApi(roundDetail.value.round.id)
    } catch {
      // 轮询失败不打断阅读：下一轮会再试
    }
  }, DETAIL_POLL_INTERVAL_MS)
}

// 抽屉关闭时停止轮询，避免关掉后还在后台空转
watch(roundDrawer, (open) => {
  if (!open) stopDetailPolling()
})
onUnmounted(stopDetailPolling)

/** trace 是受权端点（安全审查 S1），带 JWT 走 blob 下载 */
const handleTraceDownload = async (executionId: string) => {
  const blob = await downloadExecutionTrace(executionId)
  saveBlobAsFile(blob, `trace-${executionId}.zip`)
}
</script>

<style scoped>
.round-table :deep(.el-table__row) { cursor: pointer; }
.stat-line { font-size: 13px; }
.ok { color: var(--el-color-success); }
.bad { color: var(--el-color-danger); }
.muted { color: #9aa2ae; }

.round-desc { margin-bottom: 12px; }
.round-cases { width: 100%; }
/* 整行可点展开，行内 cursor 提示可点 */
.round-cases :deep(.el-table__row) { cursor: pointer; }
.case-execs { padding: 6px 16px; }
.exec-line { display: flex; align-items: flex-start; gap: 10px; padding: 3px 0; font-size: 13px; }
.exec-browser { color: #7a8290; }
.exec-duration { color: #9aa2ae; }
/* 展开后错误信息完整显示：多行换行，不再单行截断 */
.exec-error { color: var(--el-color-danger); flex: 1; min-width: 0; word-break: break-all; line-height: 1.6; }
.exec-link { font-size: 12px; }
</style>
