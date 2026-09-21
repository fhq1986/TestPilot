<template>
  <div class="execution-detail">
    <PageHeaderBar title="执行详情" :subtitle="execution?.testCaseName ?? ''" back-to="/executions" sticky>
      <template #actions>
        <el-button v-if="isRunning" type="danger" plain :icon="VideoPause" :loading="canceling"
          @click="handleCancel">终止执行</el-button>
        <el-button type="success" v-if="execution?.testCaseId" :icon="View" @click="router.push(`/testcases/${execution.testCaseId}`)">查看用例</el-button>
        <el-button type="primary" :icon="Download" :loading="reporting" @click="handleDownloadReport">生成报告</el-button>
        <el-button :icon="Refresh" @click="load">刷新</el-button>
      </template>
    </PageHeaderBar>

    <!-- 终止指令已发出、等待执行器落终态期间的锁定提示：此时按钮不可再点 -->
    <el-alert v-if="canceling" type="warning" :closable="false" class="canceling-alert">
      <template #title>
        <span class="canceling-text">
          <el-icon class="is-loading"><Loading /></el-icon>
          正在终止执行，请稍候…
        </span>
      </template>
    </el-alert>

    <el-card v-loading="loading" class="summary-card">
      <el-descriptions :column="3" border>
        <el-descriptions-item label="用例">
          <el-link type="primary" @click="router.push(`/testcases/${execution?.testCaseId}`)">
            {{ execution?.testCaseName }}
          </el-link>
        </el-descriptions-item>
        <el-descriptions-item label="所属项目">{{ execution?.projectName || '—' }}</el-descriptions-item>
        <el-descriptions-item label="状态">
          <el-tag :type="statusTagType">{{ statusLabel }}</el-tag>
          <el-tag v-if="execution?.agentHealed" size="small" type="warning" effect="plain" class="healed-tag">
            自愈通过
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="触发方式">{{ triggerLabel }}</el-descriptions-item>
        <el-descriptions-item label="耗时">{{ formatDuration(execution?.durationMs) }}</el-descriptions-item>
        <el-descriptions-item label="浏览器">
          {{ browserText(execution?.browserName) }}
          <span v-if="execution?.browserVersion" class="version-text">（{{ execution.browserVersion }}）</span>
        </el-descriptions-item>
        <el-descriptions-item v-if="execution?.dataSetRowLabel" label="数据行">
          {{ execution.dataSetRowLabel }}
        </el-descriptions-item>
        <el-descriptions-item label="开始时间">{{ formatDateTime(execution?.startedAt) }}</el-descriptions-item>
        <el-descriptions-item label="结束时间">{{ formatDateTime(execution?.endedAt) }}</el-descriptions-item>
        <el-descriptions-item label="结果数" :span="1">{{ execution?.results?.length ?? 0 }}</el-descriptions-item>
        <el-descriptions-item label="创建时间">{{ formatDateTime(execution?.createdAt) }}</el-descriptions-item>
        <el-descriptions-item label="环境">{{ execution?.environmentName || '—' }}</el-descriptions-item>
        <!-- 重试可观测：>0 说明有步骤靠内部重试才稳住，通过率数字里含水分 -->
        <el-descriptions-item label="步骤重试" :span="1">
          <el-tooltip v-if="(execution?.stepRetryCount ?? 0) > 0"
            content="有步骤在首次执行失败后靠内部重试通过，结果稳定性弱于表面通过" placement="top">
            <el-tag v-if="(execution?.stepRetryCount ?? 0) > 0" size="small" type="warning">
              内部重试 {{ execution?.stepRetryCount }} 次
            </el-tag>
          </el-tooltip>
          <span v-else class="muted">未触发</span>
        </el-descriptions-item>
        <el-descriptions-item label="执行回放" :span="1">
          <template v-if="execution?.traceUrl">
            <el-link type="primary" @click="handleTraceDownload">
              下载 trace（{{ formatSize(execution.traceSizeBytes) }}）
            </el-link>
            <div class="trace-hint">
              用 <code>npx playwright show-trace &lt;文件&gt;</code> 回放每步 DOM 与网络
            </div>
          </template>
          <span v-else class="muted">仅在失败时保留</span>
        </el-descriptions-item>
        <!-- 录像与 trace 的分工：trace 适合查元素与网络，而"动画未完就点了""焦点跳走了"
             这类时序问题，录像一眼就明白。播放器放下方单独一块（描述列表里太窄） -->
        <el-descriptions-item label="执行录像" :span="1">
          <template v-if="execution?.videoUrl">
            <el-link type="primary" @click="loadVideo">
              {{ loadingVideo ? '加载中…' : `播放录像（${formatSize(execution.videoSizeBytes)}）` }}
            </el-link>
            <div v-if="videoObjectUrl" class="trace-hint">已加载到下方播放器</div>
          </template>
          <span v-else class="muted">仅在失败时保留</span>
        </el-descriptions-item>
        <!-- 执行编排：把「它在等谁」「为什么没跑」写进详情，跳过的执行才有交代 -->
        <el-descriptions-item v-if="execution?.dependsOnTestCaseId" label="前置用例" :span="1">
          <span>已设置（同一套件内的另一条用例）</span>
          <div class="trace-hint">前置通过后本条才会开跑；前置未通过则本条直接跳过</div>
        </el-descriptions-item>
      </el-descriptions>
    </el-card>

    <!-- 录像播放器：点「播放录像」才加载（几 MB 不该在打开页面时就拉） -->
    <el-card v-if="videoObjectUrl" class="video-card">
      <div class="section-title">执行录像</div>
      <video :src="videoObjectUrl" controls class="exec-video" />
    </el-card>

    <!-- 编排跳过：只写「跳过」等于没解释，这里直接给出原因 -->
    <el-alert v-if="execution?.skipReason" type="warning" :closable="false" class="orchestration-alert">
      <template #title>
        本条执行被编排跳过：{{ execution.skipReason }}
      </template>
      <div class="orchestration-hint">
        前置用例未通过、或所属套件设为「失败快停」时，剩余用例会被跳过——这样比照常执行更能省下无效的机器时间。
      </div>
    </el-alert>

    <el-card v-if="showDiagnosis" class="diagnosis-card">
      <template #header>
        <div class="diagnosis-header">
          <span>AI 诊断</span>
          <el-button v-if="execution?.testCaseId" size="small" type="primary" :loading="rerunning"
            @click="handleRerun">
            重新执行
          </el-button>
        </div>
      </template>
      <div class="diagnosis-root-cause">{{ execution?.aiDiagnosis }}</div>
      <div v-if="confidencePercent !== null" class="diagnosis-confidence">
        <span class="diagnosis-section-label">置信度</span>
        <el-progress :percentage="confidencePercent" class="diagnosis-progress" />
      </div>
      <div v-if="execution?.aiSuggestedFix" class="diagnosis-fix">
        <div class="diagnosis-section-label">修复建议</div>
        <div class="diagnosis-fix-text">{{ execution.aiSuggestedFix }}</div>
      </div>
    </el-card>

    <!-- M8 Agent 修复轨迹：失败后自动归因 → 修复 → 重跑的尝试记录（仅修改执行副本） -->
    <el-card v-if="agentAttempts.length > 0" class="agent-card">
      <template #header>
        <div class="agent-header">
          <span>Agent 修复轨迹</span>
          <span class="agent-hint">失败后由 Agent 自动归因并修复重跑的尝试记录；只修改执行副本，不改动用例本身</span>
        </div>
      </template>
      <el-timeline>
        <el-timeline-item
          v-for="a in agentAttempts"
          :key="a.id"
          :timestamp="formatDateTime(a.createdAt)"
          :type="agentAttemptResultTagType(a.result)"
        >
          <div class="agent-attempt">
            <div class="agent-attempt-head">
              <span class="agent-attempt-title">
                第 {{ a.attemptNumber }} 次尝试 · {{ FIX_CATEGORY_LABELS[a.fixCategory] ?? '未知' }}
              </span>
              <el-tag size="small" :type="agentAttemptResultTagType(a.result)">
                {{ AGENT_ATTEMPT_RESULT_LABELS[a.result] ?? '未知' }}
              </el-tag>
              <el-tag v-if="a.needsApproval" size="small" type="warning" effect="plain">需人工审批</el-tag>
            </div>
            <div class="agent-attempt-line">
              目标步骤 {{ a.targetStepOrder }} · 置信度 {{ Math.round((a.confidence ?? 0) * 100) }}%
              · 已应用修复 {{ a.appliedSuccessfully ? '是' : '否' }}
              <span v-if="a.llmInputTokens + a.llmOutputTokens > 0">
                · Tokens {{ a.llmInputTokens }}+{{ a.llmOutputTokens }}
              </span>
            </div>
            <div v-if="a.fixSummary" class="agent-attempt-line">修复建议：{{ a.fixSummary }}</div>
            <div v-if="a.failureAfterFix" class="agent-attempt-line agent-attempt-fail">
              修复后仍失败：{{ a.failureAfterFix }}
            </div>
          </div>
        </el-timeline-item>
      </el-timeline>
    </el-card>

    <el-card v-if="visualResults.length > 0" class="visual-card">
      <template #header>
        <div class="visual-header">
          <span>视觉回归</span>
          <span class="visual-hint">与基线的像素差异；确认无误后可「接受变化」把本次截图设为新基线</span>
        </div>
      </template>
      <div v-for="row in visualResults" :key="row.id" class="visual-item">
        <div class="visual-item-head">
          <span class="visual-step">步骤 {{ row.stepOrder + 1 }}</span>
          <el-tag size="small" :type="visualTagType(row.visualStatus)">
            {{ VISUAL_STATUS_LABELS[row.visualStatus ?? 3] }}
          </el-tag>
          <span v-if="row.visualDiffRatio != null" class="visual-ratio">
            差异 {{ (row.visualDiffRatio * 100).toFixed(2) }}%
          </span>
          <el-button v-if="row.visualStatus === VisualStatus.Changed" link type="primary" class="accept-btn"
            :loading="acceptingId === row.id" @click="handleAccept(row)">
            接受变化（更新基线）
          </el-button>
        </div>
        <div class="visual-compare">
          <figure>
            <el-image v-if="row.baselineImageUrl" :src="row.baselineImageUrl" fit="contain" class="compare-img"
              :preview-src-list="comparePreview(row)" preview-teleported />
            <div v-else class="compare-empty">无基线</div>
            <figcaption>基线</figcaption>
          </figure>
          <figure>
            <el-image v-if="row.screenshotUrl" :src="row.screenshotUrl" fit="contain" class="compare-img"
              :preview-src-list="comparePreview(row)" preview-teleported />
            <div v-else class="compare-empty">无截图</div>
            <figcaption>本次实际</figcaption>
          </figure>
          <figure>
            <el-image v-if="row.diffImageUrl" :src="row.diffImageUrl" fit="contain" class="compare-img"
              :preview-src-list="comparePreview(row)" preview-teleported />
            <div v-else class="compare-empty">无差异图</div>
            <figcaption>差异高亮</figcaption>
          </figure>
        </div>
        <div v-if="row.visualNote" class="visual-note">{{ row.visualNote }}</div>
      </div>
    </el-card>

    <el-card class="results-card">
      <template #header>
        <div class="results-header">
          <span>步骤结果</span>
          <el-tag v-if="isRunning" type="primary" size="small" effect="plain" class="live-tag">
            <span class="live-dot" />{{ liveText }}
          </el-tag>
        </div>
      </template>
      <!-- 关联缺陷：已有记录的失败可以认领，避免重复提单 -->
      <el-alert v-if="linkedDefects.length > 0" type="warning" :closable="false" class="linked-defects">
        <template #title>
          本次执行已关联 {{ linkedDefects.length }} 个缺陷：
          <el-link v-for="d in linkedDefects" :key="d.id" type="primary" class="defect-link"
            @click="goDefect()">{{ d.title }}</el-link>
        </template>
      </el-alert>
      <el-table :data="results" size="small">
        <el-table-column label="序号" width="70">
          <template #default="{ row }">{{ row.stepOrder < 0 ? '前置' : row.stepOrder + 1 }}</template>
        </el-table-column>
        <el-table-column label="步骤摘要" min-width="240">
          <template #default="{ row }">{{ configSummary(row) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="statusTagTypeFor(row.status)" size="small">{{ statusLabels[row.status] ?? '未知' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="耗时" width="90">
          <template #default="{ row }">{{ formatDuration(row.durationMs) }}</template>
        </el-table-column>
        <el-table-column label="截图" width="110">
          <template #default="{ row }">
            <el-image v-if="row.screenshotUrl" :src="row.screenshotUrl" :preview-src-list="[row.screenshotUrl]"
              fit="cover" class="screenshot-thumb" preview-teleported />
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column label="视觉" width="120">
          <template #default="{ row }">
            <el-tag v-if="row.visualStatus !== undefined && row.visualStatus !== 3" size="small"
              :type="visualTagType(row.visualStatus)">
              {{ VISUAL_STATUS_LABELS[row.visualStatus] }}
            </el-tag>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column label="错误信息 / 日志" min-width="220">
          <template #default="{ row }">
            <span v-if="row.errorMessage" class="error-text">{{ row.errorMessage }}</span>
            <span v-else-if="row.log" class="log-text">{{ row.log }}</span>
            <span v-else>—</span>
          </template>
        </el-table-column>
        <el-table-column v-if="authStore.can(Permission.ManageTestCases)" label="缺陷" width="110">
          <template #default="{ row }">
            <!-- 已经转过缺陷的步骤不再给「转缺陷」入口，避免同一问题重复开单 -->
            <el-tooltip v-if="stepDefects.get(row.stepOrder)" placement="top"
              :content="`已转缺陷：${stepDefects.get(row.stepOrder)?.defectTitle}`">
              <el-link type="success" size="small" @click="goDefect()">已转缺陷</el-link>
            </el-tooltip>
            <el-link v-else-if="row.status === ExecutionStatus.Failed || row.status === ExecutionStatus.Error"
              type="primary" size="small" @click="openDefectDialog(row)">转缺陷</el-link>
            <span v-else>—</span>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!loading && results.length === 0"
        :description="isRunning ? '步骤执行中，完成一条显示一条…' : '暂无结果'" :image-size="60" />
    </el-card>

    <!-- 转缺陷 / 认领已有缺陷：同一问题重复出现时认领而不是再提一单 -->
    <el-dialog v-model="defectDialogVisible" title="转缺陷" width="640px">
      <el-radio-group v-model="defectMode" class="defect-mode">
        <el-radio-button value="create">创建新缺陷</el-radio-button>
        <el-radio-button value="link">关联已有缺陷</el-radio-button>
      </el-radio-group>

      <template v-if="defectMode === 'create'">
        <el-form label-width="90px">
          <el-form-item label="标题" required>
            <el-input v-model="defectForm.title" maxlength="200" show-word-limit />
          </el-form-item>
          <el-form-item label="严重度" required>
            <el-select v-model="defectForm.severity" style="width: 200px">
              <el-option v-for="(label, value) in DEFECT_SEVERITY_LABELS" :key="value" :label="label"
                :value="Number(value)" />
            </el-select>
          </el-form-item>
          <el-form-item label="修复负责人">
            <el-select v-model="defectForm.assignedToId" filterable clearable style="width: 100%">
              <el-option v-for="u in userOptions" :key="u.id" :label="u.name" :value="u.id" />
            </el-select>
          </el-form-item>
          <el-form-item label="描述">
            <el-input v-model="defectForm.description" type="textarea" :rows="5"
              placeholder="保存后自动附加错误信息、AI 诊断、截图与 trace 等证据" />
          </el-form-item>
        </el-form>
      </template>
      <template v-else>
        <div class="dialog-hint">选择本项目已有缺陷进行认领：记录本次复现，并把这个失败步骤与缺陷关联。</div>
        <el-select v-model="linkDefectId" filterable style="width: 100%" placeholder="选择缺陷">
          <el-option v-for="d in openDefects" :key="d.id"
            :label="`${DEFECT_SEVERITY_LABELS[d.severity] ?? d.severity} · ${d.title}`" :value="d.id" />
        </el-select>
      </template>

      <template #footer>
        <el-button @click="defectDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="defectSaving" @click="submitDefectDialog">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import PageHeaderBar from '@/components/common/PageHeaderBar.vue'
