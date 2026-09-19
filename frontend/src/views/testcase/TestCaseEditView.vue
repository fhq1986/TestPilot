<template>
  <div class="case-editor">
    <PageHeaderBar title="编辑用例" :subtitle="testCase?.name ?? ''" :back-to="`/testcases/${caseId}`" sticky />

    <el-card v-loading="savingInfo" class="info-card">
      <template #header>
        <span>执行设置</span>
      </template>
      <el-form inline>
        <el-form-item label="模块">
          <el-input v-model="moduleName" class="module-input" placeholder="如 1. 登录认证模块" clearable />
        </el-form-item>
        <el-form-item label="用例编号">
          <el-input v-model="caseCode" class="module-input" placeholder="如 TC-LOGIN-001" clearable />
        </el-form-item>
        <el-form-item label="优先级">
          <el-select v-model="priority" class="priority-select" placeholder="未设置" clearable>
            <el-option v-for="p in PRIORITY_OPTIONS" :key="p.value" :label="p.label" :value="p.value" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="isApiType" label="BaseUrl">
          <el-input v-model="baseUrl" class="baseurl-input" :placeholder="baseUrlPlaceholder" clearable />
          <el-button v-if="environments.length" link type="primary" class="fill-env-btn"
            @click="fillFromEnvironment">使用环境地址</el-button>
        </el-form-item>
        <el-form-item label="超时(ms)">
          <el-input-number v-model="timeout" :min="1000" :max="600000" :step="1000" />
        </el-form-item>
        <el-form-item label="步骤重试">
          <el-input-number v-model="retryCount" :min="0" :max="5" />
        </el-form-item>
        <el-form-item label="失败即中止">
          <el-switch v-model="failFast" />
          <span class="info-hint">任一步骤失败后跳过剩余步骤</span>
        </el-form-item>
        <el-form-item v-if="!isApiType" label="浏览器">
          <el-select v-model="browser" class="browser-select" placeholder="默认 Chromium">
            <el-option v-for="b in BROWSER_OPTIONS" :key="b.id" :label="b.label" :value="b.id" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="!isApiType" label="视觉回归">
          <el-switch v-model="visualEnabled" />
          <el-input-number v-if="visualEnabled" v-model="visualThresholdPercent" :min="0" :max="100" :step="0.5"
            :precision="2" class="threshold-input" />
          <span v-if="visualEnabled" class="info-hint">
            差异阈值 %（超过该比例判定为变化；基线共 {{ baselineCount }} 个）
          </span>
          <span v-else class="info-hint">开启后每次执行会与基线截图比对；基线按浏览器分别建立</span>
        </el-form-item>
        <!-- 忽略区域：时间戳/广告位/头像这类每次渲染必然不同的区域，比对时直接屏蔽（降误报） -->
        <el-form-item v-if="!isApiType && visualEnabled" label="忽略区域">
          <div class="ignore-regions">
            <div v-for="(r, i) in ignoreRegions" :key="i" class="ignore-row">
              <span class="ignore-label">X</span>
              <el-input-number v-model="r.x" :min="0" :max="100" :step="1" :precision="1" size="small" />
              <span class="ignore-label">Y</span>
              <el-input-number v-model="r.y" :min="0" :max="100" :step="1" :precision="1" size="small" />
              <span class="ignore-label">宽</span>
              <el-input-number v-model="r.w" :min="0" :max="100" :step="1" :precision="1" size="small" />
              <span class="ignore-label">高</span>
              <el-input-number v-model="r.h" :min="0" :max="100" :step="1" :precision="1" size="small" />
              <span class="ignore-unit">%</span>
              <el-button link type="danger" size="small" @click="ignoreRegions.splice(i, 1)">删除</el-button>
            </div>
            <el-button size="small" @click="ignoreRegions.push({ x: 0, y: 0, w: 10, h: 10 })">添加区域</el-button>
            <span class="info-hint">坐标为相对截图的百分比；该区域内的像素差异不参与判定</span>
          </div>
        </el-form-item>
        <!-- 网络拦截放在视觉回归之后：同属"影响执行结果的环境因素"，放一起好找。
             只对 Web 用例开放——API 用例不走浏览器，没有可拦截的流量 -->
        <el-form-item v-if="!isApiType" label="网络规则">
          <NetworkRulesEditor v-model="networkRules" />
          <span class="info-hint">
            执行时在页面打开后、任何请求发出前注册；只作用于本用例，不会影响别的用例
          </span>
        </el-form-item>
        <!-- 扩展字段：按项目定义动态渲染；定义删除后值残留不渲染 -->
        <el-form-item v-for="field in customFieldDefs" :key="field.id" :label="field.name">
          <el-input-number v-if="field.fieldType === 1" v-model="customFieldValues[field.id] as number" :step="1"
            style="width: 220px" />
          <el-date-picker v-else-if="field.fieldType === 2" v-model="customFieldValues[field.id] as string" type="date"
            value-format="YYYY-MM-DD" style="width: 220px" />
          <el-select v-else-if="field.fieldType === 3" v-model="customFieldValues[field.id] as string" clearable
            style="width: 220px">
            <el-option v-for="opt in parseFieldOptions(field.options)" :key="opt" :label="opt" :value="opt" />
          </el-select>
          <el-input v-else v-model="customFieldValues[field.id] as string" maxlength="500" style="width: 220px" />
        </el-form-item>

        <el-form-item label="数据集">
          <el-select v-model="dataSetId" class="dataset-select" placeholder="不使用（不参数化）" clearable filterable>
            <el-option v-for="d in dataSetOptions" :key="d.id" :label="`${d.name}（${d.rowCount} 行）`" :value="d.id" />
          </el-select>
          <span class="info-hint">步骤里用 <code v-pre>{{列名}}</code> 取值，执行时按数据行展开</span>
        </el-form-item>
        <el-form-item label="关联需求">
          <el-select v-model="requirementId" class="dataset-select" placeholder="不关联" clearable filterable>
            <el-option v-for="r in requirementOptions" :key="r.id" :label="r.title" :value="r.id" />
          </el-select>
          <span class="info-hint">挂到需求后计入需求覆盖率统计</span>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="savingInfo" @click="handleSaveInfo">保存执行设置</el-button>
        </el-form-item>
      </el-form>

      <div v-if="isApiType" class="info-hint">{{ baseUrlHint }}</div>
      <el-alert v-if="variableCheck && variableCheck.missingVariables.length > 0" type="warning" :closable="false"
        class="variable-alert" :title="`用例引用了数据集里不存在的变量：${variableCheck.missingVariables.join('、')}`" />
      <el-alert v-else-if="variableCheck && variableCheck.dataSetId && variableCheck.usedVariables.length === 0"
        type="info" :closable="false" class="variable-alert" title="已绑定数据集，但步骤里还没有引用任何 {{变量}}；实际执行会按数据行展开" />
      <div v-else-if="variableCheck && variableCheck.dataSetId && variableCheck.unusedColumns.length > 0"
        class="info-hint">
        数据集中未被引用的列：{{ variableCheck.unusedColumns.join('、') }}
      </div>
    </el-card>

    <el-card v-loading="loading">
      <el-tabs v-model="editMode">
        <el-tab-pane label="可视化编辑" name="visual">
          <StepList :steps="steps" @add="openStepEditor(null)" @edit="openStepEditor" @add-shared="openSharedPicker"
            @remove="handleRemove" @move="handleMove" @reorder="handleReorder" />
          <div class="save-bar">
            <el-button type="primary" :loading="saving" @click="handleSave">保存步骤</el-button>
            <span class="save-hint">保存时自动按顺序重排步骤序号</span>
          </div>
        </el-tab-pane>
        <el-tab-pane label="自然语言" name="nlp">
          <div class="nlp-panel">
            <el-alert type="info" :closable="false" show-icon title="用自然语言描述测试流程，AI 自动转换为结构化步骤（转换后可在可视化编辑中微调）" />
            <el-input v-model="nlpText" type="textarea" :rows="6" maxlength="5000" show-word-limit class="nlp-input"
              placeholder="例如：打开登录页，输入用户名 admin 和密码 Admin@123456，点击登录按钮，等待 2 秒，验证页面出现「工作台」文字" />
            <div class="nlp-actions">
              <el-button type="primary" :loading="nlpLoading" :disabled="nlpText.trim().length < 10"
                @click="handleNlpConvert">
                AI 转换为步骤
              </el-button>
              <span v-if="nlpLoading" class="nlp-hint">AI 生成中，约需 10-30 秒…</span>
            </div>
            <template v-if="nlpPreview.length > 0">
              <el-divider content-position="left">转换结果（{{ nlpPreview.length }} 步）</el-divider>
              <el-table :data="nlpPreview" size="small" max-height="320">
                <el-table-column label="#" width="50">
                  <template #default="{ $index }">{{ $index + 1 }}</template>
                </el-table-column>
                <el-table-column label="动作" width="110">
                  <template #default="{ row }">
                    <el-tag size="small">{{ actionLabel(row.actionType) }}</el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="配置摘要" min-width="260">
                  <template #default="{ row }">{{ configSummary(row.config) }}</template>
                </el-table-column>
              </el-table>
              <div class="nlp-actions">
                <el-button type="primary" @click="applyNlpSteps('replace')">替换当前步骤</el-button>
                <el-button @click="applyNlpSteps('append')">追加到末尾</el-button>
                <el-button text @click="nlpPreview = []">放弃</el-button>
              </div>
            </template>
          </div>
        </el-tab-pane>
        <el-tab-pane label="代码模式" name="code">
          <div class="code-panel">
            <el-alert type="info" :closable="false" show-icon
              title="以 JSON 编辑步骤数组（stepOrder / actionType / config），actionType 为数字枚举，点击「应用」后写入可视化编辑" />
            <el-input v-model="codeText" type="textarea" :rows="18" spellcheck="false" class="code-input"
              placeholder="[]" />
            <div class="code-actions">
              <el-button @click="handleFormatCode">格式化 / 校验</el-button>
              <el-button type="primary" :disabled="!codeText.trim()" @click="handleApplyCode">
                应用到步骤
              </el-button>
              <el-button text @click="syncCodeFromSteps">从当前步骤重新生成</el-button>
            </div>
            <div class="code-hint">
              actionType：0=Click 1=Fill 2=Navigate 3=Wait 4=Screenshot 5=Scroll 6=Request 7=AssertResponse
              8=ExtractVariable 10=AIAssert 11=AssertVisible 12=AssertText 13=AssertUrl 14=AssertTitle
            </div>
          </div>
        </el-tab-pane>
      </el-tabs>
      <!-- 评论：用例评审/讨论的载体（通用评论组件，按用例挂载） -->
      <el-card class="comments-card" shadow="never">
        <CommentSection :target="0" :target-id="caseId" />
      </el-card>

    </el-card>

    <StepEditor :visible="editorVisible" :step="editingStep" @update:visible="editorVisible = $event"
      @save="handleStepSave" />

    <!-- 插入共享步骤：选组 + 覆盖变量 -->
    <el-dialog v-model="sharedVisible" title="插入共享步骤" width="700px" @closed="resetSharedForm">
      <el-alert type="warning" :closable="false" class="shared-tip">
        <template #title>
          共享步骤在运行到该位置时才展开为组内的具体步骤，顺序与后续步骤自动顺延。
          组内容改动后，本用例下次执行即生效，无需重新保存。
        </template>
      </el-alert>
      <el-form :model="sharedForm" label-width="90px">
        <el-form-item label="共享步骤组">
          <el-select v-model="sharedForm.groupId" placeholder="选择共享步骤组" class="w-full" :loading="sharedLoading"
            @change="onSharedGroupChange">
            <el-option v-for="g in sharedGroups" :key="g.id" :label="g.name" :value="g.id">
              <span>{{ g.name }}</span>
              <span class="option-note">{{ g.itemCount }} 步</span>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item v-if="sharedGroups.length === 0" label=" ">
          <span class="empty-hint">
            当前项目还没有共享步骤组，可先到「共享步骤」页创建（例如把登录流程抽出来）。
          </span>
        </el-form-item>
        <el-form-item v-if="sharedForm.variables.length > 0" label="变量覆盖">
          <div class="override-block">
            <div class="override-hint">
              留空表示使用组内默认值；填写后仅对本用例生效。
            </div>
            <div v-for="v in sharedForm.variables" :key="v.name" class="override-row">
              <span class="override-name">{{ v.name }}</span>
              <el-input v-model="v.value" size="small" :placeholder="`组内默认：${v.defaultValue || '（空）'}`" />
            </div>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="sharedVisible = false">取消</el-button>
        <el-button type="primary" :disabled="!sharedForm.groupId" @click="handleInsertShared">
          插入
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import PageHeaderBar from '@/components/common/PageHeaderBar.vue'
import CommentSection from '@/components/common/CommentSection.vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { getTestCase, updateTestCase, updateTestCaseSteps, listCustomFields, type CustomFieldDef } from '@/api/testcase'
import { getEnvironments } from '@/api/environment'
import { checkCaseVariables, getDataSets } from '@/api/dataset'
import { getRequirements } from '@/api/requirement'
import { getVisualCaseSetting } from '@/api/visual'
import {
  getSharedStepGroupApi, sharedStepOptionsApi, type SharedStepOption,
} from '@/api/sharedStep'
import type { CaseVariableCheck, DataSetSummary } from '@/types/dataset'
import type { EnvironmentView } from '@/types/environment'
import { generateCases } from '@/api/ai'
import StepList, { type EditableStep } from '@/components/case-editor/StepList.vue'
import StepEditor from '@/components/case-editor/StepEditor.vue'
import NetworkRulesEditor from '@/components/case-editor/NetworkRulesEditor.vue'
import { ActionType, TestType, parseNetworkRules, serializeNetworkRules, type ActionType as ActionTypeT, type CreateTestStepPayload, type NetworkRule, type SharedVariableEntry, type StepConfig, type TestCase } from '@/types/testcase'
import type { GeneratedStep } from '@/types/ai'

