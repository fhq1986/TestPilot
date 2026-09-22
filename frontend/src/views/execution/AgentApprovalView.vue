<template>
  <div class="approval-page">
    <el-card class="approval-card">

      <!-- 自愈度量：把「Agent 修得怎么样」摆在审批动作之前；
           误判数 > 0 时整张卡转危险色，提示可能有真实缺陷被自愈掩盖，需要人工回看 -->
      <div v-loading="metricsLoading" class="heal-strip">
        <div class="heal-items">
          <div class="heal-item tone-total">
            <span class="heal-value">{{ metrics?.totalAttempts ?? 0 }}</span>
            <span class="heal-label">自愈尝试</span>
          </div>
          <div class="heal-item tone-ok">
            <span class="heal-value">{{ successRate.value }}<small v-if="successRate.unit">{{ successRate.unit }}</small></span>
            <span class="heal-label">修复成功率</span>
          </div>
          <div class="heal-item tone-plain">
            <span class="heal-value">{{ avgFix.value }}<small v-if="avgFix.unit">{{ avgFix.unit }}</small></span>
            <span class="heal-label">平均修复时长</span>
          </div>
          <div class="heal-item tone-ok">
            <span class="heal-value">{{ fixedPartial.value }}<small v-if="fixedPartial.unit">{{ fixedPartial.unit }}</small></span>
            <span class="heal-label">已修复 / 部分</span>
          </div>
          <div class="heal-item" :class="misjudged > 0 ? 'tone-bad' : 'tone-plain'">
            <span class="heal-value">{{ misjudged }}</span>
            <span class="heal-label">疑似误判</span>
          </div>
        </div>
        <!-- margin-left:auto 而不是加一个 flex:1 的占位块：窄屏换行时切换按钮仍靠右，
             不会孤零零落在第二行最左边 -->
        <el-radio-group v-model="healDays" size="small" class="heal-range" @change="loadMetrics">
          <el-radio-button :value="7">近 7 天</el-radio-button>
          <el-radio-button :value="30">近 30 天</el-radio-button>
          <el-radio-button :value="0">全部</el-radio-button>
        </el-radio-group>
      </div>

      <el-tabs v-model="activeTab" @tab-change="onTabChange">
        <el-tab-pane label="待审批" name="pending" />
        <el-tab-pane label="已批准" name="approved" />
        <el-tab-pane label="已拒绝" name="rejected" />
      </el-tabs>
      <div style="padding-bottom:5px;">
        <el-alert type="warning" :closable="false" class="boundary-tip">
          <template #title>
            需人工确认的 Agent 修复建议：破坏性 / 结构类改动默认不自动应用，采纳后会写入真实用例步骤
          </template>
        </el-alert>
      </div>

      <div class="table-wrap">
        <el-table v-loading="loading" :data="items" height="100%" border class="wrap-table">
          <el-table-column label="用例" width="180" fixed="left">
            <template #default="{ row }">
              <el-link type="primary" @click="openExecution(row.executionId)">{{ row.testCaseName }}</el-link>
            </template>
          </el-table-column>
          <el-table-column label="步骤" width="60">
            <template #default="{ row }">#{{ row.targetStepOrder }}</template>
          </el-table-column>
          <el-table-column label="修复类别" width="120">
            <template #default="{ row }">{{ FIX_CATEGORY_LABELS[row.fixCategory] ?? '未知' }}</template>
          </el-table-column>
          <el-table-column label="置信度" width="70">
            <template #default="{ row }">{{ Math.round((row.confidence ?? 0) * 100) }}%</template>
          </el-table-column>
          <el-table-column label="修复建议">
            <template #default="{ row }">{{ row.fixSummary || '—' }}</template>
          </el-table-column>
          <el-table-column label="结果" width="90">
            <template #default="{ row }">
              <el-tag size="small" :type="agentAttemptResultTagType(row.result)">
                {{ AGENT_ATTEMPT_RESULT_LABELS[row.result] ?? '未知' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="时间" width="130">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="140" fixed="right">
            <template #default="{ row }">
              <template v-if="activeTab === 'pending'">
                <el-button size="small" type="primary" :loading="acting === row.attemptId"
                  @click="approve(row)">采纳</el-button>
                <el-button size="small" :loading="acting === row.attemptId" @click="reject(row)">驳回</el-button>
              </template>
              <span v-else class="muted">{{ row.approved ? '已批准' : '已拒绝' }}</span>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <el-pagination v-model:current-page="page" v-model:page-size="pageSize"
        :total="total" :page-sizes="[20, 50, 100]"
        layout="total, sizes, prev, pager, next"
        class="pager" @current-change="onPageChange" @size-change="onPageSizeChange" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  approveAgentAttempt, getAgentApprovals, getAgentHealMetrics, rejectAgentAttempt,
} from '@/api/execution'
import {
  AGENT_ATTEMPT_RESULT_LABELS, FIX_CATEGORY_LABELS, agentAttemptResultTagType,
  type AgentApprovalItem, type AgentHealMetrics,
} from '@/types/execution'
import { formatDateTime } from '@/utils/formatter'

const router = useRouter()
const activeTab = ref<'pending' | 'approved' | 'rejected'>('pending')
const items = ref<AgentApprovalItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)
/** 正在处理的 attemptId（按钮 loading，防重复点击） */
const acting = ref<string | null>(null)