import { Download, Refresh, VideoPause, Loading, View } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { acceptVisualChange } from '@/api/visual'
import {
  cancelExecution, createExecution, downloadExecutionTrace, downloadExecutionVideo,
  getAgentAttempts, getExecution, getExecutionDefectLinks,
} from '@/api/execution'
import { downloadExecutionReport, saveBlobAsFile } from '@/api/report'
import {
  addDefectOccurrence, createDefect, getDefects, linkDefectCase,
} from '@/api/defect'
import { getTestCase } from '@/api/testcase'
import { listUserOptionsApi } from '@/api/auth'
import { useAuthStore } from '@/stores/auth'
import { Permission } from '@/constants/permissions'
import { useSignalR } from '@/composables/useSignalR'
import { formatDateTime, formatDuration } from '@/utils/formatter'
import {
  AGENT_ATTEMPT_RESULT_LABELS, ExecutionStatus, FIX_CATEGORY_LABELS, TriggerType,
  agentAttemptResultTagType, type AgentAttempt, type ExecutionDetail, type ExecutionResultItem,
} from '@/types/execution'
import { ACTION_TYPE_LABELS, type StepConfig } from '@/types/testcase'
import { VisualStatus, VISUAL_STATUS_LABELS } from '@/types/visual'
import { DEFECT_SEVERITY_LABELS, type DefectListItem, type ExecutionDefectLink } from '@/types/defect'
import type { UserOption } from '@/types/auth'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
// 响应式取路由参数：MainLayout 的 <router-view> 无 key，/executions/A → /executions/B
// 会复用本组件实例（setup 不重跑），「重新执行」提交后跳转就靠这里的 watch 重载
const executionId = computed(() => route.params.id as string)