const route = useRoute()
const router = useRouter()
const caseId = route.params.id as string

const testCase = ref<TestCase | null>(null)
const steps = ref<EditableStep[]>([])
const loading = ref(false)
const saving = ref(false)
const savingInfo = ref(false)
/** 当前用例所属项目：插入共享步骤时按项目过滤可选组 */
const projectId = ref('')
const baseUrl = ref('')
const moduleName = ref('')
const caseCode = ref('')
const priority = ref('')

/** 优先级选项（与导入映射保持一致） */
const PRIORITY_OPTIONS = [
  { value: 'P0', label: 'P0 高' },
  { value: 'P1', label: 'P1 中' },
  { value: 'P2', label: 'P2 低' },
  { value: 'P3', label: 'P3 最低' },
]
const timeout = ref(30000)
const retryCount = ref(0)
const failFast = ref(false)

// 迭代 B：浏览器 / 视觉回归 / 数据集
const BROWSER_OPTIONS = [
  { id: 'chromium', label: 'Chromium（Blink，默认）' },
  { id: 'firefox', label: 'Firefox（Gecko）' },
  { id: 'webkit', label: 'WebKit（Safari 内核）' },
]
const browser = ref('chromium')
const visualEnabled = ref(false)
/** 界面按百分比展示，提交时换算成 0-1 的比例 */
const visualThresholdPercent = ref(1)
/** 忽略区域（页面编辑数组，提交时序列化成 JSON）；元素形状与后端 {x,y,w,h} 一致 */
const ignoreRegions = ref<Array<{ x: number; y: number; w: number; h: number }>>([])

