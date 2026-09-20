<template>
  <div class="plan-detail" v-loading="loading">
    <PageHeaderBar :title="plan?.name ?? '测试计划'" :subtitle="plan?.releaseName ?? ''"
      back-to="/test-plans" sticky>
      <template #actions>
        <el-button :icon="Download" :loading="exporting" @click="handleExport">导出报告</el-button>
        <el-button :icon="Share" :loading="sharing" @click="handleShare">分享</el-button>
        <el-button v-if="plan && plan.status !== 3" :icon="FolderChecked"
          @click="changeStatus(3, '归档')">归档</el-button>
        <el-button v-if="plan && plan.status === 1" type="success" :icon="Select"
          @click="changeStatus(2, '完成')">标记完成</el-button>
        <el-button v-if="plan && plan.status === 0" type="primary" :icon="VideoPlay"
          @click="changeStatus(1, '开始')">开始</el-button>
        <el-button type="primary" :icon="Refresh" @click="loadAll">刷新</el-button>
      </template>
    </PageHeaderBar>

    <!-- 达标判定横幅：进页面第一眼就该知道过没过 -->
    <el-alert v-if="report" :type="report.gate.passed ? 'success' : 'error'" :closable="false"
      class="gate-banner" show-icon>
      <template #title>
        <span class="gate-title">
          达标判定：{{ report.gate.passed ? '已达标' : '未达标' }}
        </span>
        <span class="gate-meta">
          目标 {{ (report.gate.targetPassRate * 100).toFixed(0) }}% ·
          实际 {{ (report.gate.stats.passRate * 100).toFixed(1) }}% ·
          <template v-if="report.gate.evaluatedRoundNo">
            判定依据：第 {{ report.gate.evaluatedRoundNo }} 轮
          </template>
          <template v-else>尚无轮次</template>
        </span>
      </template>
      <div v-if="report.gate.reasons.length > 0" class="gate-reasons">
        <div v-for="(reason, i) in report.gate.reasons" :key="i">· {{ reason }}</div>
      </div>
    </el-alert>

    <el-card class="tab-card">
      <el-tabs v-model="activeTab">
        <el-tab-pane label="概览" name="overview">
          <PlanOverviewPanel :plan="plan" :running-round="runningRound" :starting="starting"
            @start="handleStartRound" @abort="handleAbort" />
        </el-tab-pane>

        <el-tab-pane name="scope">
          <template #label>
            <span>范围<el-badge v-if="(plan?.scopeIssues.length ?? 0) > 0"
              :value="plan?.scopeIssues.length" class="tab-badge" /></span>
          </template>
          <PlanScopePanel :plan="plan" :items="items" :running-round="runningRound"
            @add-cases="openCasePicker" @import-suite="openSuiteImport"
            @remove="removeFromScope" @reorder="persistOrder" @clear="handleClearScope" />
        </el-tab-pane>

        <el-tab-pane name="rounds">
          <template #label>
            <span>轮次<el-badge v-if="rounds.length" :value="rounds.length" type="info" class="tab-badge" /></span>
          </template>
          <PlanRoundsPanel :rounds="rounds" :target-pass-rate="plan?.targetPassRate ?? 0.95"
            @abort="handleAbort" />
        </el-tab-pane>

        <el-tab-pane label="报告" name="report">
          <PlanReportPanel :report="report" :target-pass-rate="plan?.targetPassRate ?? 0.95" />
        </el-tab-pane>
      </el-tabs>
    </el-card>

    <!-- 添加用例 / 从套件导入：状态与请求自包含，父级负责落库合并与刷新 -->
    <PlanCasePickerDialog ref="casePickerRef" @apply="applyPickedCases" />
    <PlanSuiteImportDialog ref="suiteImportRef" :plan-id="planId" @imported="loadAll" />

    <!-- 计划没配默认环境时开轮先选环境：相对地址 Navigate / 自动登录都依赖环境的 BaseUrl -->
    <el-dialog v-model="envDialogVisible" title="选择执行环境" width="520px">
      <div v-loading="envLoading">
        <el-select v-model="selectedEnvId" style="width: 100%" placeholder="选择环境">
          <el-option label="不使用环境" value="" />
          <el-option v-for="env in envOptions" :key="env.id" :label="env.name" :value="env.id">
            <div class="environment-option">
              <span>{{ env.name }}</span>
              <span class="environment-option-url">{{ env.baseUrl }}</span>
            </div>
          </el-option>
        </el-select>
        <div class="env-hint">不选环境时，UI 用例里的相对地址（如 /login）将无法拼接完整 URL，自动登录也不会执行。</div>
      </div>
      <template #footer>
        <el-button @click="envDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="starting" @click="confirmStartRound">开始新一轮</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Download, FolderChecked, Refresh, Select, Share, VideoPlay } from '@element-plus/icons-vue'