const execution = ref<ExecutionDetail | null>(null)
const results = ref<ExecutionResultItem[]>([])
/** M8 Agent 修复轨迹（空数组 = 无自愈记录，模板不渲染该区） */
const agentAttempts = ref<AgentAttempt[]>([])
const loading = ref(false)
const rerunning = ref(false)
let timer: number | undefined
let diagnosisTimer: number | undefined
let diagnosisReloadScheduled = false

const { connection, start, stop } = useSignalR('/hubs/execution')

const acceptingId = ref('')

const visualResults = computed(() =>
  results.value.filter((item) => item.visualStatus !== undefined && item.visualStatus !== VisualStatus.Skipped))

const visualTagType = (status?: number) =>
  ({ 0: 'info', 1: 'success', 2: 'danger' }[status ?? 3] ?? 'info') as 'info' | 'success' | 'danger'

const browserText = (name?: string | null) =>
  ({ chromium: 'Chromium', firefox: 'Firefox', webkit: 'WebKit' }[name ?? ''] ?? name ?? '—')

/** trace 是受权端点（S1），带 JWT 走 blob 下载；文件名后端 Content-Disposition 给出 */
const handleTraceDownload = async () => {
  const blob = await downloadExecutionTrace(executionId.value)
  saveBlobAsFile(blob, `trace-${executionId.value}.zip`)
}