// ------------------------------ 扩展字段（按项目定义动态渲染）
const customFieldDefs = ref<CustomFieldDef[]>([])
/** 键 = 定义 Id；值统一字符串（数字/日期控件用 v-model 时会自动转） */
const customFieldValues = ref<Record<string, string | number | null>>({})
const parseFieldOptions = (options?: string | null): string[] => {
  if (!options) return []
  try {
    const list = JSON.parse(options) as unknown
    return Array.isArray(list) ? (list as string[]) : []
  } catch {
    return []
  }
}

/** 详情里的忽略区域 JSON → 可编辑数组；坏数据静默清空（后端提交时会再校验一次） */
const parseIgnoreRegions = (json?: string | null) => {
  if (!json) return []
  try {
    const list = JSON.parse(json) as Array<Record<string, unknown>>
    if (!Array.isArray(list)) return []
    return list
      .map((it) => ({
        x: Number(it.x) || 0, y: Number(it.y) || 0,
        w: Number(it.w) || 0, h: Number(it.h) || 0,
      }))
      .filter((r) => r.w > 0 && r.h > 0)
  } catch {
    return []
  }
}
/**
 * 网络规则在页面上是**数组**形态，提交时才序列化成后端要的 JSON 字符串。
 * 让编辑器直接编辑数组而不是 JSON 文本，是为了不把"手写 JSON 不许写错"的负担丢给用户。
 */