// ------------------------------------------------------------ 自愈度量
const metrics = ref<AgentHealMetrics | null>(null)
const metricsLoading = ref(false)
/** 度量时间窗口（天）：7 / 30；0 = 全部 */
const healDays = ref(7)

/**
 * 指标值拆成「数字 + 单位」两段：单位用小字淡色排，否则 26px 的 "%"、"秒"
 * 会和数字一样抢视线，读数反而变慢。取不到数据时统一给「—」。
 */
type MetricText = { value: string; unit: string }

const successRate = computed<MetricText>(() => {
  if (!metrics.value) return { value: '—', unit: '' }
  return { value: String(Math.round(metrics.value.successRate * 100)), unit: '%' }
})

const avgFix = computed<MetricText>(() => {
  const m = metrics.value?.avgFixMinutes
  if (m === null || m === undefined) return { value: '—', unit: '' }
  return m < 1
    ? { value: String(Math.round(m * 60)), unit: '秒' }
    : { value: m.toFixed(1), unit: '分' }
})

/** 已修复 / 部分：斜杠后半段是同一个小字，读作「已修复 2，部分 0」 */
const fixedPartial = computed<MetricText>(() => {
  if (!metrics.value) return { value: '—', unit: '' }
  return { value: String(metrics.value.fixed), unit: `/ ${metrics.value.partial}` }
})

const misjudged = computed(() => metrics.value?.misjudgedCount ?? 0)

const loadMetrics = async () => {
  metricsLoading.value = true
  try {
    const to = new Date()
    const from = healDays.value > 0
      ? new Date(to.getTime() - healDays.value * 24 * 60 * 60 * 1000)
      : null
    metrics.value = await getAgentHealMetrics({
      from: from?.toISOString(),
      to: to.toISOString(),
    })
  } catch {
    // 度量拉不到不该弹错：它是辅助信息，审批表本身仍可用
    metrics.value = null
  } finally {
    metricsLoading.value = false
  }
}

const load = async () => {
  loading.value = true
  try {
    const res = await getAgentApprovals({
      status: activeTab.value,
      page: page.value,
      pageSize: pageSize.value,
    })
    items.value = res.items
    total.value = res.total
  } catch {
    items.value = []
    total.value = 0
  } finally {
    loading.value = false
  }
}

const onTabChange = () => {
  page.value = 1
  void load()
}

const onPageChange = () => void load()
const onPageSizeChange = (size: number) => {
  pageSize.value = size
  page.value = 1
  void load()
}

const openExecution = (id: string) => router.push(`/executions/${id}`)

const approve = async (row: AgentApprovalItem) => {
  const confirmed = await ElMessageBox.confirm(
    `将把该修复应用到真实用例步骤，请确认：\n${row.fixSummary || '（无描述）'}`,
    '采纳 Agent 修复',
    { type: 'warning', confirmButtonText: '采纳', cancelButtonText: '取消' },
  ).catch(() => false)
  if (!confirmed) return
  acting.value = row.attemptId
  try {
    const res = await approveAgentAttempt(row.attemptId)
    ElMessage.success(res?.applied ? `已采纳并应用到 ${res.applied} 个步骤` : '已标记采纳（无可用动作可应用）')
    await load()
  } finally {
    acting.value = null
  }
}

