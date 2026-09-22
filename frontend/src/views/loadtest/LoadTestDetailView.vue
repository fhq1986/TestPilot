<template>
  <div class="loadtest-detail" v-loading="loading">
    <PageHeaderBar :title="detail?.name ?? '压测场景'"
      :subtitle="detail ? (LOAD_TEST_SOURCE_LABELS[detail.source] ?? '') : ''" back-to="/loadtests" sticky>
      <template #actions>
        <el-button :icon="Refresh" @click="loadAll">刷新</el-button>
        <el-button :icon="Check" type="primary" :loading="saving" :disabled="!detail" @click="handleSave">保存</el-button>
        <el-button :icon="VideoPlay" type="success" :loading="running" :disabled="!detail" @click="handleRun">
          运行
        </el-button>
      </template>
    </PageHeaderBar>

    <!-- 基本信息：名称/目标地址/环境都要能改，否则创建后填错就没法纠正 -->
    <el-card class="section-card">
      <template #header><span class="section-title">基本信息</span></template>
      <el-form label-width="100px">
        <div class="form-row">
          <el-form-item label="名称" class="form-name">
            <el-input v-model="form.name" maxlength="200" placeholder="场景名称" />
          </el-form-item>
          <el-form-item label="所属项目" class="form-fixed">
            <span class="readonly-text">{{ detail?.projectName || detail?.projectId || '—' }}</span>
          </el-form-item>
        </div>
        <div class="form-row">
          <el-form-item label="目标地址" class="form-name">
            <el-input v-model="form.targetBaseUrl" placeholder="https://api.example.com（留空则用环境地址）" />
          </el-form-item>
          <el-form-item label="执行环境" class="form-fixed">
            <el-select v-model="form.environmentId" placeholder="未指定" clearable>
              <el-option v-for="e in environmentOptions" :key="e.id" :label="e.name" :value="e.id" />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item label="说明">
          <el-input v-model="form.description" type="textarea" :rows="2" maxlength="1000" placeholder="场景用途（可选）" />
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 用例选择：来源决定配置方式（已有接口用例 / 导入 OpenAPI） -->
    <el-card class="section-card">
      <template #header>
        <div class="section-head">
          <span class="section-title">用例选择</span>
          <el-tag size="small" type="info">{{ selectedCaseCount }} 条</el-tag>
          <div class="section-head-spacer" />
          <el-button v-if="isOpenApi" size="small" :icon="Upload" @click="openImport">导入 OpenAPI</el-button>
        </div>
      </template>

      <template v-if="isOpenApi">
        <el-alert v-if="operationOptions.length === 0" type="info" :closable="false"
          title="尚未导入 OpenAPI 规范。点右上角「导入 OpenAPI」，粘贴规范后勾选要压测的接口。" />
        <div v-else>
          <div class="op-toolbar">
            <el-button size="small" link type="primary" @click="selectAllOperations">全选</el-button>
            <el-button size="small" link type="primary" @click="clearOperations">全不选</el-button>
            <span class="op-hint">已选 {{ selectedOperations.length }} / {{ operationOptions.length }} 个接口</span>
          </div>
          <el-checkbox-group v-model="selectedOperations" class="op-list">
            <el-checkbox v-for="op in operationOptions" :key="operationKey(op)" :value="operationKey(op)"
              class="op-item">
              <span class="op-method">{{ op.method.toUpperCase() }}</span>
              <span class="op-path">{{ op.path }}</span>
              <span v-if="op.label && op.label !== op.path" class="op-label">{{ op.label }}</span>
            </el-checkbox>
          </el-checkbox-group>
        </div>
      </template>

      <template v-else>
        <el-select v-model="caseIds" multiple filterable remote reserve-keyword :remote-method="searchCases"
          :loading="caseLoading" placeholder="搜索并选择接口用例（仅接口类型用例可压测）" class="full-width">
          <el-option v-for="c in caseOptionList" :key="c.id" :label="c.name" :value="c.id">
            <span>{{ c.name }}</span>
            <span class="option-sub">{{ c.module || '未分类' }}</span>
          </el-option>
        </el-select>
        <div class="form-hint">只列出接口类型（Api）的用例；压测脚本按用例的请求步骤生成。</div>
      </template>
    </el-card>

    <!-- 负载与阈值 -->
    <el-card class="section-card">
      <template #header><span class="section-title">负载与阈值</span></template>
      <el-form label-width="100px">
        <el-form-item label="执行器">
          <el-select v-model="profile.kind" class="executor-select">
            <el-option v-for="(label, value) in LOAD_TEST_EXECUTOR_LABELS" :key="value" :label="label"
              :value="value" />
          </el-select>
          <div class="form-hint">摘要：{{ formatLoadText(headline.vus, headline.duration) }}</div>
        </el-form-item>

        <!-- 梯度加压：靠 stages 描述爬升曲线，头部并发/时长由各段推导 -->
        <el-form-item v-if="profile.kind === 'ramping-vus'" label="加压曲线">
          <div class="stages-editor">
            <div v-for="(stage, index) in profile.stages" :key="index" class="stage-row">
              <span class="stage-index">{{ index + 1 }}</span>
              <el-input v-model="stage.duration" size="small" class="stage-duration" placeholder="30s" />
              <span class="stage-label">内升至</span>
              <el-input-number v-model="stage.target" size="small" :min="0" :max="100000" controls-position="right"
                class="stage-target" />
              <span class="stage-label">VU</span>
              <el-button link type="danger" :icon="Delete" @click="profile.stages.splice(index, 1)" />
            </div>
            <el-button size="small" :icon="Plus" @click="addStage">添加阶段</el-button>
            <div class="form-hint">时长用 k6 字面量（30s / 1m）；峰值 {{ headline.vus }} VU，总时长约 {{ headline.duration }}s</div>
          </div>
        </el-form-item>

        <!-- 恒定并发 -->
        <el-form-item v-else-if="profile.kind === 'constant-vus'" label="并发 VUs">
          <el-input-number v-model="profile.vus" :min="1" :max="100000" controls-position="right" />
          <span class="inline-label">持续时长（秒）</span>
          <el-input-number v-model="durationSeconds" :min="1" :max="86400" controls-position="right" />
        </el-form-item>

        <!-- 恒定到达率 -->
        <template v-else>
          <el-form-item label="到达率">
            <el-input-number v-model="profile.rate" :min="1" :max="100000" controls-position="right" />
            <span class="inline-label">每</span>
            <el-select v-model="profile.timeUnit" class="time-unit-select">
              <el-option label="秒" value="s" />
              <el-option label="分" value="m" />
            </el-select>
            <span class="inline-label">持续时长（秒）</span>
            <el-input-number v-model="durationSeconds" :min="1" :max="86400" controls-position="right" />
          </el-form-item>
          <el-form-item label="VU 池">
            <span class="inline-label">预分配</span>
            <el-input-number v-model="profile.preAllocatedVUs" :min="0" :max="100000" controls-position="right" />
            <span class="inline-label">上限</span>
            <el-input-number v-model="profile.maxVUs" :min="1" :max="100000" controls-position="right" />
          </el-form-item>
        </template>

        <el-form-item label="思考时间">
          <el-input-number v-model="profile.thinkTimeSeconds" :min="0" :max="600" :step="0.5"
            controls-position="right" />
          <span class="inline-label">秒 / 每次迭代之间</span>
        </el-form-item>

        <el-form-item label="阈值">
          <div class="thresholds-editor">
            <div v-for="(threshold, index) in thresholds" :key="index" class="threshold-row">
              <el-select v-model="threshold.metric" size="small" class="th-metric">
                <el-option v-for="(label, value) in THRESHOLD_METRIC_LABELS" :key="value" :label="label"
                  :value="value" />
              </el-select>
              <el-select v-model="threshold.aggregator" size="small" class="th-aggregator">
                <el-option v-for="opt in THRESHOLD_AGGREGATOR_OPTIONS" :key="opt.value" :label="opt.label"
                  :value="opt.value" />
              </el-select>
              <el-select v-model="threshold.operator" size="small" class="th-operator">
                <el-option v-for="opt in THRESHOLD_OPERATOR_OPTIONS" :key="opt.value" :label="opt.label"
                  :value="opt.value" />
              </el-select>
              <el-input-number v-model="threshold.value" size="small" :min="0" :step="1" controls-position="right"
                class="th-value" />
              <el-button link type="danger" :icon="Delete" @click="thresholds.splice(index, 1)" />
            </div>
            <el-button size="small" :icon="Plus" @click="thresholds.push(defaultThreshold())">添加阈值</el-button>
            <div class="form-hint">阈值不通过时本次运行判定为失败；如 p(95) &lt; 500 表示 95 分位耗时须小于 500ms。</div>
          </div>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 脚本 -->
    <el-card class="section-card">
      <template #header>
        <div class="section-head">
          <span class="section-title">脚本</span>
          <div class="section-head-spacer" />
          <el-button size="small" :icon="Refresh" :loading="generating" :disabled="!detail" @click="handleGenerate">
            {{ scriptText ? '重新生成' : '生成脚本' }}
          </el-button>
          <el-button size="small" :icon="CopyDocument" :disabled="!scriptText" @click="copyScript">复制</el-button>
          <el-button size="small" :icon="Download" :disabled="!scriptText" @click="downloadScript">下载</el-button>
        </div>
      </template>

      <!-- warnings 表示有步骤没被翻译进脚本：必须显式可见，否则用户会以为全量覆盖了 -->
      <el-alert v-if="warnings.length > 0" type="warning" :closable="false" class="script-warning"
        title="部分步骤未能翻译进脚本，以下内容不会被执行：">
        <ul class="warning-list">
          <li v-for="(w, i) in warnings" :key="i">{{ w }}</li>
        </ul>
      </el-alert>

      <pre v-if="scriptText" class="script-preview">{{ scriptText }}</pre>
      <el-empty v-else description="尚未生成脚本。生成与运行都基于已保存的配置，改动后请先保存。" :image-size="72" />

      <div v-if="scriptHash || scriptGeneratedAt" class="script-meta">
        <span v-if="scriptHash">指纹：{{ scriptHash.slice(0, 12) }}</span>
        <span v-if="scriptGeneratedAt">生成于 {{ formatFullDateTime(scriptGeneratedAt) }}</span>
      </div>
    </el-card>

    <!-- 运行历史 -->
    <el-card class="section-card">
      <template #header>
        <div class="section-head">
          <span class="section-title">运行历史</span>
          <div class="section-head-spacer" />
          <el-button size="small" :icon="Refresh" :loading="runsLoading" @click="loadRuns">刷新</el-button>
        </div>
      </template>

      <div class="table-wrap-x">
        <el-table :data="runs" size="small" row-key="id" empty-text="暂无运行记录">
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="executionStatusTagType(row.status)" size="small">
                {{ EXECUTION_STATUS_LABELS[row.status] }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="开始时间" width="190">
            <template #default="{ row }">{{ formatFullDateTime(row.startedAt) }}</template>
          </el-table-column>
          <el-table-column label="耗时" width="90">
            <template #default="{ row }">{{ formatDuration(row.durationMs) }}</template>
          </el-table-column>
          <el-table-column label="请求数" width="90">
            <template #default="{ row }">{{ row.totalRequests }}</template>
          </el-table-column>
          <el-table-column label="RPS" width="90">
            <template #default="{ row }">{{ row.rps ?? '—' }}</template>
          </el-table-column>
          <el-table-column label="p95" width="90">
            <template #default="{ row }">{{ row.p95Ms !== null && row.p95Ms !== undefined ? `${Math.round(row.p95Ms)}ms` : '未采集' }}</template>
          </el-table-column>
          <el-table-column label="错误率" width="100">
            <template #default="{ row }">{{ formatRate(row.errorRate) }}</template>
          </el-table-column>
          <el-table-column label="阈值" width="110">
            <template #default="{ row }">
              <el-tag v-if="row.thresholdTotal > 0" size="small"
                :type="row.thresholdFailed > 0 ? 'danger' : 'success'">
                {{ row.thresholdTotal - row.thresholdFailed }}/{{ row.thresholdTotal }}
              </el-tag>
              <span v-else class="muted">未设阈值</span>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="90" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click="router.push(`/loadtests/runs/${row.id}`)">查看</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </el-card>

    <!-- 导入 OpenAPI -->
    <el-dialog v-model="importVisible" title="导入 OpenAPI" width="760px" destroy-on-close top="6vh">
      <el-alert type="info" :closable="false" class="import-tip"
        title="粘贴 OpenAPI / Swagger 规范（JSON 或 YAML）。导入后会创建接口定义，勾选要压测的接口后保存。" />
      <el-input v-model="importSpec" type="textarea" :rows="12" class="spec-input"
        placeholder="粘贴 OpenAPI 规范原文" />
      <div v-if="operationOptions.length > 0" class="import-preview">
        本次已解析 {{ operationOptions.length }} 个接口，已默认全选；关闭后可在「用例选择」里调整。
      </div>
      <template #footer>
        <el-button @click="importVisible = false">取消</el-button>
        <el-button type="primary" :loading="importing" :disabled="!importSpec.trim()" @click="handleImport">
          导入
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Check, CopyDocument, Delete, Download, Plus, Refresh, Upload, VideoPlay } from '@element-plus/icons-vue'
import PageHeaderBar from '@/components/common/PageHeaderBar.vue'
import { getEnvironments } from '@/api/environment'
import { getTestCase, getTestCases } from '@/api/testcase'
import {
  generateLoadTestScript, getLoadTest, getLoadTestRuns, getLoadTestScript, importLoadTestOpenApi,
  runLoadTest, updateLoadTest,
} from '@/api/loadtest'
import { formatDuration, formatFullDateTime } from '@/utils/formatter'
import { EXECUTION_STATUS_LABELS, executionStatusTagType } from '@/types/execution'
import { TestType, type TestCaseSummary } from '@/types/testcase'
import type { EnvironmentView } from '@/types/environment'
import {
  defaultLoadTestProfile, formatLoadText, formatRate, operationKey, parseK6Duration, parseOperationKey,
  secondsToK6Duration, LoadTestSource, LOAD_TEST_EXECUTOR_LABELS, LOAD_TEST_SOURCE_LABELS,
  THRESHOLD_AGGREGATOR_OPTIONS, THRESHOLD_METRIC_LABELS, THRESHOLD_OPERATOR_OPTIONS,
  type LoadTestProfile, type LoadTestRunSummary, type LoadTestScenarioDetail, type LoadTestThreshold,
  type OpenApiOperation, type UpdateLoadTestScenarioRequest,
} from '@/types/loadtest'