const networkRules = ref<NetworkRule[]>([])
const dataSetId = ref('')
const dataSetOptions = ref<DataSetSummary[]>([])
// 需求覆盖：候选列表与当前关联
const requirementId = ref('')
const requirementOptions = ref<{ id: string; title: string }[]>([])
const baselineCount = ref(0)
const variableCheck = ref<CaseVariableCheck | null>(null)
const editMode = ref('visual')

const editorVisible = ref(false)
const editingStep = ref<EditableStep | null>(null)

// 自然语言模式
const nlpText = ref('')
const nlpLoading = ref(false)
const nlpPreview = ref<GeneratedStep[]>([])

// 代码模式
const codeText = ref('')

const isApiType = computed(() => testCase.value?.type === TestType.Api)

// BaseUrl 为「可选覆盖」：未填写时执行会自动使用所选环境的地址
const environments = ref<EnvironmentView[]>([])
const baseUrlPlaceholder = computed(() => {
  const env = environments.value[0]
  return env ? `留空自动使用环境地址：${env.baseUrl}` : '留空自动使用执行时选择的环境地址'
})
const baseUrlHint = computed(() => {
  if (baseUrl.value.trim()) {
    return '已单独指定 BaseUrl，执行时优先使用它（不再跟随环境地址）。'
  }
  const list = environments.value.map((e) => `${e.name}（${e.baseUrl}）`).join('、')
  return `未单独指定：执行时自动使用所选环境的地址${list ? `；本项目环境：${list}` : ''}。仅需覆盖环境地址时才填写。`
})