// ------------------------------ 执行录像

const videoObjectUrl = ref<string | null>(null)
const loadingVideo = ref(false)

/**
 * 按需加载录像。
 *
 * 为什么不直接 `<video :src="execution.videoUrl">`：那是受权端点，`<video>` 带不上
 * Authorization 头，会直接 404。而把 token 拼进 URL 又会进浏览器历史与服务端日志。
 * 所以走 axios 拿 blob 再造 objectURL。
 */
const loadVideo = async () => {
  if (!execution.value?.videoUrl || loadingVideo.value) return
  loadingVideo.value = true
  try {
    const blob = await downloadExecutionVideo(executionId.value)
    releaseVideo()
    videoObjectUrl.value = URL.createObjectURL(blob)
  } finally {
    loadingVideo.value = false
  }
}

/** objectURL 不释放会一直占着那几 MB 内存，直到页面关闭 */
const releaseVideo = () => {
  if (videoObjectUrl.value) {
    URL.revokeObjectURL(videoObjectUrl.value)
    videoObjectUrl.value = null
  }
}

// ------------------------------ 缺陷关联（转缺陷 / 认领）

const linkedDefects = ref<DefectListItem[]>([])
const defectDialogVisible = ref(false)
const defectSaving = ref(false)
const defectMode = ref<'create' | 'link'>('create')
const defectTargetRow = ref<ExecutionResultItem | null>(null)
const defectForm = ref<{ title: string; severity: number; assignedToId?: string; description: string }>({
  title: '',
  severity: 1,
  description: '',
})
const openDefects = ref<DefectListItem[]>([])
const linkDefectId = ref<string | undefined>(undefined)
const userOptions = ref<UserOption[]>([])
/** 步骤号 → 该步骤已关联的缺陷（「缺陷」列据此禁用重复转单） */
const stepDefects = ref<Map<number, ExecutionDefectLink>>(new Map())
let testCaseProjectId: string | null = null

const loadDefectState = async () => {
  if (!authStore.can(Permission.ViewTestCases)) return
  // 两个请求互不依赖，一起发；任一失败都降级成空（缺陷列只是辅助信息，不该阻断页面）
  const [defects, links] = await Promise.all([
    getDefects({ executionId: executionId.value, pageSize: 50 }).catch(() => null),
    getExecutionDefectLinks(executionId.value).catch(() => null),
  ])
  linkedDefects.value = defects?.items ?? []
  stepDefects.value = new Map((links ?? []).map((l) => [l.stepOrder, l]))
}

