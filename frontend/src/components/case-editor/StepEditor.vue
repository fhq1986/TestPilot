<template>
  <el-dialog :model-value="visible" :title="step ? '编辑步骤' : '添加步骤'" width="520px"
    @update:model-value="$emit('update:visible', $event)" @closed="resetForm">
    <el-form ref="formRef" :model="form" :rules="rules" label-width="100px">
      <el-form-item label="动作" prop="actionType">
        <el-select v-model="form.actionType" class="w-full" @change="resetConfig">
          <el-option v-for="item in editableActions" :key="item.value" :label="item.label" :value="item.value" />
        </el-select>
      </el-form-item>

      <template v-if="needsSelector">
        <el-form-item label="定位方式" prop="selectorType">
          <el-select v-model="form.selectorType" class="w-full">
            <el-option label="CSS 选择器" value="css" />
            <el-option label="XPath" value="xpath" />
            <el-option label="AI 定位（按元素描述）" value="ai" />
          </el-select>
        </el-form-item>
        <el-form-item v-if="form.selectorType === 'ai'" label="元素描述" prop="selectorDescription">
          <el-input v-model="form.selectorDescription"
            placeholder="例如：蓝色的提交按钮 / 右上角的登录链接（执行时由 AI 定位并缓存选择器）" />
        </el-form-item>
        <el-form-item v-else label="选择器值" prop="selectorValue">
          <el-input v-model="form.selectorValue" placeholder="#id / .class / //div[...]" />
        </el-form-item>
      </template>

      <el-form-item v-if="needsUrl" label="URL" prop="url">
        <el-input v-model="form.url" placeholder="https://example.com 或 file:///..." />
      </el-form-item>

      <el-form-item v-if="needsMethod" label="请求方法" prop="method">
        <el-select v-model="form.method" class="w-full">
          <el-option v-for="m in httpMethods" :key="m" :label="m" :value="m" />
        </el-select>
      </el-form-item>

      <el-form-item v-if="needsEndpoint" :label="endpointLabel" prop="endpoint">
        <el-input v-model="form.endpoint" :placeholder="endpointPlaceholder" />
      </el-form-item>

      <el-form-item v-if="needsValue" :label="valueLabel" prop="value">
        <el-input v-model="form.value" :placeholder="valuePlaceholder" />
      </el-form-item>

      <!-- 属性名单独一项：与"期望值"是两件事，挤在一个输入框里得自己发明分隔符 -->
      <el-form-item v-if="needsAttribute" label="属性名" prop="attribute">
        <el-input v-model="form.attribute" :placeholder="attributePlaceholder" />
      </el-form-item>

      <el-form-item v-if="needsBody" :label="bodyLabel" prop="body">
        <el-input v-model="form.body" type="textarea" :rows="4" :placeholder="bodyPlaceholder" />
      </el-form-item>

      <el-form-item v-if="needsHeaders" label="请求头" prop="headers">
        <el-input v-model="form.headers" type="textarea" :rows="3" placeholder='{"X-Token":"{token}"}' />
      </el-form-item>

      <el-form-item v-if="form.actionType === ActionType.Wait" label="等待方式" prop="waitMode">
        <el-radio-group v-model="form.waitMode">
          <el-radio value="selector">等待元素出现</el-radio>
          <el-radio value="ms">固定毫秒数</el-radio>
        </el-radio-group>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="$emit('update:visible', false)">取消</el-button>
      <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import { ActionType, type StepConfig, type SelectorConfig, type HeaderEntry } from '@/types/testcase'

const props = defineProps<{
  visible: boolean
  step: { stepOrder: number; actionType: ActionType; config: StepConfig } | null
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
  save: [payload: { stepOrder: number; actionType: ActionType; config: StepConfig }]
}>()

const formRef = ref<FormInstance>()
const saving = ref(false)