/** 需要显式覆盖时，一键填入首个环境的地址 */
const fillFromEnvironment = () => {
  const env = environments.value[0]
  if (env?.baseUrl) baseUrl.value = env.baseUrl
}

let localIdSeed = 1

const actionLabels: Record<number, string> = {
  [ActionType.Click]: '点击', [ActionType.Fill]: '输入', [ActionType.Navigate]: '打开',
  [ActionType.Wait]: '等待', [ActionType.Screenshot]: '截图', [ActionType.Scroll]: '滚动',
  [ActionType.Request]: '接口请求', [ActionType.AssertResponse]: '响应断言',
  [ActionType.ExtractVariable]: '变量提取', [ActionType.AIAssert]: 'AI 断言',
  [ActionType.AssertVisible]: '可见性断言', [ActionType.AssertText]: '文本断言',
  [ActionType.AssertUrl]: 'URL 断言', [ActionType.AssertTitle]: '标题断言',
}
const actionLabel = (t: number) => actionLabels[t] ?? `未知(${t})`

const configSummary = (config?: StepConfig) => {
  if (!config) return '—'
  const parts: string[] = []
  if (config.url) parts.push(config.url)
  if (config.selector?.value) parts.push(`选择器: ${config.selector.value}`)
  else if (config.selector?.description) parts.push(`元素: ${config.selector.description}`)
  if (config.method) parts.push(`${config.method} ${config.endpoint ?? ''}`)
  if (config.value) parts.push(`值: ${config.value}`)
  if (config.body) parts.push(`体: ${config.body.length > 50 ? config.body.slice(0, 50) + '…' : config.body}`)
  return parts.join(' · ') || '—'
}

/** 历史上用例可能存的是 chrome / Chrome 等写法，统一展示为引擎名 */
const normalizeBrowser = (value?: string | null) => {
  const key = (value ?? '').trim().toLowerCase()
  if (key.startsWith('firefox') || key === 'ff') return 'firefox'
  if (key.startsWith('webkit') || key.startsWith('safari')) return 'webkit'
  return 'chromium'
}

/** 拉取数据集候选、视觉配置与变量匹配情况 */
const loadIterationBContext = async (projectId: string) => {
  try {
    const res = await getDataSets({ projectId, page: 1, pageSize: 100 })
    dataSetOptions.value = res.items
  } catch {
    dataSetOptions.value = []
  }
  // 需求候选：同项目下的需求（覆盖率锚点）
  try {
    const res = await getRequirements({ projectId, page: 1, pageSize: 200 })
    requirementOptions.value = res.items.map((r) => ({ id: r.id, title: r.title }))
  } catch {
    requirementOptions.value = []
  }
  try {
    const setting = await getVisualCaseSetting(caseId)
    baselineCount.value = setting.baselineCount
  } catch {
    baselineCount.value = 0
  }
  await refreshVariableCheck()
}

const refreshVariableCheck = async () => {
  try {
    variableCheck.value = await checkCaseVariables(caseId)
  } catch {
    variableCheck.value = null
  }
}

const load = async () => {
  loading.value = true
  try {
    const data = await getTestCase(caseId)
    testCase.value = data
    projectId.value = data.projectId
    baseUrl.value = data.baseUrl ?? ''
    moduleName.value = data.module ?? ''
    caseCode.value = data.caseCode ?? ''
    priority.value = data.priority ?? ''
    timeout.value = data.timeout
    retryCount.value = data.retryCount
    failFast.value = data.failFast ?? false
    browser.value = normalizeBrowser(data.browser)
    visualEnabled.value = data.visualEnabled ?? false
    visualThresholdPercent.value = Math.round((data.visualThreshold ?? 0.01) * 10000) / 100
    ignoreRegions.value = parseIgnoreRegions(data.visualIgnoreRegions)
    // 扩展字段：先拉项目定义，再把详情里的值回填（无定义的键忽略）
    try {
      customFieldDefs.value = await listCustomFields(data.projectId)
    } catch { /* 定义拉取失败不阻塞编辑 */ }
    const saved = (() => {
      try { return data.customFields ? (JSON.parse(data.customFields) as Record<string, string>) : {} }
      catch { return {} }
    })()
    const values: Record<string, string | number | null> = {}
    for (const def of customFieldDefs.value) {
      const v = saved[def.id]
      values[def.id] = v ?? null
    }
    customFieldValues.value = values
    // 坏 JSON 会被 parseNetworkRules 吞成空数组（不让编辑页打不开）；真正的校验在服务端
    networkRules.value = parseNetworkRules(data.networkRules)
    dataSetId.value = data.dataSetId ?? ''
    requirementId.value = data.requirementId ?? ''
    steps.value = data.steps.map((s) => ({ ...s, localId: localIdSeed++ }))
    // 环境地址用于 BaseUrl 的继承提示（用例未配置时执行会自动使用）
    try {
      environments.value = await getEnvironments(data.projectId)
    } catch {
      environments.value = []
    }
    // 迭代 B：数据集候选、视觉基线与变量匹配情况
    await loadIterationBContext(data.projectId)
  } finally {
    loading.value = false
  }
}

