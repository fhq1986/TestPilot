<template>
  <div class="ai-generate-page">
    <el-tabs v-model="activeTab">
      <el-tab-pane label="文本生成" name="text">
        <el-card>
          <template #header>
            <span>AI 智能用例生成</span>
          </template>

          <el-form :model="form" label-position="top" @submit.prevent>
            <el-form-item label="需求文档（可选）">
              <div class="doc-row">
                <el-upload ref="docUploadRef" :auto-upload="false" :limit="1"
                  accept=".txt,.md,.markdown,.docx"
                  :on-change="onDocChange" :on-remove="() => (docFile = null)">
                  <el-button :icon="Upload">选择文档</el-button>
                </el-upload>
                <el-button :icon="Document" :loading="extracting" :disabled="!docFile"
                  @click="handleExtractDoc">重新解析</el-button>
                <span v-if="docInfo" class="doc-info">{{ docInfo }}</span>
              </div>
              <div class="doc-hint">
                支持 {{ supportedText }}；选择文件后会自动解析并填入下方「需求描述」，可继续编辑后再生成。
              </div>
            </el-form-item>

            <el-form-item required>
              <template #label>
                <span class="req-label">
                  <span>需求描述</span>
                  <el-button v-if="form.requirement" link type="danger" size="small" :icon="Delete"
                    @click="handleClearRequirement">一键清除</el-button>
                </span>
              </template>
              <el-input v-model="form.requirement" type="textarea" :rows="6" clearable
                placeholder="请输入测试需求，例如：用户登录功能，支持用户名+密码登录，密码错误时提示，连续3次失败锁定1分钟"
                maxlength="20000" show-word-limit />
            </el-form-item>

            <el-form-item label="目标项目" required>
              <el-select v-model="form.projectId" placeholder="选择项目" filterable class="project-select">
                <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
              </el-select>
            </el-form-item>

            <el-form-item label="生成数量">
              <el-slider v-model="form.minCases" :min="3" :max="20" show-stops />
              <div class="slider-hint">当前：{{ form.minCases }} 条</div>
            </el-form-item>

            <el-form-item>
              <el-button type="primary" :icon="MagicStick" :loading="generating" :disabled="!canGenerate"
                @click="handleGenerate">
                生成测试用例
              </el-button>
              <span class="gen-hint">模型：deepseek-v4-flash（内网 AI Worker）</span>
            </el-form-item>
          </el-form>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="Swagger 导入" name="swagger">
        <el-card>
          <template #header>
            <span>Swagger / OpenAPI 导入</span>
          </template>

          <el-form :model="swaggerForm" label-position="top" @submit.prevent>
            <el-form-item label="目标项目" required>
              <el-select v-model="swaggerForm.projectId" placeholder="选择项目" filterable class="project-select">
                <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
              </el-select>
            </el-form-item>

            <el-form-item label="Swagger JSON（粘贴）">
              <el-input v-model="swaggerForm.content" type="textarea" :rows="8"
                placeholder="粘贴 OpenAPI 3.x / Swagger 2.0 JSON 文档（与 URL 二选一）" />
            </el-form-item>

            <el-form-item label="Swagger URL（可选）">
              <el-input v-model="swaggerForm.url" placeholder="例如 http://127.0.0.1:8000/swagger/v1/swagger.json" />
            </el-form-item>

            <el-form-item>
              <el-button type="primary" :icon="Upload" :loading="importing" :disabled="!canImport"
                @click="handleImport">
                导入并生成用例
              </el-button>
              <span class="gen-hint">确定性边界值 + 非法输入用例自动生成（每端点上限 8 条）</span>
            </el-form-item>
          </el-form>
        </el-card>
      </el-tab-pane>
    </el-tabs>

    <el-card v-if="generatedCases.length > 0" class="result-card">
      <template #header>
        <div class="result-header">
          <span>生成结果（{{ generatedCases.length }} 条）</span>
          <div class="result-actions">
            <el-button v-if="endpointSummaries.length > 0" type="primary" size="small" plain
              :loading="flowLoading" @click="handleAnalyzeFlow">
              AI 业务流
            </el-button>
            <el-button type="success" size="small" :loading="adopting" @click="handleBatchAdopt">
              批量采纳
            </el-button>
          </div>
        </div>
      </template>

      <el-table :data="generatedCases" row-key="localId" @selection-change="selectedRows = $event">
        <el-table-column type="selection" width="48" />
        <el-table-column label="用例名称" min-width="220">
          <template #default="{ row }">
            <el-input v-model="row.name" size="small" />
          </template>
        </el-table-column>
        <el-table-column label="优先级" width="90">
          <template #default="{ row }">
            <el-tag :type="priorityType(row.priority)">{{ row.priority }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="步骤数" width="80">
          <template #default="{ row }">{{ row.steps.length }}</template>
        </el-table-column>
        <el-table-column label="操作" width="160">
          <template #default="{ row }">
            <el-button link type="success" @click="adoptOne(row)">采纳</el-button>
            <el-button link type="danger" @click="rejectOne(row)">拒绝</el-button>
          </template>
        </el-table-column>
        <el-table-column type="expand">
          <template #default="{ row }">
            <div class="step-preview">
              <div v-for="(s, i) in row.steps" :key="i" class="step-line">
                <el-tag size="small" type="info">{{ actionLabel(s.actionType) }}</el-tag>
                <span class="step-desc">{{ s.aiElementDescription || configSummary(s.config) }}</span>
              </div>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Document, MagicStick, Upload } from '@element-plus/icons-vue'