const editableActions = [
  { value: ActionType.Navigate, label: '打开页面 (Navigate)' },
  { value: ActionType.Fill, label: '输入文本 (Fill)' },
  { value: ActionType.Click, label: '点击元素 (Click)' },
  { value: ActionType.Wait, label: '等待 (Wait)' },
  { value: ActionType.Scroll, label: '滚动 (Scroll)' },
  { value: ActionType.Screenshot, label: '截图 (Screenshot)' },
  { value: ActionType.AssertVisible, label: '断言元素可见 (AssertVisible)' },
  { value: ActionType.AssertText, label: '断言文本 (AssertText)' },
  { value: ActionType.AssertUrl, label: '断言 URL (AssertUrl)' },
  { value: ActionType.AssertTitle, label: '断言页面标题 (AssertTitle)' },
  { value: ActionType.AssertA11y, label: '无障碍扫描 (AssertA11y)' },
  { value: ActionType.Request, label: '接口请求 (Request)' },
  { value: ActionType.AssertResponse, label: '响应断言 (AssertResponse)' },
  { value: ActionType.ExtractVariable, label: '变量提取 (ExtractVariable)' },
  // 迭代 F：确定性交互与断言。此前这些只能靠 AIAction/AIAssert 兜底，
  // 而 AI 不确定、慢、按调用计费——确定性的事就该用确定性动作
  { value: ActionType.Select, label: '选择下拉项 (Select)' },
  { value: ActionType.UploadFile, label: '上传文件 (UploadFile)' },
  { value: ActionType.PressKey, label: '按键 (PressKey)' },
  { value: ActionType.Hover, label: '悬浮 (Hover)' },
  { value: ActionType.AssertAttribute, label: '断言元素属性 (AssertAttribute)' },
  { value: ActionType.AssertCount, label: '断言元素个数 (AssertCount)' },
  { value: ActionType.AssertValue, label: '断言输入值 (AssertValue)' },
  { value: ActionType.AssertState, label: '断言元素状态 (AssertState)' },
]

const httpMethods = ['GET', 'POST', 'PUT', 'DELETE']

const form = reactive({
  actionType: ActionType.Navigate,
  selectorType: 'css',
  selectorValue: '',
  selectorDescription: '',
  url: '',
  method: 'GET',
  endpoint: '',
  value: '',
  attribute: '',
  body: '',
  headers: '',
  waitMode: 'selector',
})

const needsSelector = computed(() =>
  [ActionType.Fill, ActionType.Click, ActionType.Scroll, ActionType.AssertVisible, ActionType.AssertText,
    ActionType.Select, ActionType.UploadFile, ActionType.PressKey, ActionType.Hover,
    ActionType.AssertAttribute, ActionType.AssertCount, ActionType.AssertValue, ActionType.AssertState]
    .includes(form.actionType) ||
  (form.actionType === ActionType.Wait && form.waitMode === 'selector'),
)

const needsUrl = computed(() => form.actionType === ActionType.Navigate)

const needsMethod = computed(() => form.actionType === ActionType.Request)

const needsEndpoint = computed(() =>
  [ActionType.Request, ActionType.ExtractVariable].includes(form.actionType),
)

const needsBody = computed(() =>
  [ActionType.Request, ActionType.AssertResponse].includes(form.actionType),
)

const needsHeaders = computed(() => form.actionType === ActionType.Request)

const needsValue = computed(() =>
  [ActionType.Fill, ActionType.AssertText, ActionType.AssertResponse, ActionType.ExtractVariable,
    ActionType.AssertUrl, ActionType.AssertTitle, ActionType.AssertA11y,
    ActionType.Select, ActionType.UploadFile, ActionType.PressKey,
    ActionType.AssertAttribute, ActionType.AssertCount, ActionType.AssertValue, ActionType.AssertState]
    .includes(form.actionType) ||
  (form.actionType === ActionType.Wait && form.waitMode === 'ms'),
)

/** AssertAttribute 独有的「属性名」输入框 */
const needsAttribute = computed(() => form.actionType === ActionType.AssertAttribute)

/** 选择器是否必填：PressKey 允许不填（不填就发给当前焦点，用于 Escape 关弹窗这类场景） */
const selectorRequired = computed(() =>
  needsSelector.value && form.actionType !== ActionType.PressKey,
)