// ---------- 自然语言模式 ----------
const handleNlpConvert = async () => {
  if (nlpText.value.trim().length < 10) {
    ElMessage.warning('请至少输入 10 个字的流程描述')
    return
  }
  nlpLoading.value = true
  try {
    // API 用例在需求中补上下文，引导 LLM 生成接口步骤
    const requirement = isApiType.value
      ? `接口测试（BaseUrl: ${baseUrl.value || '未设置'}）：${nlpText.value.trim()}`
      : nlpText.value.trim()
    const cases = await generateCases({ requirement, minCases: 1 })
    const generated = cases.find((c) => c.steps.length > 0)
    if (!generated || generated.steps.length === 0) {
      ElMessage.warning('AI 未能生成有效步骤，请尝试更具体的描述')
      return
    }
    nlpPreview.value = generated.steps
    ElMessage.success(`AI 已生成 ${generated.steps.length} 个步骤，请预览确认`)
  } finally {
    nlpLoading.value = false
  }
}

const applyNlpSteps = (mode: 'replace' | 'append') => {
  const mapped = nlpPreview.value.map((gs, i) => toEditableStep(gs, steps.value.length + i))
  if (mode === 'replace') steps.value = mapped
  else steps.value = [...steps.value, ...mapped]
  nlpPreview.value = []
  ElMessage.success(`已${mode === 'replace' ? '替换' : '追加'} ${mapped.length} 个步骤，可在可视化编辑中查看并保存`)
  editMode.value = 'visual'
}

const toEditableStep = (gs: GeneratedStep, index: number): EditableStep => ({
  id: '',
  localId: localIdSeed++,
  stepOrder: gs.stepOrder ?? index,
  actionType: gs.actionType as ActionTypeT,
  config: gs.config ?? {},
  aiInstruction: null,
  aiElementDescription: gs.aiElementDescription ?? null,
})

// ---------- 代码模式 ----------
const buildCodeJson = () =>
  JSON.stringify(
    steps.value.map((s) => ({
      stepOrder: s.stepOrder,
      actionType: s.actionType,
      config: s.config,
      aiInstruction: s.aiInstruction ?? null,
      aiElementDescription: s.aiElementDescription ?? null,
    })),
    null,
    2,
  )

// 切到代码模式时同步最新步骤；离开时清空预览状态
watch(editMode, (mode) => {
  if (mode === 'code' && !codeText.value.trim()) codeText.value = buildCodeJson()
})

const syncCodeFromSteps = () => {
  codeText.value = buildCodeJson()
  ElMessage.success('已从当前步骤生成 JSON')
}

const handleFormatCode = () => {
  try {
    const parsed = JSON.parse(codeText.value)
    codeText.value = JSON.stringify(parsed, null, 2)
    ElMessage.success('JSON 格式正确')
  } catch (ex) {
    ElMessage.error(`JSON 解析失败: ${ex instanceof Error ? ex.message : String(ex)}`)
  }
}

const handleApplyCode = () => {
  let parsed: unknown
  try {
    parsed = JSON.parse(codeText.value)
  } catch (ex) {
    ElMessage.error(`JSON 解析失败: ${ex instanceof Error ? ex.message : String(ex)}`)
    return
  }
  if (!Array.isArray(parsed)) {
    ElMessage.error('顶层必须是步骤数组（[]）')
    return
  }
  const validActions = new Set<number>(Object.values(ActionType).filter((v): v is number => typeof v === 'number'))
  const mapped: EditableStep[] = []
  for (const [i, item] of parsed.entries()) {
    const row = item as Record<string, unknown>
    const actionType = row?.actionType
    if (typeof actionType !== 'number' || !validActions.has(actionType)) {
      ElMessage.error(`第 ${i + 1} 步 actionType 无效（${String(actionType)}），须为 0-12 的枚举值`)
      return
    }
    mapped.push({
      id: '',
      localId: localIdSeed++,
      stepOrder: typeof row.stepOrder === 'number' ? row.stepOrder : i,
      actionType: actionType as ActionTypeT,
      config: (row.config ?? {}) as StepConfig,
      aiInstruction: (row.aiInstruction as string | null) ?? null,
      aiElementDescription: (row.aiElementDescription as string | null) ?? null,
    })
  }
  if (mapped.length === 0) {
    ElMessage.warning('步骤数组为空，未应用（如需清空请直接在可视化编辑删除）')
    return
  }
  steps.value = mapped
  ElMessage.success(`已应用 ${mapped.length} 个步骤，可在可视化编辑中查看并保存`)
  editMode.value = 'visual'
}