const reject = async (row: AgentApprovalItem) => {
  acting.value = row.attemptId
  try {
    await rejectAgentAttempt(row.attemptId)
    ElMessage.success('已驳回')
    await load()
  } finally {
    acting.value = null
  }
}

onMounted(() => {
  void loadMetrics()
  void load()
})
</script>

<style scoped>
/* 整页纵向布局：高度 = 视口 - 顶栏 56px - main 上下内边距 32px */
.approval-page {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 56px - 32px);
  overflow: hidden;
}

.approval-card {
  flex: 1;
  min-height: 0;
  margin-top: 0;
  /* 让 el-card body 也继承 flex 布局 */
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.approval-card :deep(.el-card__body) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.muted {
  color: var(--el-text-color-secondary);
}

/* ---------------------------------------------------------------- 自愈度量条 */
/* 5 张指标卡 + 右侧时间窗切换：先看「Agent 修得怎么样」再看待办。
   卡片沿用 PlanReportPanel 的 stat-card 语言（顶色条 + 淡彩底 + hover 浮起），
   同一套「指标卡」在平台各处长得一样，用户不必重新认一遍。 */
.heal-strip {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding: 2px 2px 16px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  flex-shrink: 0;
}

/* 卡片自己一组，便于窄屏时整体换行；组内再换行 */
.heal-items {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}

/* 时间窗切换始终贴右。用 margin-left:auto 而不是 flex:1 占位块——
   窄屏换行后占位块留在上一行，切换按钮会孤零零落在第二行最左边 */
.heal-range {
  margin-left: auto;
}

.heal-item {
  position: relative;
  overflow: hidden;
  box-sizing: border-box;
  min-width: 120px;
  padding: 10px 14px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  background: #fff;
  transition: transform 0.18s ease, box-shadow 0.18s ease;
}

/* 顶部 3px 色条：扫一眼按颜色分辨指标含义 */
.heal-item::before {
  content: '';
  position: absolute;
  inset: 0 0 auto;
  height: 3px;
  background: var(--el-border-color);
}

.heal-item:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 14px rgba(31, 59, 115, 0.10);
}

/* 淡彩边框 + 同色浅底：边框随指标语义走，比灰边有辨识度又不抢数字 */
.tone-total {
  border-color: rgba(63, 127, 212, 0.38);
  background: linear-gradient(180deg, #f6faff 0%, #fff 46%);
}
.tone-total::before { background: linear-gradient(90deg, #1f3b73, #3f7fd4); }

.tone-ok {
  border-color: rgba(103, 194, 58, 0.45);
  background: linear-gradient(180deg, #f6fcf3 0%, #fff 46%);
}
.tone-ok::before { background: var(--el-color-success); }

.tone-bad {
  border-color: rgba(245, 108, 108, 0.45);
  background: linear-gradient(180deg, #fef5f5 0%, #fff 46%);
}
.tone-bad::before { background: var(--el-color-danger); }

/* 中性指标（平均时长 / 无误判时的误判数）：只留一条灰蓝条，不喧宾夺主 */
.tone-plain::before { background: var(--el-color-info); }

.heal-value {
  display: block;
  font-size: 26px;
  font-weight: 600;
  line-height: 1.2;
  color: #1f2d3d;
  /* 等宽数字：轮询刷新时数字不会左右跳动 */
  font-variant-numeric: tabular-nums;
}

/* 单位（% / 秒 / 分 / 「/ 部分」）用小字淡色，别跟数字抢视线 */
.heal-value small {
  margin-left: 2px;
  font-size: 13px;
  font-weight: 500;
  color: var(--el-text-color-secondary);
}

.heal-label {
  display: block;
  margin-top: 4px;
  font-size: 12px;
  color: #909399;
}

/* 误判数 > 0：可能有真实缺陷被自愈掩盖，数字转危险色提醒回看 */
.heal-item.tone-bad .heal-value {
  color: var(--el-color-danger);
}

.wrap-table :deep(.cell) {
  white-space: normal;
  word-break: break-all;
  line-height: 1.5;
}

.table-wrap {
  flex: 1;
  min-height: 0;
  margin-top: 8px;
}

.pager {
  margin-top: 12px;
  justify-content: flex-end;
  flex-shrink: 0;
}
</style>