const route = useRoute()
const router = useRouter()
const scenarioId = route.params.id as string

const loading = ref(false)
const saving = ref(false)
const running = ref(false)
const detail = ref<LoadTestScenarioDetail | null>(null)
const environmentOptions = ref<EnvironmentView[]>([])

// ------------------------------------------------------------ 基本信息

const form = reactive({
  name: '',
  description: '',
  targetBaseUrl: '',
  environmentId: '',
})

const isOpenApi = computed(() => detail.value?.source === LoadTestSource.OpenApi)

// ------------------------------------------------------------ 用例选择

const caseIds = ref<string[]>([])
const caseOptions = ref<TestCaseSummary[]>([])
const caseCache = ref<Record<string, TestCaseSummary>>({})
const caseKeyword = ref('')
const caseLoading = ref(false)

/** 候选项 = 当前搜索结果 ∪ 已选用例（已选但不在结果页里的也要显示，否则会"凭空消失"） */
const caseOptionList = computed(() => {
  const map = new Map<string, TestCaseSummary>()
  caseOptions.value.forEach((c) => map.set(c.id, c))
  caseIds.value.forEach((id) => {
    const cached = caseCache.value[id]
    if (cached && !map.has(id)) map.set(id, cached)
  })
  return [...map.values()]
})