const stepLabel = (row: ExecutionResultItem) => (row.stepOrder < 0 ? '前置' : `步骤 ${row.stepOrder + 1}`)

const openDefectDialog = async (row: ExecutionResultItem) => {
  defectTargetRow.value = row
  defectMode.value = 'create'
  linkDefectId.value = undefined
  defectForm.value = {
    title: `${execution.value?.testCaseName ?? '用例'} ${stepLabel(row)}失败`,
    severity: 1,
    assignedToId: undefined,
    description: '',
  }
  defectDialogVisible.value = true

  // 并行准备：用例的项目 ID（关联已有缺陷需按项目拉列表）、负责人选项、本项目未闭环缺陷
  if (execution.value?.testCaseId && testCaseProjectId === null) {
    try {
      const testCase = await getTestCase(execution.value.testCaseId)
      testCaseProjectId = testCase.projectId
    } catch {
      testCaseProjectId = null
    }
  }
  if (userOptions.value.length === 0) {
    try {
      userOptions.value = await listUserOptionsApi()
    } catch {
      userOptions.value = []
    }
  }
  if (testCaseProjectId) {
    try {
      const result = await getDefects({ projectId: testCaseProjectId, pageSize: 50 })
      openDefects.value = result.items
    } catch {
      openDefects.value = []
    }
  }
}

const submitDefectDialog = async () => {
  const row = defectTargetRow.value
  if (!row) return
  defectSaving.value = true
  try {
    if (defectMode.value === 'create') {
      if (!testCaseProjectId) {
        ElMessage.warning('无法确定用例所属项目')
        return
      }
      if (!defectForm.value.title.trim()) {
        ElMessage.warning('请填写标题')
        return
      }
      await createDefect({
        projectId: testCaseProjectId,
        title: defectForm.value.title,
        description: defectForm.value.description || null,
        severity: defectForm.value.severity,
        assignedToId: defectForm.value.assignedToId || null,
        foundInExecutionId: executionId.value,
        foundInStepOrder: row.stepOrder,
      })
      ElMessage.success('缺陷已创建，证据已自动附加')
    } else {
      if (!linkDefectId.value) {
        ElMessage.warning('请选择要关联的缺陷')
        return
      }
      await addDefectOccurrence(linkDefectId.value, executionId.value, row.stepOrder)
      if (execution.value?.testCaseId) {
        await linkDefectCase(linkDefectId.value, execution.value.testCaseId)
      }
      ElMessage.success('已认领到已有缺陷')
    }
    defectDialogVisible.value = false
    void loadDefectState()
  } finally {
    defectSaving.value = false
  }
}

const goDefect = () => {
  void router.push('/defects')
}

