/* MARKER-ZZ-9421 */<template>
  <div class="case-detail">
    <PageHeaderBar title="用例详情" :subtitle="testCase?.name ?? ''" back-to="/testcases" sticky>
      <template #actions>
        <el-button type="primary" :icon="VideoPlay" :loading="executing" @click="handleExecute">
          执行
        </el-button>
        <el-button :icon="Clock" @click="versionVisible = true">版本历史</el-button>
        <el-button :icon="Edit" @click="router.push(`/testcases/${caseId}/edit`)">编辑步骤</el-button>
      </template>
    </PageHeaderBar>

    <el-card v-loading="loading" class="info-card">
      <el-descriptions :column="3" border>
        <el-descriptions-item label="名称">{{ testCase?.name }}</el-descriptions-item>
        <el-descriptions-item label="所属项目">{{ testCase?.projectName || '—' }}</el-descriptions-item>
        <el-descriptions-item label="类型">
          <el-tag>{{ typeLabel }}</el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="状态">
          <el-tag :type="statusTagType">{{ statusLabel }}</el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="浏览器">{{ testCase?.browser || '—' }}</el-descriptions-item>
        <el-descriptions-item v-if="testCase?.type === TestType.Api" label="BaseUrl">
          {{ testCase?.baseUrl || '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="超时">{{ formatDuration(testCase?.timeout) }}</el-descriptions-item>
        <el-descriptions-item label="版本">v{{ testCase?.version ?? 1 }}</el-descriptions-item>
        <el-descriptions-item label="失败策略">
          <el-tag v-if="testCase?.failFast" type="warning" size="small">失败即中止</el-tag>
          <span v-else>继续执行</span>
        </el-descriptions-item>
        <el-descriptions-item label="描述" :span="3">{{ testCase?.description || '—' }}</el-descriptions-item>
      </el-descriptions>
    </el-card>

    <el-card class="steps-card">
      <template #header>
        <span>测试步骤（{{ steps.length }}）</span>
      </template>
      <el-table :data="steps" size="small">
        <el-table-column label="序号" width="70">
          <template #default="{ $index }">{{ $index + 1 }}</template>
        </el-table-column>
        <el-table-column label="动作" width="120">
          <template #default="{ row }">
            <el-tag size="small">{{ actionLabel(row.actionType) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="配置摘要" min-width="300">
          <template #default="{ row }">{{ configSummary(row) }}</template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card class="history-card">
      <template #header>
        <div class="history-header">
          <span>执行历史</span>
          <el-button size="small" type="primary" @click="loadHistory">刷新</el-button>
        </div>
      </template>
      <el-table v-loading="historyLoading" :data="history" size="small">
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="statusTagTypeFor(row.status)">{{ statusLabelFor(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="耗时" width="100">
          <template #default="{ row }">{{ formatDuration(row.durationMs) }}</template>
        </el-table-column>
        <el-table-column label="开始时间" width="180">
          <template #default="{ row }">{{ formatDateTime(row.startedAt) }}</template>
        </el-table-column>
        <el-table-column label="操作">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="router.push(`/executions/${row.id}`)">详情</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!historyLoading && history.length === 0" description="暂无执行记录" :image-size="60" />
    </el-card>

    <el-dialog v-model="environmentDialogVisible" title="选择执行环境" width="520px">
      <div v-loading="environmentsLoading" class="environment-dialog-body">
        <el-select v-model="selectedEnvironmentId" class="environment-select" placeholder="选择环境">
          <el-option label="不使用环境" value="" />
          <el-option v-for="env in environments" :key="env.id" :label="env.name" :value="env.id">
            <div class="environment-option">
              <span class="environment-option-name">{{ env.name }}</span>
              <span class="environment-option-url">{{ env.baseUrl }}</span>
            </div>
          </el-option>
        </el-select>
      </div>
      <template #footer>
        <el-button @click="environmentDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="executing" @click="confirmExecute">确认执行</el-button>
      </template>
    </el-dialog>

    <!-- 版本历史：查看历史内容、与当前对比、回滚 -->
    <CaseVersionHistory v-model:visible="versionVisible" :test-case-id="caseId" :current="testCase" @restored="load" />

    <!-- 评审：提交 / 批准 / 驳回（方案 A 标记层） -->
    <el-card shadow="never" class="review-card">
      <template #header>
        <div class="review-head">
          <span>评审</span>
          <el-tag :type="reviewTagType" size="small">{{ reviewLabel }}</el-tag>
        </div>
      </template>

      <div v-if="testCase?.reviewStatus === 1" class="review-form">
        <el-input v-model="reviewNote" type="textarea" :rows="2" maxlength="500" placeholder="评审意见（驳回时必填）" />
        <div class="review-actions">
          <el-button type="success" size="small" :loading="reviewSaving" @click="handleReview('approve')">批准</el-button>
          <el-button type="danger" size="small" :loading="reviewSaving" @click="handleReview('reject')">驳回</el-button>
        </div>
      </div>

      <div v-if="testCase?.reviewStatus === 3 && testCase?.reviewNote" class="review-note">
        <span class="review-note-label">驳回意见：</span>{{ testCase.reviewNote }}
      </div>

            <div class="review-meta">
        <el-button v-if="testCase?.reviewStatus === 0 || testCase?.reviewStatus === 3"
          type="primary" size="small" :loading="reviewSaving" @click="handleSubmitReview">提交评审</el-button>
        <div v-if="testCase?.reviewedAt" class="review-result">
          <div class="review-result-line">
            <span class="review-result-label">评审结论</span>
            <span>{{ reviewLabel }}</span>
            <span class="review-result-sep">·</span>
            <span class="review-result-label">评审人</span>
            <span>{{ testCase?.reviewedByName || '—' }}</span>
            <span class="review-result-sep">·</span>
            <span>{{ formatDateTime(testCase?.reviewedAt ?? '') }}</span>
          </div>
          <div v-if="testCase?.reviewNote" class="review-result-line">
            <span class="review-result-label">评审意见</span>
            <span>{{ testCase?.reviewNote }}</span>
          </div>
        </div>
         <!-- <el-alert type="warning" :closable="false" class="boundary-tip" v-else-if="testCase?.reviewStatus === 0">
        <template #title>
         该用例尚未纳入评审流程
        </template>
      </el-alert> -->
        <div style="font-size: 14px; color:orange;padding-top:5px;" v-else-if="testCase?.reviewStatus === 0" class="info-hint">该用例尚未纳入评审流程</div>
      </div>
    </el-card>

    <!-- 评论：用例评审/讨论 -->
    <el-card shadow="never" class="comments-card">
      <template #header>
        <span>评论</span>
      </template>
      <CommentSection :target="0" :target-id="caseId" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import PageHeaderBar from '@/components/common/PageHeaderBar.vue'
import CommentSection from '@/components/common/CommentSection.vue'
import { reviewCase, submitReview } from '@/api/testcase'
import CaseVersionHistory from '@/components/testcase/CaseVersionHistory.vue'
import { ElMessage } from 'element-plus'
import { Clock, Edit, VideoPlay } from '@element-plus/icons-vue'
import { getTestCase } from '@/api/testcase'
import { createExecution, getExecutions } from '@/api/execution'
import { getEnvironments } from '@/api/environment'
import { formatDateTime, formatDuration } from '@/utils/formatter'
import { ActionType, TestType, TestCaseStatus, type TestCase, type TestStep } from '@/types/testcase'
import { ExecutionStatus, type ExecutionSummary } from '@/types/execution'
import type { EnvironmentView } from '@/types/environment'

const route = useRoute()
const router = useRouter()
const caseId = route.params.id as string

// ------------------------------ 评审（方案 A 标记层）
const REVIEW_LABELS: Record<number, string> = { 0: '未纳入评审', 1: '待评审', 2: '已通过', 3: '已驳回' }
const reviewLabel = computed(() => REVIEW_LABELS[testCase.value?.reviewStatus ?? 0] ?? '未纳入评审')
const reviewTagType = computed(() =>
  ({ 0: 'info', 1: 'warning', 2: 'success', 3: 'danger' }[testCase.value?.reviewStatus ?? 0] ?? 'info'))
const reviewNote = ref('')
const reviewSaving = ref(false)

const handleSubmitReview = async () => {
  reviewSaving.value = true
  try {
    const r = await submitReview(caseId)
    ElMessage.success(r.message)
    await load()
  } finally {
    reviewSaving.value = false
  }
}

const handleReview = async (action: 'approve' | 'reject') => {
  if (action === 'reject' && !reviewNote.value.trim()) {
    ElMessage.warning('驳回时必须填写评审意见')
    return
  }
  reviewSaving.value = true
  try {
    const r = await reviewCase(caseId, action, reviewNote.value.trim())
    ElMessage.success(r.message)
    reviewNote.value = ''
    await load()
  } finally {
    reviewSaving.value = false
  }
}

const testCase = ref<TestCase | null>(null)
const steps = ref<TestStep[]>([])
const versionVisible = ref(false)
const loading = ref(false)
const executing = ref(false)
const history = ref<ExecutionSummary[]>([])
const historyLoading = ref(false)
const environmentDialogVisible = ref(false)
const environmentsLoading = ref(false)
const environments = ref<EnvironmentView[]>([])
const selectedEnvironmentId = ref('')

const typeLabel = computed(() => ({ [TestType.Web]: 'Web', [TestType.Api]: 'API', [TestType.Mobile]: '移动端' }[testCase.value?.type ?? 0] ?? '未知'))
const statusLabel = computed(() => ({ [TestCaseStatus.Draft]: '草稿', [TestCaseStatus.Active]: '启用' }[testCase.value?.status ?? 0] ?? '未知'))
const statusTagType = computed(() => ({ [TestCaseStatus.Draft]: 'info', [TestCaseStatus.Active]: 'success' }[testCase.value?.status ?? 0] ?? 'info'))

const actionLabels: Record<number, string> = {
  [ActionType.Click]: '点击',
  [ActionType.Fill]: '输入',
  [ActionType.Navigate]: '打开',
  [ActionType.Wait]: '等待',
  [ActionType.Screenshot]: '截图',
  [ActionType.Scroll]: '滚动',
  [ActionType.Request]: '接口请求',
  [ActionType.AssertResponse]: '响应断言',
  [ActionType.ExtractVariable]: '变量提取',
  [ActionType.AIAction]: 'AI 动作',
  [ActionType.AIAssert]: 'AI 断言',
  [ActionType.AssertVisible]: '可见性断言',
  [ActionType.AssertText]: '文本断言',
}

const actionLabel = (type: ActionType) => actionLabels[type] ?? '未知'

const configSummary = (step: TestStep) => {
  const cfg = step.config ?? {}
  const parts: string[] = []
  if (cfg.url) parts.push(cfg.url)
  if (cfg.selector?.value) parts.push(`选择器: ${cfg.selector.value}`)
  if (cfg.value) parts.push(`值: ${cfg.value}`)
  return parts.join(' · ') || '—'
}

const statusLabelsFor: Record<number, string> = {
  [ExecutionStatus.Pending]: '等待中',
  [ExecutionStatus.Running]: '执行中',
  [ExecutionStatus.Passed]: '通过',
  [ExecutionStatus.Failed]: '失败',
  [ExecutionStatus.Error]: '错误',
  [ExecutionStatus.Skipped]: '跳过',
  [ExecutionStatus.Canceled]: '已终止',
}

const statusTagTypeFor = (status: ExecutionStatus) =>
  ({ [ExecutionStatus.Passed]: 'success', [ExecutionStatus.Failed]: 'danger', [ExecutionStatus.Error]: 'danger', [ExecutionStatus.Running]: 'primary', [ExecutionStatus.Pending]: 'info', [ExecutionStatus.Skipped]: 'info', [ExecutionStatus.Canceled]: 'info' }[status] ?? 'info')

const statusLabelFor = (status: ExecutionStatus) => statusLabelsFor[status] ?? '未知'

const load = async () => {
  loading.value = true
  try {
    testCase.value = await getTestCase(caseId)
    steps.value = testCase.value?.steps ?? []
  } finally {
    loading.value = false
  }
}

const loadHistory = async () => {
  historyLoading.value = true
  try {
    const res = await getExecutions({ testCaseId: caseId, page: 1, pageSize: 10 })
    history.value = res.items
  } finally {
    historyLoading.value = false
  }
}

const submitExecution = async (environmentId: string | null) => {
  executing.value = true
  try {
    const execution = await createExecution({ testCaseId: caseId, environmentId })
    ElMessage.success('执行已提交')
    router.push(`/executions/${execution.id}`)
  } finally {
    executing.value = false
  }
}

const handleExecute = async () => {
  const projectId = testCase.value?.projectId
  if (projectId) {
    environmentsLoading.value = true
    try {
      environments.value = await getEnvironments(projectId)
    } finally {
      environmentsLoading.value = false
    }
    if (environments.value.length > 0) {
      selectedEnvironmentId.value = ''
      environmentDialogVisible.value = true
      return
    }
  }
  await submitExecution(null)
}

const confirmExecute = async () => {
  environmentDialogVisible.value = false
  await submitExecution(selectedEnvironmentId.value || null)
}

onMounted(async () => {
  await load()
  await loadHistory()
})
</script>

<style scoped>
.page-header {
  margin-bottom: 16px;
}

.info-card {
  margin-bottom: 16px;
}

.steps-card {
  margin-bottom: 16px;
}

.history-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.environment-dialog-body {
  min-height: 40px;
}

.environment-select {
  width: 100%;
}

.environment-option {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.environment-option-url {
  color: #909399;
  font-size: 12px;
  font-family: monospace;
}

.comments-card {
  margin-top: 15px;
}

.review-card {
  margin-top: 15px;
}

.review-actions {
  padding-top: 10px;
}
.review-head { display: flex; align-items: center; justify-content: space-between; }
.review-result { display: flex; flex-direction: column; gap: 4px; font-size: 13px; }
.review-result-line { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.review-result-label { color: var(--el-text-color-secondary); }
.review-result-sep { color: var(--el-text-color-tertiary); }
</style>

/* rebuild-trigger */