// ---------- 原有可视化编辑逻辑 ----------
const openStepEditor = (row: EditableStep | null) => {
  // 共享步骤引用不走向量编辑器：它的 actionType/config 无意义，只能改变量覆盖
  if (row?.sharedGroupId) {
    void openSharedOverride(row)
    return
  }
  editingStep.value = row
  editorVisible.value = true
}

// ---------- 共享步骤（迭代 C） ----------
const sharedVisible = ref(false)
const sharedLoading = ref(false)
const sharedGroups = ref<SharedStepOption[]>([])
const sharedForm = ref<{
  groupId: string
  variables: { name: string; value: string; defaultValue: string }[]
}>({ groupId: '', variables: [] })
/** 编辑既有引用时记下它的 localId；为 null 表示新增 */
const sharedEditingLocalId = ref<number | null>(null)

function resetSharedForm() {
  sharedForm.value = { groupId: '', variables: [] }
  sharedEditingLocalId.value = null
}

async function loadSharedGroups() {
  if (!projectId.value) return
  sharedLoading.value = true
  try {
    // 用精简的 options 接口：列表接口改成分页后，下拉需要一次拿全
    sharedGroups.value = await sharedStepOptionsApi(projectId.value)
  } finally {
    sharedLoading.value = false
  }
}

async function openSharedPicker() {
  resetSharedForm()
  await loadSharedGroups()
  sharedVisible.value = true
}

async function openSharedOverride(row: EditableStep) {
  resetSharedForm()
  await loadSharedGroups()
  sharedEditingLocalId.value = row.localId
  sharedForm.value.groupId = row.sharedGroupId ?? ''
  await onSharedGroupChange(row.sharedVariables ?? [])
  sharedVisible.value = true
}

/** 换组或打开时重算变量行：以组内默认变量为骨架，套上已有的覆盖值 */
async function onSharedGroupChange(existing?: SharedVariableEntry[]) {
  if (!sharedForm.value.groupId) {
    sharedForm.value.variables = []
    return
  }
  const detail = await getSharedStepGroupApi(sharedForm.value.groupId)
  const overrideMap = new Map((existing ?? []).map((v) => [v.name, v.value]))
  sharedForm.value.variables = detail.variables.map((v) => ({
    name: v.name,
    // 未覆盖时留空，让后端回落到组内默认值（填成默认值会把默认值固化成覆盖）
    value: overrideMap.get(v.name) ?? '',
    defaultValue: v.value ?? '',
  }))
}

function handleInsertShared() {
  const overrides = sharedForm.value.variables
    .filter((v) => v.value !== '')
    .map((v) => ({ name: v.name, value: v.value }))
  const group = sharedGroups.value.find((g) => g.id === sharedForm.value.groupId)

  if (sharedEditingLocalId.value !== null) {
    const target = steps.value.find((s) => s.localId === sharedEditingLocalId.value)
    if (target) {
      target.sharedGroupId = sharedForm.value.groupId
      target.sharedGroupName = group?.name ?? null
      target.sharedVariables = overrides
    }
  } else {
    steps.value.push({
      id: '',
      localId: localIdSeed++,
      stepOrder: steps.value.length,
      actionType: ActionType.AIAction,
      config: {},
      sharedGroupId: sharedForm.value.groupId,
      sharedGroupName: group?.name ?? null,
      sharedVariables: overrides,
    })
  }
  sharedVisible.value = false
  ElMessage.success('共享步骤已加入，保存后生效')
}

const handleStepSave = (payload: { stepOrder: number; actionType: ActionTypeT; config: StepConfig }) => {
  if (payload.stepOrder >= 0 && editingStep.value) {
    const target = steps.value.find((s) => s.localId === editingStep.value!.localId)
    if (target) {
      target.actionType = payload.actionType
      target.config = payload.config
    }
  } else {
    steps.value.push({
      id: '',
      localId: localIdSeed++,
      stepOrder: steps.value.length,
      actionType: payload.actionType,
      config: payload.config,
    })
  }
  editingStep.value = null
}

const handleRemove = async (row: EditableStep) => {
  try {
    await ElMessageBox.confirm('确认删除该步骤？', '提示', { type: 'warning' })
  } catch {
    return
  }
  steps.value = steps.value.filter((s) => s.localId !== row.localId)
}

const handleMove = (row: EditableStep, offset: number) => {
  const index = steps.value.findIndex((s) => s.localId === row.localId)
  const target = index + offset
  if (index < 0 || target < 0 || target >= steps.value.length) return
  const arr = [...steps.value]
    ;[arr[index], arr[target]] = [arr[target], arr[index]]
  steps.value = arr
}