import PageHeaderBar from '@/components/common/PageHeaderBar.vue'
import PlanOverviewPanel from '@/components/testplan/PlanOverviewPanel.vue'
import PlanScopePanel from '@/components/testplan/PlanScopePanel.vue'
import PlanRoundsPanel from '@/components/testplan/PlanRoundsPanel.vue'
import PlanReportPanel from '@/components/testplan/PlanReportPanel.vue'
import PlanCasePickerDialog from '@/components/testplan/PlanCasePickerDialog.vue'
import PlanSuiteImportDialog from '@/components/testplan/PlanSuiteImportDialog.vue'
import {
  abortPlanRoundApi, exportPlanReportApi, getTestPlanApi,
  importPlanItemsFromSuiteApi, listPlanRoundsApi, listTestPlanItemsApi, planReportApi,
  setTestPlanItemsApi, setTestPlanStatusApi, startPlanRoundApi,
} from '@/api/testPlan'
import { createShare } from '@/api/share'
import { getEnvironments } from '@/api/environment'
import { ReportShareKind } from '@/types/share'
import { PlanRoundStatus, type PlanRoundSummary, type TestPlanDetail, type TestPlanItem, type TestPlanReport } from '@/types/testPlan'
import type { EnvironmentView } from '@/types/environment'

const route = useRoute()
const planId = route.params.id as string

const plan = ref<TestPlanDetail | null>(null)
const items = ref<TestPlanItem[]>([])
const rounds = ref<PlanRoundSummary[]>([])
const report = ref<TestPlanReport | null>(null)
const loading = ref(false)
const starting = ref(false)
// 开轮前环境选择（计划未配默认环境时弹出）
const envDialogVisible = ref(false)
const envLoading = ref(false)
const envOptions = ref<EnvironmentView[]>([])
const selectedEnvId = ref('')
const activeTab = ref('overview')

const runningRound = computed(() => rounds.value.find((r) => r.status === PlanRoundStatus.Running) ?? null)

async function loadAll() {
  loading.value = true
  try {
    const [detail, itemList, roundList, rep] = await Promise.all([
      getTestPlanApi(planId),
      listTestPlanItemsApi(planId),
      listPlanRoundsApi(planId),
      planReportApi(planId).catch(() => null),
    ])
    plan.value = detail
    items.value = itemList
    rounds.value = roundList
    report.value = rep
  } finally {
    loading.value = false
  }
}

async function changeStatus(status: number, label: string) {
  await ElMessageBox.confirm(`确认将计划标记为「${label}」？`, '状态流转', { type: 'info' })
  const result = await setTestPlanStatusApi(planId, status as 0)
  ElMessage.success(result.message)
  await loadAll()
}

const exporting = ref(false)
async function handleExport() {
  if (!plan.value) return
  exporting.value = true
  try {
    await exportPlanReportApi(planId, plan.value.name)
    ElMessage.success('已开始下载验收报告')
  } finally {
    exporting.value = false
  }
}

const sharing = ref(false)