const valueLabel = computed(() => {
  if (form.actionType === ActionType.Fill) return '输入文本'
  if (form.actionType === ActionType.AssertText) return '期望文本'
  if (form.actionType === ActionType.AssertResponse) return '期望状态码'
  if (form.actionType === ActionType.ExtractVariable) return '变量名'
  if (form.actionType === ActionType.AssertUrl) return '期望 URL 片段'
  if (form.actionType === ActionType.AssertTitle) return '期望标题片段'
  if (form.actionType === ActionType.AssertA11y) return '最低拦截级别'
  if (form.actionType === ActionType.Select) return '要选中的项'
  if (form.actionType === ActionType.UploadFile) return '文件路径'
  if (form.actionType === ActionType.PressKey) return '按键名'
  if (form.actionType === ActionType.AssertAttribute) return '期望属性值'
  if (form.actionType === ActionType.AssertCount) return '期望个数'
  if (form.actionType === ActionType.AssertValue) return '期望输入值'
  if (form.actionType === ActionType.AssertState) return '期望状态'
  return '毫秒数'
})

const valuePlaceholder = computed(() => {
  if (form.actionType === ActionType.AssertText) return '期望文本包含的内容'
  if (form.actionType === ActionType.AssertResponse) return '例如 200、2xx、404'
  if (form.actionType === ActionType.ExtractVariable) return '变量名，后续步骤可用 {变量名} 引用'
  if (form.actionType === ActionType.AssertUrl) return '期望当前 URL 包含的片段，例如 /dashboard'
  if (form.actionType === ActionType.AssertTitle) return '期望页面标题包含的片段，例如 工作台'
  if (form.actionType === ActionType.AssertA11y) {
    return '留空 = serious（只拦严重及以上）；可选 minor / moderate / serious / critical / none'
  }
  if (form.actionType === ActionType.Select) return '下拉项的 value 或显示文本，例如 approved'
  if (form.actionType === ActionType.UploadFile) {
    return '执行机上的文件绝对路径；多个用换行或分号分隔，例如 D:\\data\\a.xlsx'
  }
  if (form.actionType === ActionType.PressKey) return '例如 Enter、Escape、Tab、Control+A'
  if (form.actionType === ActionType.AssertAttribute) return '期望属性值包含的片段'
  if (form.actionType === ActionType.AssertCount) return '例如 3'
  if (form.actionType === ActionType.AssertValue) return '期望输入框当前值包含的片段'
  if (form.actionType === ActionType.AssertState) {
    return '可见 visible / 隐藏 hidden / 可用 enabled / 禁用 disabled / 已勾选 checked / 未勾选 unchecked / 可编辑 editable / 只读 readonly'
  }
  if (form.actionType === ActionType.Wait) return '例如 1500'
  return '输入内容'
})

/** 选择器值是否必填要跟着动作走：PressKey 不填表示"发给当前焦点" */
const rules = computed<FormRules>(() => ({
  actionType: [{ required: true, message: '请选择动作', trigger: 'change' }],
  url: [{ required: true, message: '请输入 URL', trigger: 'blur' }],
  selectorValue: selectorRequired.value
    ? [{ required: true, message: '请输入选择器值', trigger: 'blur' }]
    : [],
  method: [{ required: true, message: '请选择请求方法', trigger: 'change' }],
  endpoint: [{ required: true, message: '请输入请求路径', trigger: 'blur' }],
  value: [{ required: true, message: '请输入值', trigger: 'blur' }],
  attribute: [{ required: true, message: '请输入属性名', trigger: 'blur' }],
}))

const attributePlaceholder = '例如 disabled、aria-label、href、data-status'

const endpointLabel = computed(() =>
  form.actionType === ActionType.ExtractVariable ? 'JSONPath' : '请求路径',
)

const endpointPlaceholder = computed(() =>
  form.actionType === ActionType.ExtractVariable ? '例如 $.data.token' : '例如 /users/{id}（相对 BaseUrl 或绝对 URL）',
)

const bodyLabel = computed(() =>
  form.actionType === ActionType.AssertResponse ? '期望 JSON 片段' : '请求体',
)