const selectedCaseCount = computed(() =>
  isOpenApi.value ? selectedOperations.value.length : caseIds.value.length)

const loadCases = async () => {
  if (!detail.value?.projectId) return
  caseLoading.value = true
  try {
    const res = await getTestCases({
      projectId: detail.value.projectId,
      search: caseKeyword.value || undefined,
      page: 1,
      pageSize: 100,
    })
    // 压测只针对接口用例：UI/移动端用例的请求步骤无法直接转成 k6
    caseOptions.value = res.items.filter((c) => c.type === TestType.Api)
    res.items.forEach((c) => (caseCache.value[c.id] = c))
  } finally {
    caseLoading.value = false
  }
}

const searchCases = (query: string) => {
  caseKeyword.value = query
  return loadCases()
}

/** 详情里只存了 caseIds；把不在首批结果里的已选用例补拉回来，保证下拉能显示名称 */
const resolveSelectedCases = async () => {
  const missing = caseIds.value.filter((id) => !caseCache.value[id])
  if (missing.length === 0) return
  const fetched = await Promise.all(missing.map((id) => getTestCase(id).catch(() => null)))
  fetched.forEach((c) => {
    if (c) caseCache.value[c.id] = c
  })
}

// ------------------------------------------------------------ OpenAPI 导入