/**
 * 生成免登录分享链接。
 *
 * 有效期默认 7 天：验收报告是阶段性材料，长期有效的链接会变成事实上的公开数据。
 * 需要长期留存时导出 xlsx 归档更合适。
 */
async function handleShare() {
  sharing.value = true
  try {
    const link = await createShare({
      kind: ReportShareKind.TestPlan,
      refId: planId,
      expiresInDays: 7,
    })
    await ElMessageBox.confirm(
      `分享链接已生成（7 天后过期，可随时吊销）：\n${link.url}\n\n点击「复制链接」把链接放到剪贴板。`,
      '分享验收报告',
      { confirmButtonText: '复制链接', cancelButtonText: '关闭', type: 'success' },
    )
      .then(async () => {
        const ok = await copyText(link.url)
        ElMessage[ok ? 'success' : 'warning'](ok ? '链接已复制到剪贴板' : '复制失败，请手动选中链接复制')
      })
      .catch(() => undefined)
  } finally {
    sharing.value = false
  }
}

/** 复制文本：优先 Clipboard API，非安全上下文（http）下退回旧式 execCommand */
async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // 落空则继续走 execCommand 回退
  }
  try {
    const el = document.createElement('textarea')
    el.value = text
    el.style.position = 'fixed'
    el.style.opacity = '0'
    document.body.appendChild(el)
    el.select()
    const ok = document.execCommand('copy')
    document.body.removeChild(el)
    return ok
  } catch {
    return false
  }
}

async function handleStartRound() {
  if (!plan.value) return

  // 计划没配默认环境 → 先弹环境选择框（项目里有环境可选时才弹）。
  // 相对地址 Navigate 和自动登录都依赖环境的 BaseUrl，不选环境 UI 用例必挂。
  if (!plan.value.environmentId && plan.value.projectId) {
    envLoading.value = true
    try {
      envOptions.value = await getEnvironments(plan.value.projectId)
    } finally {
      envLoading.value = false
    }
    if (envOptions.value.length > 0) {
      selectedEnvId.value = ''
      envDialogVisible.value = true
      return // 确认后走 confirmStartRound
    }
  }

  await confirmAndStart()
}

/** 环境对话框确认：带所选环境进入后续流程 */
async function confirmStartRound() {
  envDialogVisible.value = false
  await confirmAndStart(selectedEnvId.value || undefined)
}

async function confirmAndStart(environmentId?: string) {
  if (!plan.value) return
  const broken = plan.value.scopeIssues.filter((i) => i.level === 'Error').length
  if (broken > 0) {
    try {
      await ElMessageBox.confirm(
        `范围里有 ${broken} 个不可执行的用例（执行时会被跳过）。仍然开始新一轮？`,
        '范围体检有警告',
        { type: 'warning' },
      )
    } catch {
      return
    }
  } else {
    try {
      await ElMessageBox.confirm(
        `将按当前范围（${plan.value.caseCount} 个用例）开启新一轮执行。`,
        '开启新一轮',
        { type: 'info' },
      )
    } catch {
      return
    }
  }

  starting.value = true
  try {
    const result = await startPlanRoundApi(planId, environmentId ? { environmentId } : undefined)
    ElMessage.success(`第 ${result.roundNo} 轮已开始，共创建 ${result.created} 条执行`)
    activeTab.value = 'rounds'
    await loadAll()
  } finally {
    starting.value = false
  }
}

async function handleAbort(round: PlanRoundSummary) {
  try {
    await ElMessageBox.confirm(
      '只把尚未开始的执行标记为跳过，已经在跑的不会被打断。确认中止本轮？',
      '中止轮次',
      { type: 'warning' },
    )
  } catch {
    return
  }
  const result = await abortPlanRoundApi(round.id)
  ElMessage.success(result.message)
  await loadAll()
}

// ------------------------------------------------------------ 范围维护
const casePickerRef = ref<InstanceType<typeof PlanCasePickerDialog>>()
const suiteImportRef = ref<InstanceType<typeof PlanSuiteImportDialog>>()