import type { UploadFile, UploadInstance } from 'element-plus'
import { getProjects } from '@/api/project'
import { adoptCases, analyzeApiFlow, extractDocument, generateCases, importSwagger } from '@/api/ai'
import { ActionType } from '@/types/testcase'
import type { ApiEndpointSummary, GeneratedCase } from '@/types/ai'
import type { Project } from '@/types/project'

const router = useRouter()

interface EditableCase extends GeneratedCase {
  localId: number
}

// 需求文档解析
const docUploadRef = ref<UploadInstance>()
const docFile = ref<File | null>(null)
const extracting = ref(false)
const docInfo = ref('')
const supportedText = ref('.txt / .md / .docx')

/** 选择文件后自动解析，无需再点按钮 */
const onDocChange = (file: UploadFile) => {
  docFile.value = (file.raw as File) ?? null
  docInfo.value = ''
  if (docFile.value) void handleExtractDoc()
}

/** 一键清除需求描述：同时清空已选文档与解析状态，便于重新开始 */
const handleClearRequirement = () => {
  form.requirement = ''
  docFile.value = null
  docInfo.value = ''
  docUploadRef.value?.clearFiles()
  ElMessage.success('已清除需求描述')
}

/** 解析文档 → 填充需求描述（超出模型上限的部分截断并提示） */
const handleExtractDoc = async () => {
  if (!docFile.value) {
    ElMessage.warning('请先选择需求文档')
    return
  }
  extracting.value = true
  try {
    const doc = await extractDocument(docFile.value)
    const limit = 20000
    const truncated = doc.text.length > limit
    form.requirement = truncated ? doc.text.slice(0, limit) : doc.text
    docInfo.value = `已从「${doc.fileName}」提取 ${doc.charCount} 字` +
      (truncated ? `，已截取前 ${limit} 字用于生成` : '')
    for (const w of doc.warnings ?? []) ElMessage.warning(w)
    if (truncated) ElMessage.warning('文档较长，仅使用前 20000 字生成用例；建议精简需求后重试')
    ElMessage.success('文档解析完成，可点击「生成测试用例」')
  } finally {
    extracting.value = false
  }
}

const projectOptions = ref<Project[]>([])
const generatedCases = ref<EditableCase[]>([])
const selectedRows = ref<EditableCase[]>([])
const generating = ref(false)
const importing = ref(false)
const adopting = ref(false)
const flowLoading = ref(false)
const activeTab = ref('text')
const endpointSummaries = ref<ApiEndpointSummary[]>([])
const resultProjectId = ref('')
const resultSource = ref<'text' | 'swagger'>('text')
let localIdSeed = 1

const form = reactive({
  requirement: '',
  projectId: '',
  minCases: 5,
})

const swaggerForm = reactive({
  projectId: '',
  content: '',
  url: '',
})

const canGenerate = computed(() => form.requirement.trim().length >= 10 && !!form.projectId)

const canImport = computed(
  () => !!swaggerForm.projectId && (swaggerForm.content.trim().length > 0 || swaggerForm.url.trim().length > 0),
)

const actionLabels: Record<number, string> = {
  [ActionType.Click]: '点击', [ActionType.Fill]: '输入', [ActionType.Navigate]: '打开',
  [ActionType.Wait]: '等待', [ActionType.Screenshot]: '截图', [ActionType.Scroll]: '滚动',
  [ActionType.AssertVisible]: '可见性断言', [ActionType.AssertText]: '文本断言',
  [ActionType.Request]: '接口请求', [ActionType.AssertResponse]: '响应断言',
  [ActionType.ExtractVariable]: '变量提取',
}

const actionLabel = (type: number) => actionLabels[type] ?? '步骤'

const priorityType = (priority: string) =>
  ({ P0: 'danger', P1: 'warning', P2: 'info', P3: 'info' }[priority] ?? 'info')

const configSummary = (config?: {
  url?: string | null
  selector?: { value?: string | null; description?: string | null } | null
  method?: string | null
  endpoint?: string | null
  body?: string | null
  value?: string | null
}) => {
  if (!config) return '—'
  const parts: string[] = []
  if (config.url) parts.push(config.url)
  if (config.selector?.value) parts.push(`选择器: ${config.selector.value}`)
  else if (config.selector?.description) parts.push(`元素: ${config.selector.description}`)
  if (config.method) parts.push(`${config.method} ${config.endpoint ?? ''}`)
  else if (config.endpoint) parts.push(`路径: ${config.endpoint}`)
  if (config.value) parts.push(`值: ${config.value}`)
  if (config.body) parts.push(`体: ${config.body.length > 60 ? config.body.slice(0, 60) + '…' : config.body}`)
  return parts.join(' · ') || '—'
}