const operationOptions = ref<OpenApiOperation[]>([])
const selectedOperations = ref<string[]>([])
const apiDefinitionId = ref<string | null>(null)
const importVisible = ref(false)
const importing = ref(false)
const importSpec = ref('')

const openImport = () => {
  importSpec.value = ''
  importVisible.value = true
}

const selectAllOperations = () => {
  selectedOperations.value = operationOptions.value.map(operationKey)
}

const clearOperations = () => {
  selectedOperations.value = []
}

const handleImport = async () => {
  if (!detail.value) return
  importing.value = true
  try {
    const result = await importLoadTestOpenApi({
      projectId: detail.value.projectId,
      name: form.name.trim() || detail.value.name,
      spec: importSpec.value,
    })
    apiDefinitionId.value = result.apiDefinitionId
    operationOptions.value = result.operations
    selectAllOperations()
    // 规范里带了 servers/baseUrl 时，顺手把目标地址填上，省得用户再抄一遍
    if (!form.targetBaseUrl.trim() && result.baseUrl) form.targetBaseUrl = result.baseUrl
    importVisible.value = false
    ElMessage.success(`已解析 ${result.operations.length} 个接口，请勾选后保存`)
  } finally {
    importing.value = false
  }
}

// ------------------------------------------------------------ 负载与阈值