const bodyPlaceholder = computed(() =>
  form.actionType === ActionType.AssertResponse ? '例如 {"data":{"name":"张三"}}（子集匹配）' : 'JSON 字符串，支持 {变量名} 替换',
)

// rules 已改为计算属性（见上方），因为「选择器是否必填」要跟着动作类型变：
// PressKey 不填选择器表示"发给当前焦点"，用固定必填会把它挡在保存之外。


watch(
  () => props.visible,
  (visible) => {
    if (!visible) return
    if (props.step) {
      const cfg = props.step.config ?? {}
      form.actionType = props.step.actionType
      form.selectorType = cfg.selector?.type === 'xpath' ? 'xpath' : cfg.selector?.type === 'ai' ? 'ai' : 'css'
      form.selectorValue = cfg.selector?.value ?? ''
      form.selectorDescription = cfg.selector?.description ?? ''
      form.url = cfg.url ?? ''
      form.method = cfg.method ?? 'GET'
      form.endpoint = cfg.endpoint ?? ''
      form.value = cfg.value ?? ''
      form.attribute = cfg.attribute ?? ''
      form.body = cfg.body ?? ''
      form.headers = headersToJson(cfg.headers)
      form.waitMode = cfg.selector?.value ? 'selector' : 'ms'
    } else {
      resetForm()
    }
  },
)

const resetConfig = () => {
  form.selectorType = 'css'
  form.selectorValue = ''
  form.selectorDescription = ''
  form.url = ''
  form.method = 'GET'
  form.endpoint = ''
  form.value = ''
  form.attribute = ''
  form.body = ''
  form.headers = ''
  form.waitMode = 'selector'
}

const headersToJson = (headers?: HeaderEntry[] | null) => {
  if (!headers?.length) return ''
  const obj: Record<string, string> = {}
  for (const entry of headers) obj[entry.name] = entry.value
  return JSON.stringify(obj, null, 2)
}

const resetForm = () => {
  resetConfig()
  form.actionType = ActionType.Navigate
  formRef.value?.clearValidate()
}

const handleSave = async () => {
  try {
    await formRef.value?.validate()
  } catch {
    return
  }
  saving.value = true
  try {
    if (needsSelector.value && form.selectorType === 'ai' && !form.selectorDescription.trim()) {
      ElMessage.warning('AI 定位需要填写元素描述，例如「蓝色的提交按钮」')
      return
    }
    const selector: SelectorConfig | null =
      needsSelector.value && (form.selectorType === 'ai' || form.selectorValue.trim())
        ? {
            type: form.selectorType,
            value: form.selectorType === 'ai' ? null : form.selectorValue,
            description: form.selectorType === 'ai' ? form.selectorDescription.trim() : null,
          }
        : null
    const config: StepConfig = {}
    if (needsUrl.value) config.url = form.url
    if (selector) config.selector = selector
    if (needsValue.value) config.value = form.value
    if (needsAttribute.value) config.attribute = form.attribute.trim()
    if (form.actionType === ActionType.Wait && form.waitMode === 'selector') config.value = undefined
    if (form.actionType === ActionType.Request) {
      config.method = form.method
      config.endpoint = form.endpoint
      if (form.body.trim()) config.body = form.body
      if (form.headers.trim()) {
        let parsed: unknown
        try {
          parsed = JSON.parse(form.headers)
        } catch {
          ElMessage.warning('请求头必须是 JSON 对象，例如 {"X-Token":"{token}"}')
          return
        }
        if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
          ElMessage.warning('请求头必须是 JSON 对象，例如 {"X-Token":"{token}"}')
          return
        }
        config.headers = Object.entries(parsed as Record<string, unknown>).map(([name, value]) => ({
          name,
          value: String(value),
        }))
      }
    }
    if (form.actionType === ActionType.AssertResponse && form.body.trim()) config.body = form.body
    if (form.actionType === ActionType.ExtractVariable) config.endpoint = form.endpoint

    emit('save', {
      stepOrder: props.step?.stepOrder ?? -1,
      actionType: form.actionType,
      config,
    })
    emit('update:visible', false)
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.w-full {
  width: 100%;
}
</style>