const handleGenerate = async () => {
  if (!canGenerate.value) {
    ElMessage.warning('请填写需求描述（至少10字）并选择项目')
    return
  }
  generating.value = true
  try {
    const cases = await generateCases({ requirement: form.requirement, minCases: form.minCases })
    generatedCases.value = cases.map((c) => ({ ...c, localId: localIdSeed++ }))
    endpointSummaries.value = []
    resultProjectId.value = form.projectId
    resultSource.value = 'text'
    ElMessage.success(`已生成 ${cases.length} 条用例`)
  } finally {
    generating.value = false
  }
}

const handleImport = async () => {
  if (!canImport.value) {
    ElMessage.warning('请选择项目并提供 Swagger JSON 内容或 URL')
    return
  }
  importing.value = true
  try {
    const res = await importSwagger({
      projectId: swaggerForm.projectId,
      content: swaggerForm.content.trim() || undefined,
      url: swaggerForm.url.trim() || undefined,
    })
    generatedCases.value = res.cases.map((c) => ({ ...c, localId: localIdSeed++ }))
    endpointSummaries.value = res.endpointSummaries ?? []
    resultProjectId.value = swaggerForm.projectId
    resultSource.value = 'swagger'
    ElMessage.success(`导入成功：${res.apiName}（${res.endpointCount} 端点），生成 ${res.generatedCases} 条用例`)
  } finally {
    importing.value = false
  }
}

const handleAnalyzeFlow = async () => {
  if (endpointSummaries.value.length === 0) {
    ElMessage.warning('暂无端点摘要，请先导入 Swagger')
    return
  }
  flowLoading.value = true
  try {
    const cases = await analyzeApiFlow({ endpoints: endpointSummaries.value })
    generatedCases.value = [
      ...generatedCases.value,
      ...cases.map((c) => ({ ...c, localId: localIdSeed++ })),
    ]
    ElMessage.success(`AI 业务流生成 ${cases.length} 条用例，已追加到结果列表`)
  } finally {
    flowLoading.value = false
  }
}

const adoptOne = async (row: EditableCase) => {
  try {
    await ElMessageBox.confirm('确认采纳该用例？', '提示', { type: 'info' })
  } catch {
    return
  }
  await doAdopt([row])
}

const handleBatchAdopt = async () => {
  const targets = selectedRows.value.length > 0 ? selectedRows.value : generatedCases.value
  if (targets.length === 0) return
  const label = selectedRows.value.length > 0 ? `选中的 ${targets.length} 条` : `全部 ${targets.length} 条`
  try {
    await ElMessageBox.confirm(`确认将${label}用例采纳到项目？`, '提示', { type: 'info' })
  } catch {
    return
  }
  await doAdopt(targets)
}

const doAdopt = async (cases: EditableCase[]) => {
  adopting.value = true
  try {
    const result = await adoptCases({
      projectId: resultProjectId.value,
      aiPrompt: resultSource.value === 'text' ? form.requirement : null,
      cases: cases.map((c) => ({
        name: c.name,
        priority: c.priority,
        type: c.type,
        steps: c.steps.map((s) => ({
          stepOrder: s.stepOrder,
          actionType: s.actionType,
          config: s.config,
          aiElementDescription: s.aiElementDescription ?? null,
        })),
      })),
    })
    generatedCases.value = generatedCases.value.filter((c) => !cases.some((x) => x.localId === c.localId))
    ElMessage.success(`已采纳 ${result.created} 条用例`)
    router.push({ path: '/testcases', query: { projectId: resultProjectId.value } })
  } finally {
    adopting.value = false
  }
}

const rejectOne = (row: EditableCase) => {
  generatedCases.value = generatedCases.value.filter((c) => c.localId !== row.localId)
}

onMounted(async () => {
  const res = await getProjects({ page: 1, pageSize: 100 })
  projectOptions.value = res.items
})
</script>

<style scoped>
.doc-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

.doc-row :deep(.el-upload) {
  display: flex;
  align-items: center;
}

.req-label {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.doc-info {
  line-height: 32px;
  font-size: 12px;
  color: var(--el-color-success);
}

.doc-hint {
  margin-top: 6px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.project-select {
  width: 320px;
}

.slider-hint {
  color: #909399;
  font-size: 13px;
}

.gen-hint {
  margin-left: 12px;
  color: #909399;
  font-size: 13px;
}

.result-card {
  margin-top: 16px;
}

.result-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.result-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.step-preview {
  padding: 8px 16px;
}

.step-line {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 0;
}

.step-desc {
  color: #606266;
  font-size: 13px;
}
</style>