const openCasePicker = () => casePickerRef.value?.open(plan.value?.projectId, items.value.map((i) => i.testCaseId))
const openSuiteImport = () => suiteImportRef.value?.open(plan.value?.projectId)

async function applyPickedCases(pickedIds: string[]) {
  const merged = [...items.value.map((i) => i.testCaseId), ...pickedIds]
  await setTestPlanItemsApi(planId, merged)
  ElMessage.success(`已追加 ${pickedIds.length} 个用例`)
  await loadAll()
}

async function removeFromScope(testCaseId?: string | null) {
  if (!testCaseId) return
  const merged = items.value.map((i) => i.testCaseId).filter((id) => id !== testCaseId)
  await setTestPlanItemsApi(planId, merged)
  ElMessage.success('已从范围移除')
  await loadAll()
}

/** 清空整个范围：确认框在子组件里做，这里只管落库 */
async function handleClearScope() {
  await setTestPlanItemsApi(planId, [])
  ElMessage.success('已清空用例范围')
  await loadAll()
}

/** 落库：排序即改范围顺序，接口会按传入顺序重写 Order */
async function persistOrder(ids: string[]) {
  try {
    await setTestPlanItemsApi(planId, ids)
    await loadAll()
  } catch {
    // 失败时重载回服务端的真实顺序，避免界面停在一个没落库的假状态
    await loadAll()
  }
}

onMounted(loadAll)

// ------------------------------ 执行中自动刷新
//
// 轮次跑起来后通过率/耗时/结果都在变，原来只能手动点「刷新」才看得到。
// 这里做轮询：只要还存在「进行中」的轮次，就每 5 秒静默重拉一次轮次列表与达标判定；
// 轮次全部结束后自动停掉。用静默刷新（不动整页 loading 遮罩），避免每 5 秒闪一次。
const POLL_INTERVAL_MS = 5000
let pollTimer: ReturnType<typeof setInterval> | undefined

/** 只重拉轮次 + 达标判定：执行过程中用例范围不会变，没必要连 items 一起拉 */
async function refreshRoundsQuietly() {
  try {
    const [roundList, rep] = await Promise.all([
      listPlanRoundsApi(planId),
      planReportApi(planId).catch(() => null),
    ])
    rounds.value = roundList
    report.value = rep
  } catch {
    // 轮询失败不打断页面：下一轮会再试，避免网络抖动弹一堆错误提示
  }
}

function stopPolling() {
  if (pollTimer !== undefined) {
    clearInterval(pollTimer)
    pollTimer = undefined
  }
}

function startPolling() {
  if (pollTimer !== undefined) return
  pollTimer = setInterval(() => {
    // 没有进行中的轮次了（跑完/中止）→ 停止轮询
    if (!runningRound.value) {
      stopPolling()
      return
    }
    void refreshRoundsQuietly()
  }, POLL_INTERVAL_MS)
}

// 进行中的轮次出现/消失时自动开关轮询（含首屏加载后已有运行中轮次的情况）
watch(runningRound, (round) => (round ? startPolling() : stopPolling()), { immediate: true })

onUnmounted(stopPolling)
</script>

<style scoped>
.plan-detail { padding-bottom: 12px; }

/* 环境选择：名称 + BaseUrl 两段展示，提示语解释"不选环境的后果" */
.environment-option { display: flex; justify-content: space-between; gap: 12px; }
.environment-option-url { color: #9aa2ae; font-size: 12px; }
.env-hint { margin-top: 8px; font-size: 12px; color: #9aa2ae; line-height: 1.6; }

.gate-banner { margin-bottom: 12px; }
.gate-title { font-weight: 500; margin-right: 10px; }
.gate-meta { font-size: 13px; opacity: 0.85; }
.gate-reasons { margin-top: 6px; font-size: 13px; line-height: 1.8; }

.tab-card :deep(.el-card__body) { padding-top: 8px; }

.tab-badge { margin-left: 6px; }
.tab-badge :deep(.el-badge__content) { transform: translateY(-8px); }
</style>