/** 拖拽排序：vue-draggable-plus 直接给了重排后的完整数组，赋值即可 */
const handleReorder = (reordered: EditableStep[]) => {
  steps.value = reordered
}

const handleSave = async () => {
  const payload: CreateTestStepPayload[] = steps.value.map((s, i) => ({
    stepOrder: i,
    actionType: s.actionType,
    config: s.config,
    aiInstruction: s.aiInstruction ?? null,
    aiElementDescription: s.aiElementDescription ?? null,
    // 共享步骤引用必须一并提交，否则保存后引用会丢，退化成一条空动作
    sharedGroupId: s.sharedGroupId ?? null,
    sharedVariables: s.sharedVariables ?? null,
  }))
  saving.value = true
  try {
    await updateTestCaseSteps(caseId, payload)
    ElMessage.success('步骤已保存')
    codeText.value = buildCodeJson()
    await load()
  } finally {
    saving.value = false
  }
}

const handleSaveInfo = async () => {
  if (!testCase.value) return
  savingInfo.value = true
  try {
    await updateTestCase(caseId, {
      name: testCase.value.name,
      description: testCase.value.description ?? null,
      status: testCase.value.status,
      browser: isApiType.value ? (testCase.value.browser ?? null) : browser.value,
      timeout: timeout.value,
      retryCount: retryCount.value,
      baseUrl: baseUrl.value.trim() || null,
      failFast: failFast.value,
      visualEnabled: !isApiType.value && visualEnabled.value,
      visualThreshold: Math.max(0, Math.min(1, visualThresholdPercent.value / 100)),
      visualIgnoreRegions:
        ignoreRegions.value.length > 0 ? JSON.stringify(ignoreRegions.value) : null,
      customFields: Object.keys(customFieldValues.value).length > 0
        ? JSON.stringify(Object.fromEntries(
          Object.entries(customFieldValues.value)
            .filter(([, v]) => v !== null && v !== undefined && v !== '')
            .map(([k, v]) => [k, String(v)])))
        : null,
      // API 用例不走浏览器，规则没有意义：显式发 null 清掉，避免切类型后残留一份不生效的配置
      networkRules: isApiType.value ? null : serializeNetworkRules(networkRules.value),
      dataSetId: dataSetId.value || null,
      requirementId: requirementId.value || null,
      module: moduleName.value.trim() || null,
      caseCode: caseCode.value.trim() || null,
      priority: priority.value || null,
    })
    ElMessage.success('用例信息已保存')
    await load()
  } finally {
    savingInfo.value = false
  }
}

onMounted(() => load())
</script>

<style scoped>
.browser-select,
.dataset-select {
  width: 220px;
}

.threshold-input {
  margin-left: 8px;
  width: 130px;
}

.variable-alert {
  margin-top: 8px;
}

.module-input {
  width: 170px;
}

.priority-select {
  width: 120px;
}

.page-header {
  margin-bottom: 16px;
}

.info-card {
  margin-bottom: 16px;
}

.fill-env-btn {
  margin-left: 8px;
}

.baseurl-input {
  width: 320px;
}

.info-hint {
  color: #909399;
  font-size: 13px;
}

/* ---------- 插入共享步骤弹窗 ---------- */
.shared-tip {
  margin-bottom: 14px;
}

.option-note {
  margin-left: 8px;
  color: #9aa2ae;
  font-size: 12px;
}

.empty-hint {
  color: #9aa2ae;
  font-size: 13px;
  line-height: 1.7;
}

.override-block {
  width: 100%;
}

.override-hint {
  color: #9aa2ae;
  font-size: 12px;
  margin-bottom: 8px;
}

.override-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 6px;
}

.override-name {
  width: 140px;
  font-family: "Consolas", "Menlo", monospace;
  font-size: 12.5px;
  color: var(--el-text-color-regular);
  flex-shrink: 0;
}

.save-bar {
  margin-top: 16px;
  display: flex;
  align-items: center;
  gap: 12px;
}

.save-hint {
  color: #909399;
  font-size: 13px;
}

.nlp-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.nlp-input {
  margin-top: 4px;
}

.nlp-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.nlp-hint {
  color: #909399;
  font-size: 13px;
}

.code-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.code-input :deep(textarea) {
  font-family: Consolas, Monaco, 'Courier New', monospace;
  font-size: 13px;
  line-height: 1.5;
}

.code-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.code-hint {
  color: #909399;
  font-size: 12px;
  line-height: 1.6;
}

.comments-card {
  margin-top: 15px;
}

.review-card {
  margin-top: 15px;
}
</style>