const profile = reactive<LoadTestProfile>(defaultLoadTestProfile())
const thresholds = ref<LoadTestThreshold[]>([])
const durationSeconds = ref(60)

const defaultThreshold = (): LoadTestThreshold => ({
  metric: 'http_req_duration', aggregator: 'p(95)', operator: '<', value: 500,
})

const addStage = () => profile.stages.push({ duration: '30s', target: 10 })

/**
 * 场景头部摘要（并发数 / 时长）。列表页展示的就是这两个数，必须和 profile 自洽：
 * 梯度加压只能把各段目标取峰值、时长求和；恒定类直接取输入值。
 */
const headline = computed(() => {
  if (profile.kind === 'ramping-vus') {
    const vus = profile.stages.reduce((max, s) => Math.max(max, Number(s.target) || 0), 0)
    const duration = profile.stages.reduce((sum, s) => sum + parseK6Duration(s.duration), 0)
    return { vus, duration }
  }
  if (profile.kind === 'constant-arrival-rate') {
    return { vus: Number(profile.maxVUs) || 0, duration: Number(durationSeconds.value) || 0 }
  }
  return { vus: Number(profile.vus) || 0, duration: Number(durationSeconds.value) || 0 }
})

/** 切执行器时把时长/并发接续过来，避免从恒定并发切过去后输入框归零 */
watch(() => profile.kind, (kind) => {
  if (kind !== 'ramping-vus') {
    const seconds = parseK6Duration(profile.duration)
    if (seconds > 0) durationSeconds.value = seconds
  }
  if (kind === 'constant-vus' && (!profile.vus || profile.vus <= 0)) profile.vus = headline.value.vus || 10
})