const formatSize = (bytes?: number | null) => {
  if (bytes == null) return '未知大小'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

const comparePreview = (row: ExecutionResultItem) =>
  [row.baselineImageUrl, row.screenshotUrl, row.diffImageUrl].filter((url): url is string => !!url)

/** 接受变化：把本次截图设为新基线，后续执行以它为准 */
const handleAccept = async (row: ExecutionResultItem) => {
  acceptingId.value = row.id
  try {
    await acceptVisualChange(row.id)
    ElMessage.success('已更新视觉基线')
    await load()
  } finally {
    acceptingId.value = ''
  }
}

const statusLabels: Record<number, string> = {
  [ExecutionStatus.Pending]: '等待中',
  [ExecutionStatus.Running]: '执行中',
  [ExecutionStatus.Passed]: '通过',
  [ExecutionStatus.Failed]: '失败',
  [ExecutionStatus.Error]: '错误',
  [ExecutionStatus.Skipped]: '跳过',
  [ExecutionStatus.Canceled]: '已终止',
}

const statusLabel = computed(() => (execution.value ? statusLabels[execution.value.status] ?? '未知' : '—'))
const statusTagType = computed(() => (execution.value ? ({ [ExecutionStatus.Passed]: 'success', [ExecutionStatus.Failed]: 'danger', [ExecutionStatus.Error]: 'danger', [ExecutionStatus.Running]: 'primary', [ExecutionStatus.Pending]: 'info', [ExecutionStatus.Skipped]: 'info', [ExecutionStatus.Canceled]: 'info' }[execution.value.status] ?? 'info') : 'info'))
const triggerLabel = computed(() => (execution.value ? ({ [TriggerType.Manual]: '手动', [TriggerType.Scheduled]: '定时', [TriggerType.CIWebhook]: 'CI', [TriggerType.AIRegression]: 'AI 回归' }[execution.value.triggerType] ?? '—') : '—'))

const statusTagTypeFor = (value: ExecutionStatus) =>
  ({ [ExecutionStatus.Passed]: 'success', [ExecutionStatus.Failed]: 'danger', [ExecutionStatus.Error]: 'danger', [ExecutionStatus.Running]: 'primary', [ExecutionStatus.Pending]: 'info', [ExecutionStatus.Skipped]: 'info', [ExecutionStatus.Canceled]: 'info' }[value] ?? 'info')

const showDiagnosis = computed(() => {
  const ex = execution.value
  return !!ex && [ExecutionStatus.Failed, ExecutionStatus.Error].includes(ex.status) && !!ex.aiDiagnosis
})

const confidencePercent = computed(() => {
  const confidence = execution.value?.diagnosisConfidence
  return confidence == null ? null : Math.round(confidence * 100)
})

/**
 * 步骤摘要：动作 · 定位方式（仅 AI 定位时显示）· 配置摘要。
 *
 * 动作取自结果行的 stepActionType（历史行为空，降级为「步骤 N」）；
 * AI 定位的选择器只有语义描述（selector.description）没有 value，
 * 这是此前 AI 步骤摘要显示「—」的根因——旧实现只读 selector.value。
 */
const configSummary = (row: ExecutionResultItem) => {
  const snapshot = row.stepSnapshot
  const stepLabel = row.stepOrder < 0 ? '前置' : `步骤 ${row.stepOrder + 1}`
  if (!snapshot) return `${stepLabel}（配置快照缺失）`

  const parts: string[] = []
  const action = row.stepActionType != null ? ACTION_TYPE_LABELS[row.stepActionType] : undefined
  if (action) parts.push(action)
  if (snapshot.selector?.type === 'ai') parts.push('AI定位')

  const config: string[] = []
  if (snapshot.url) config.push(ellipsis(snapshot.url, 60))
  if (snapshot.method || snapshot.endpoint) config.push([snapshot.method, snapshot.endpoint].filter(Boolean).join(' '))
  // AI 定位显示语义描述；css/xpath 显示选择器值
  if (snapshot.selector?.type === 'ai') config.push(ellipsis(snapshot.selector.description ?? '', 40))
  else if (snapshot.selector?.value) config.push(ellipsis(snapshot.selector.value, 40))
  if (snapshot.attribute) config.push(`@${snapshot.attribute}`)
  if (snapshot.value) config.push(`「${ellipsis(snapshot.value, 40)}」`)
  if (config.length > 0) parts.push(config.filter(Boolean).join(' '))

  return parts.join(' · ') || stepLabel
}

/** 摘要里超长文本的展示截断（提示内容仍在详情/日志里可看全） */
const ellipsis = (text: string, max: number) =>
  text.length > max ? `${text.slice(0, max)}…` : text

const reporting = ref(false)

/** 导出本次执行的测试报告（xlsx：概览 + 模块明细 + 缺陷报告 + 步骤截图） */
const handleDownloadReport = async () => {
  reporting.value = true
  try {
    const blob = await downloadExecutionReport(executionId.value)
    saveBlobAsFile(blob, `测试报告_${executionId.value.slice(0, 8)}.xlsx`)
    ElMessage.success('报告已生成')
  } finally {
    reporting.value = false
  }
}

const isRunning = computed(() =>
  execution.value
    ? [ExecutionStatus.Pending, ExecutionStatus.Running].includes(execution.value.status)
    : false)

/** 正在执行的步骤序号（0 基，-1 为自动登录前置）。来自 StepStarted 推送；页面刷新后由轮询推断。 */
const currentStepOrder = ref<number | null>(null)

/** 执行中卡片头部的实时进度文案：共 x 步，当前正执行第 x 步 */
const liveText = computed(() => {
  const total = execution.value?.totalSteps
  const current = currentStepOrder.value
  const parts: string[] = []
  if (total != null && total > 0) parts.push(`共${total}步`)
  if (current != null && current < 0) parts.push('正在执行前置登录')
  else if (current != null) parts.push(`当前正执行第${current + 1}步`)
  return parts.join('，') || '执行中'
})

/** 拉取本次执行的 Agent 修复轨迹（失败静默为空，不影响详情页其余内容） */
const loadAgentAttempts = async () => {
  try {
    agentAttempts.value = await getAgentAttempts(executionId.value)
  } catch {
    agentAttempts.value = []
  }
}

const load = async () => {
  if (loading.value) return
  loading.value = true
  const requestedId = executionId.value
  try {
    const detail = await getExecution(requestedId)
    // 参数已变化（如刚点了重新执行跳到新执行）：丢弃过期响应，由 watch 的 load 接管
    if (requestedId !== executionId.value) return
    execution.value = detail
    // 执行中只做合并（保留 StepStarted 瞬时行），终态才整体替换
    mergeResults(detail.results ?? [], !isRunning.value)
    // 追踪「当前正执行第几步」：优先用 StepStarted 推送值；刷新后无推送时，
    // 用已落库结果的最大序号 +1 推断（执行中的步骤尚未落库）
    if (isRunning.value) {
      if (currentStepOrder.value == null) {
        const orders = (detail.results ?? []).map((r) => r.stepOrder)
        currentStepOrder.value = orders.length > 0 ? Math.max(...orders) + 1 : 0
      }
    } else {
      currentStepOrder.value = null
      // 已落终态：终止流程结束，解除「正在终止」状态与按钮锁定
      canceling.value = false
    }
  } finally {
    loading.value = false
  }
  void loadDefectState()
  void loadAgentAttempts()
  scheduleDiagnosisReload()
}

/**
 * 终止执行：中断正在跑的步骤，未执行的步骤标记为跳过，整条执行落「已终止」终态。
 * 确认后进入 canceling 状态（头部提示 + 按钮锁定），直到执行真正落终态（load 里复位），
 * 期间不能重复点击；指令发送失败时立即解除锁定允许重试。
 */
const canceling = ref(false)
const handleCancel = async () => {
  if (!execution.value || canceling.value) return
  const confirmed = await ElMessageBox.confirm(
    '确定终止当前执行吗？正在执行的步骤将被中断，未执行的步骤将标记为跳过。',
    '终止执行',
    { type: 'warning', confirmButtonText: '终止', cancelButtonText: '取消' },
  ).catch(() => false)
  if (confirmed === false) return
  canceling.value = true
  try {
    await cancelExecution(executionId.value)
    ElMessage.success('终止指令已发送')
    await load()
  } catch {
    canceling.value = false
  }
}

const scheduleDiagnosisReload = () => {
  const ex = execution.value
  if (!ex || !ex.testCaseId) return
  if (![ExecutionStatus.Failed, ExecutionStatus.Error].includes(ex.status)) return
  if (ex.aiDiagnosis || diagnosisReloadScheduled) return
  diagnosisReloadScheduled = true
  diagnosisTimer = window.setTimeout(() => {
    diagnosisTimer = undefined
    load()
  }, 3000)
}

const handleRerun = async () => {
  const testCaseId = execution.value?.testCaseId
  if (!testCaseId || rerunning.value) return
  rerunning.value = true
  try {
    const created = await createExecution({
      testCaseId,
      environmentId: execution.value?.environmentId ?? undefined,
    })
    ElMessage.success('执行已提交')
    router.push(`/executions/${created.id}`)
  } finally {
    rerunning.value = false
  }
}

const ensurePolling = () => {
  window.clearInterval(timer)
  timer = window.setInterval(async () => {
    if (execution.value && [ExecutionStatus.Pending, ExecutionStatus.Running].includes(execution.value.status)) {
      await load()
    } else {
      window.clearInterval(timer)
    }
  }, 5000)
}

const sortByOrder = (list: ExecutionResultItem[]) =>
  [...list].sort((a, b) => a.stepOrder - b.stepOrder)

/** StepStarted 瞬时占位行 Id：此类行只存在于前端，完成推送/轮询结果到达后按 stepOrder 替换 */
const TRANSIENT_ID = ''

const upsertResult = (list: ExecutionResultItem[], item: ExecutionResultItem) => {
  // SignalR 推送的 DTO 在保存前 Id 可能为 Guid.Empty，以 stepOrder 匹配为主
  const index = list.findIndex((r) => r.id === item.id || r.stepOrder === item.stepOrder)
  if (index >= 0) {
    const next = [...list]
    next[index] = item
    return next
  }
  return sortByOrder([...list, item])
}

/**
 * StepStarted：先插一条「执行中」占位行，让当前正在跑的步骤立即可见。
 * 同 stepOrder 已有行（完成结果或已有占位）时不降级覆盖，防止事件乱序把真实结果冲掉。
 */
const upsertStartedPlaceholder = (
  list: ExecutionResultItem[], stepOrder: number, snapshot: StepConfig | null,
) => {
  if (list.some((r) => r.stepOrder === stepOrder)) return list
  const placeholder: ExecutionResultItem = {
    id: TRANSIENT_ID,
    stepOrder,
    status: ExecutionStatus.Running,
    durationMs: null,
    stepSnapshot: snapshot,
  }
  return sortByOrder([...list, placeholder])
}

/**
 * 合并轮询结果与本地实时行。
 * 后端每完成一步即落库，轮询拿到的结果是权威的；本地多出的只有 StepStarted 瞬时占位
 * （对应步骤尚未完成、库里还没有行）。旧逻辑执行中直接整体替换成服务端的空/部分列表，
 * 会把 SignalR 刚推上来的行每隔 5 秒清空一次——表现为「全部执行完才一起显示」。
 * 终态时服务端结果已完整，整体替换以清掉瞬时行。
 */
const mergeResults = (fetched: ExecutionResultItem[], terminal: boolean) => {
  const base = sortByOrder(fetched)
  if (terminal) {
    results.value = base
    return
  }
  const transient = results.value.filter(
    (r) => r.id === TRANSIENT_ID && !base.some((f) => f.stepOrder === r.stepOrder),
  )
  results.value = transient.length > 0 ? sortByOrder([...base, ...transient]) : base
}

onMounted(async () => {
  await load()
  ensurePolling()
  try {
    await start()
    connection.value?.on('StepCompleted', (result: ExecutionResultItem) => {
      results.value = upsertResult(results.value, result)
    })
    connection.value?.on('StepStarted', (payload: { stepOrder: number; stepSnapshot: StepConfig | null }) => {
      currentStepOrder.value = payload.stepOrder
      results.value = upsertStartedPlaceholder(results.value, payload.stepOrder, payload.stepSnapshot)
    })
    connection.value?.on('StatusChanged', (status: number) => {
      if (execution.value) execution.value.status = status
      if (![ExecutionStatus.Pending, ExecutionStatus.Running].includes(status)) {
        currentStepOrder.value = null
        window.clearInterval(timer)
        load()
      }
    })
    connection.value?.onreconnected(async () => {
      // 重连后服务器端组关系已失效，需重新加入才能继续收到事件
      try {
        await connection.value?.invoke('JoinExecutionGroup', executionId.value.replace(/-/g, ''))
      } catch {
        // 轮询兜底
      }
    })
    // 后端 hub 组 id 为 ToString("N")（无连字符），而 API 返回的 id 带连字符，需归一化后才能收到事件
    await connection.value?.invoke('JoinExecutionGroup', executionId.value.replace(/-/g, ''))
  } catch {
    // SignalR 不可用时轮询兜底
  }
})

// 路由参数变化（/executions/A → /executions/B，组件被复用）：重置状态并加载新执行，
// 重新加入推送组、重新挂轮询——「重新执行」提交后跳转即落到新执行的详情页
watch(executionId, async (newId, oldId) => {
  if (!newId || newId === oldId) return
  results.value = []
  linkedDefects.value = []
  stepDefects.value = new Map()
  currentStepOrder.value = null
  canceling.value = false
  // 换执行必须丢掉上一条的录像，否则播放器里放的还是上一条的内容
  releaseVideo()
  testCaseProjectId = null
  diagnosisReloadScheduled = false
  window.clearTimeout(diagnosisTimer)
  diagnosisTimer = undefined
  await load()
  ensurePolling()
  try {
    await connection.value?.invoke('JoinExecutionGroup', newId.replace(/-/g, ''))
  } catch {
    // 轮询兜底
  }
})

onUnmounted(() => {
  window.clearInterval(timer)
  window.clearTimeout(diagnosisTimer)
  releaseVideo()
  void stop()
})
</script>

<style scoped>
.page-header {
  margin-bottom: 16px;
}

.summary-card {
  margin-bottom: 16px;
}

.diagnosis-card {
  margin-bottom: 16px;
  margin-top:15px;
}

.agent-card {
  margin-bottom: 16px;
  margin-top: 15px;
}

.agent-header {
  display: flex;
  align-items: baseline;
  gap: 12px;
}

.agent-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.agent-attempt-head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.agent-attempt-title {
  font-weight: 500;
}

.agent-attempt-line {
  font-size: 13px;
  color: var(--el-text-color-regular);
  margin-top: 4px;
  line-height: 1.6;
}

.agent-attempt-fail {
  color: var(--el-color-danger);
}

.healed-tag {
  margin-left: 8px;
}

.diagnosis-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.diagnosis-root-cause {
  font-size: 14px;
  line-height: 1.6;
}

.diagnosis-confidence {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 12px;
}

.diagnosis-progress {
  flex: 1;
}

.diagnosis-fix {
  margin-top: 12px;
}

.diagnosis-section-label {
  font-size: 13px;
  color: #909399;
}

.diagnosis-fix-text {
  margin-top: 4px;
  font-size: 13px;
  line-height: 1.6;
  white-space: pre-line;
}

.visual-card {
  margin-bottom: 16px;
}

.visual-header {
  display: flex;
  align-items: baseline;
  gap: 12px;
}

.visual-hint {
  font-size: 12px;
  font-weight: 400;
  color: var(--el-text-color-secondary);
}

.visual-item {
  padding: 10px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.visual-item:last-child {
  border-bottom: none;
}

.visual-item-head {
  display: flex;
  align-items: center;
  gap: 10px;
}

.visual-step {
  font-weight: 500;
}

.visual-ratio {
  font-size: 12px;
  color: var(--el-color-danger);
}

.accept-btn {
  margin-left: auto;
}

.visual-compare {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
  margin-top: 8px;
}

.visual-compare figure {
  margin: 0;
}

.compare-img {
  width: 100%;
  height: 200px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
  background: var(--el-fill-color-lighter);
}

.compare-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 200px;
  border: 1px dashed var(--el-border-color);
  border-radius: 4px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.visual-compare figcaption {
  margin-top: 4px;
  text-align: center;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.visual-note {
  margin-top: 8px;
  padding: 8px 10px;
  background: var(--el-color-warning-light-9);
  border-radius: 4px;
  font-size: 12px;
  line-height: 1.7;
}

.version-text {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.screenshot-thumb {
  width: 72px;
  height: 44px;
  border-radius: 4px;
  cursor: zoom-in;
}

.error-text {
  color: #f56c6c;
  font-size: 12px;
  word-break: break-all;
}

.log-text {
  color: #606266;
  font-size: 12px;
  word-break: break-all;
}

.trace-hint {
  margin-top: 2px;
  color: #9aa2ae;
  font-size: 12px;
  line-height: 1.6;
}

.trace-hint code {
  font-family: "Consolas", "Menlo", monospace;
  background: #f6f8fa;
  padding: 0 3px;
  border-radius: 2px;
}

.muted {
  color: #9aa2ae;
}

.linked-defects {
  margin-bottom: 10px;
}

/* 终止进行中提示：标题里图标旋转 + 文案对齐 */
.canceling-alert {
  margin-bottom: 16px;
}

.canceling-text {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.results-header {
  display: flex;
  align-items: center;
  gap: 10px;
}

/* 执行中脉冲圆点：提示步骤结果正在逐条实时写入 */
.live-tag :deep(.el-tag__content),
.live-tag {
  display: inline-flex;
  align-items: center;
}

.live-dot {
  width: 6px;
  height: 6px;
  margin-right: 5px;
  border-radius: 50%;
  background: var(--el-color-primary);
  animation: live-pulse 1.2s ease-in-out infinite;
}

@keyframes live-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.25; }
}

/* 编排跳过提示：放在概览卡与步骤结果之间，位置固定（无论有没有步骤结果都能看到） */
.orchestration-alert {
  margin-bottom: 12px;
}

.orchestration-hint {
  margin-top: 2px;
  font-size: 12px;
  line-height: 1.6;
}

.defect-link {
  margin: 0 8px;
}

.defect-mode {
  margin-bottom: 14px;
}

.dialog-hint {
  font-size: 12px;
  color: #909399;
  margin-bottom: 8px;
}

/* 录像播放器：独占一块，宽度放开——描述列表里挤不下 */
.video-card {
  margin-top: 16px;
}

.exec-video {
  width: 100%;
  max-width: 720px;
  margin-top: 8px;
  border-radius: 4px;
  background: #000;
}
</style>