const buildProfile = (): LoadTestProfile => {
  const next: LoadTestProfile = {
    ...profile,
    vus: Number(profile.vus) || 0,
    rate: Number(profile.rate) || 0,
    preAllocatedVUs: Number(profile.preAllocatedVUs) || 0,
    maxVUs: Number(profile.maxVUs) || 0,
    thinkTimeSeconds: Number(profile.thinkTimeSeconds) || 0,
    stages: profile.stages.map((s) => ({ duration: s.duration, target: Number(s.target) || 0 })),
  }
  // 恒定类执行器的总时长由 durationSeconds 决定，回写成 k6 字面量
  if (next.kind === 'constant-vus') {
    next.vus = Number(profile.vus) || 0
    next.duration = secondsToK6Duration(durationSeconds.value)
  } else if (next.kind === 'constant-arrival-rate') {
    next.duration = secondsToK6Duration(durationSeconds.value)
  }
  return next
}

// ------------------------------------------------------------ 脚本

const scriptText = ref('')
const scriptHash = ref<string | null>(null)
const scriptGeneratedAt = ref<string | null>(null)
const warnings = ref<string[]>([])
const generating = ref(false)

/** 详情里若只有指纹没有正文（后端为省流量没带），按需重取一次原文 */
const refreshScript = async () => {
  const text = await getLoadTestScript(scenarioId)
  scriptText.value = typeof text === 'string' ? text : String(text ?? '')
}

const handleGenerate = async () => {
  if (!detail.value) return
  generating.value = true
  try {
    const result = await generateLoadTestScript(scenarioId)
    scriptText.value = result.script
    scriptHash.value = result.hash
    scriptGeneratedAt.value = new Date().toISOString()
    warnings.value = result.warnings ?? []
    if (warnings.value.length > 0) {
      ElMessage.warning(`脚本已生成，但有 ${warnings.value.length} 处未能翻译，请查看上方提示`)
    } else {
      ElMessage.success('脚本已生成')
    }
  } finally {
    generating.value = false
  }
}

/** 复制文本：优先 Clipboard API，非安全上下文（http）下退回旧式 execCommand */
const copyText = async (text: string): Promise<boolean> => {
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

const copyScript = async () => {
  const ok = await copyText(scriptText.value)
  ElMessage[ok ? 'success' : 'warning'](ok ? '脚本已复制到剪贴板' : '复制失败，请手动选中脚本复制')
}

const downloadScript = () => {
  const blob = new Blob([scriptText.value], { type: 'text/javascript' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${form.name.trim() || 'loadtest'}.js`
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

// ------------------------------------------------------------ 运行历史 / 运行

const runs = ref<LoadTestRunSummary[]>([])
const runsLoading = ref(false)

const loadRuns = async () => {
  runsLoading.value = true
  try {
    runs.value = await getLoadTestRuns(scenarioId, 20)
  } finally {
    runsLoading.value = false
  }
}

const handleRun = async () => {
  if (!detail.value) return
  running.value = true
  try {
    const result = await runLoadTest(scenarioId)
    ElMessage.success(`已开始运行，runId：${result.runId}`)
    await loadRuns()
  } catch {
    // 409（项目内已有运行中任务）/ 400（脚本未生成等）由请求拦截器统一提示服务端消息
  } finally {
    running.value = false
  }
}

// ------------------------------------------------------------ 加载 / 保存

/** 后端返回的 profile 可能缺字段（老数据），逐字段兜底，避免 reactive 上出现 undefined */
const applyProfile = (source?: LoadTestProfile | null) => {
  const base = defaultLoadTestProfile()
  const p = source ?? base
  profile.kind = p.kind ?? base.kind
  profile.vus = p.vus ?? base.vus
  profile.rate = p.rate ?? base.rate
  profile.timeUnit = p.timeUnit ?? base.timeUnit
  profile.preAllocatedVUs = p.preAllocatedVUs ?? base.preAllocatedVUs
  profile.maxVUs = p.maxVUs ?? base.maxVUs
  profile.stages = Array.isArray(p.stages) && p.stages.length > 0
    ? p.stages.map((s) => ({ duration: s.duration, target: s.target }))
    : base.stages.map((s) => ({ ...s }))
  profile.duration = p.duration ?? base.duration
  profile.gracefulRampDown = p.gracefulRampDown ?? base.gracefulRampDown
  profile.thinkTimeSeconds = p.thinkTimeSeconds ?? base.thinkTimeSeconds
}

const loadAll = async () => {
  loading.value = true
  try {
    const data = await getLoadTest(scenarioId)
    detail.value = data
    form.name = data.name
    form.description = data.description ?? ''
    form.targetBaseUrl = data.targetBaseUrl ?? ''
    form.environmentId = data.environmentId ?? ''
    apiDefinitionId.value = data.apiDefinitionId ?? null

    applyProfile(data.profile)
    thresholds.value = data.thresholds?.length
      ? data.thresholds.map((t) => ({ ...t }))
      : [defaultThreshold()]
    // 恒定类的时长来自场景头部；梯度加压时该值不参与输入
    durationSeconds.value = data.durationSeconds > 0 ? data.durationSeconds : parseK6Duration(profile.duration)

    scriptText.value = data.scriptText ?? ''
    scriptHash.value = data.scriptHash ?? null
    scriptGeneratedAt.value = data.scriptGeneratedAt ?? null
    warnings.value = []

    caseIds.value = [...data.caseIds]
    selectedOperations.value = [...data.operations]
    operationOptions.value = data.operations.map(parseOperationKey)

    environmentOptions.value = data.projectId ? await getEnvironments(data.projectId) : []
    if (data.source === LoadTestSource.Cases) {
      await loadCases()
      await resolveSelectedCases()
    }
    // 有指纹却没带正文 → 按需重取一次，保证预览与下载可用
    if (!scriptText.value && data.scriptHash) await refreshScript()

    await loadRuns()
  } finally {
    loading.value = false
  }
}

const handleSave = async () => {
  if (!detail.value) return
  if (!form.name.trim()) {
    ElMessage.warning('请输入场景名称')
    return
  }
  if (isOpenApi.value && selectedOperations.value.length === 0) {
    ElMessage.warning('请至少勾选一个要压测的接口')
    return
  }
  if (!isOpenApi.value && caseIds.value.length === 0) {
    ElMessage.warning('请至少选择一条接口用例')
    return
  }

  saving.value = true
  try {
    const payload: UpdateLoadTestScenarioRequest = {
      name: form.name.trim(),
      description: form.description.trim() || null,
      environmentId: form.environmentId || null,
      targetBaseUrl: form.targetBaseUrl.trim() || null,
      apiDefinitionId: apiDefinitionId.value,
      operations: selectedOperations.value,
      profile: buildProfile(),
      thresholds: thresholds.value.map((t) => ({ ...t, value: Number(t.value) || 0 })),
      variables: { ...(detail.value.variables ?? {}) },
      virtualUsers: headline.value.vus,
      durationSeconds: headline.value.duration,
      caseIds: caseIds.value,
    }
    const saved = await updateLoadTest(scenarioId, payload)
    detail.value = saved
    // 配置变更会让旧脚本失效，指纹以服务端返回为准
    scriptHash.value = saved.scriptHash ?? null
    ElMessage.success('已保存')
  } finally {
    saving.value = false
  }
}

onMounted(loadAll)
</script>

<style scoped>
.loadtest-detail {
  padding-bottom: 12px;
}

.section-card {
  margin-bottom: 12px;
}

.section-head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.section-head-spacer {
  flex: 1;
}

.section-title {
  font-size: 15px;
  font-weight: 600;
}

.form-row {
  display: flex;
  gap: 16px;
}

.form-name {
  flex: 1;
  min-width: 0;
}

.form-fixed {
  width: 280px;
}

.readonly-text {
  color: var(--el-text-color-regular);
}

.full-width {
  width: 100%;
}

.form-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.6;
}

.inline-label {
  margin: 0 8px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.executor-select {
  width: 260px;
}

.time-unit-select {
  width: 80px;
}

/* 选项右侧的模块名弱化，避免与用例名抢视线 */
.option-sub {
  float: right;
  margin-left: 12px;
  color: var(--el-text-color-placeholder);
  font-size: 12px;
}

/* ------------------------------------------------------------ OpenAPI 接口列表 */
.op-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.op-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.op-list {
  display: flex;
  flex-direction: column;
  max-height: 320px;
  overflow: auto;
  padding: 4px 8px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
}

.op-item {
  margin-right: 0;
  height: 30px;
}

.op-method {
  display: inline-block;
  min-width: 52px;
  margin-right: 6px;
  font-weight: 600;
  font-size: 12px;
  color: var(--el-color-primary);
}

.op-path {
  font-family: Consolas, Monaco, monospace;
}

.op-label {
  margin-left: 10px;
  color: var(--el-text-color-placeholder);
  font-size: 12px;
}

/* ------------------------------------------------------------ 加压曲线 / 阈值 */
.stages-editor,
.thresholds-editor {
  width: 100%;
}

.stage-row,
.threshold-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.stage-index {
  width: 20px;
  text-align: center;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.stage-duration {
  width: 110px;
}

.stage-target {
  width: 130px;
}

.stage-label {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.th-metric {
  width: 150px;
}

.th-aggregator {
  width: 130px;
}

.th-operator {
  width: 120px;
}

.th-value {
  width: 130px;
}

/* ------------------------------------------------------------ 脚本 */
.script-warning {
  margin-bottom: 10px;
}

.warning-list {
  margin: 6px 0 0;
  padding-left: 18px;
  font-size: 13px;
  line-height: 1.8;
}

.script-preview {
  margin: 0;
  max-height: 460px;
  overflow: auto;
  padding: 12px;
  background: #1e1e1e;
  color: #d4d4d4;
  border-radius: 6px;
  font-family: Consolas, Monaco, 'Courier New', monospace;
  font-size: 12px;
  line-height: 1.6;
  white-space: pre;
}

.script-meta {
  display: flex;
  gap: 16px;
  margin-top: 8px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

/* ------------------------------------------------------------ 运行历史 */
.table-wrap-x {
  width: 100%;
  overflow-x: auto;
}

.muted {
  color: var(--el-text-color-placeholder);
}

.import-tip {
  margin-bottom: 10px;
}

.spec-input :deep(textarea) {
  font-family: Consolas, Monaco, monospace;
}

.import-preview {
  margin-top: 8px;
  font-size: 12px;
  color: var(--el-color-success);
}

/* 窄屏：两栏表单并排会把输入框压到一百多像素，退回上下一列 */
@media (max-width: 767px) {
  .form-row {
    flex-direction: column;
    gap: 0;
  }

  .form-fixed {
    width: 100%;
  }

  .executor-select {
    width: 100%;
  }

  .stage-row,
  .threshold-row {
    flex-wrap: wrap;
  }
}
</style>
