<template>
  <el-dialog v-model="visible" title="从 Playwright 脚本导入用例" width="80%" destroy-on-close top="6vh">
    <el-alert type="warning" :closable="false" class="script-tip"
      title="把 codegen 生成的脚本（或手写脚本）粘贴进来即可转成平台步骤；语义定位（getByRole/getByText 等）会自动转成 AI 定位（描述式），运行时可自愈。" />

    <el-form label-width="90px">
      <el-form-item label="脚本内容">
        <el-input v-model="scriptText" type="textarea" :rows="15" spellcheck="false"
          placeholder="npx playwright codegen http://localhost:3000  →  复制生成的脚本粘贴到这里" />
        <div class="script-actions">
          <el-button size="small" :loading="parsing" @click="handleParseScript">解析预览</el-button>
          <span v-if="parseResult" class="script-summary">
            {{ parseResult.steps.length }} 个步骤
            <template v-if="parseResult.suggestedName">｜建议名称：{{ parseResult.suggestedName }}</template>
            <template v-if="parseResult.suggestedBaseUrl">｜基地址：{{ parseResult.suggestedBaseUrl }}</template>
          </span>
        </div>
      </el-form-item>

      <div v-if="parseResult && parseResult.warnings.length > 0" class="script-warnings">
        <el-alert v-for="(w, i) in parseResult.warnings.slice(0, 6)" :key="i" type="warning" :closable="false"
          class="script-warning" :title="w" />
        <div v-if="parseResult.warnings.length > 6" class="script-summary">
          还有 {{ parseResult.warnings.length - 6 }} 条未映射的行（已跳过）
        </div>
      </div>

      <el-table v-if="parseResult && parseResult.steps.length > 0" :data="parseResult.steps" size="small"
        max-height="220" class="script-preview">
        <el-table-column label="#" width="50" prop="stepOrder" />
        <el-table-column label="动作" width="120">
          <template #default="{ row }">{{ ACTION_TYPE_LABELS[row.actionType] ?? row.actionType }}</template>
        </el-table-column>
        <el-table-column label="识别到的目标" min-width="240">
          <template #default="{ row }">
            <span>{{ row.description || row.config?.url || row.config?.endpoint || '—' }}</span>
            <el-tag v-if="row.note" size="small" type="warning" class="script-note">{{ row.note }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="原始脚本行" min-width="240" show-overflow-tooltip prop="sourceLine" />
      </el-table>

      <div class="script-form">
        <el-form-item label="所属项目">
          <el-select v-model="scriptForm.projectId" placeholder="选择项目" filterable>
            <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="用例名称">
          <el-input v-model="scriptForm.name" placeholder="留空则用脚本里的 test 标题" maxlength="200" />
        </el-form-item>
        <el-form-item label="模块">
          <el-input v-model="scriptForm.module" placeholder="可选" maxlength="100" />
        </el-form-item>
        <el-form-item label="优先级">
          <el-select v-model="scriptForm.priority" placeholder="未设置" clearable>
            <el-option v-for="p in PRIORITY_OPTIONS" :key="p" :label="p" :value="p" />
          </el-select>
        </el-form-item>
        <el-form-item label="BaseUrl">
          <el-input v-model="scriptForm.baseUrl" placeholder="留空则用脚本里第一个绝对 URL" />
        </el-form-item>
      </div>
    </el-form>

    <template #footer>
      <el-button @click="visible = false">关闭</el-button>
      <el-button type="primary" :loading="importingScript" :disabled="!parseResult || parseResult.steps.length === 0"
        @click="handleImportScript">
        导入为用例
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { importScript, parseScript, type ScriptParseResult } from '@/api/script'
import { ACTION_TYPE_LABELS } from '@/types/testcase'
import type { Project } from '@/types/project'

const props = defineProps<{
  projectOptions: Project[]
  /** 当前项目筛选：打开弹窗时的默认所属项目 */
  projectId: string
  /** 当前模块筛选：打开弹窗时的默认模块 */
  defaultModule: string
}>()

/** 导入成功后通知父级刷新列表 */
const emit = defineEmits<{ imported: [projectId: string] }>()

const PRIORITY_OPTIONS = ['P0', 'P1', 'P2', 'P3']

const visible = ref(false)
const scriptText = ref('')
const parsing = ref(false)
const importingScript = ref(false)
const parseResult = ref<ScriptParseResult | null>(null)
const scriptForm = reactive({
  projectId: '',
  name: '',
  module: '',
  priority: '',
  baseUrl: '',
})

const open = () => {
  scriptText.value = ''
  parseResult.value = null
  scriptForm.projectId = props.projectId || props.projectOptions[0]?.id || ''
  scriptForm.name = ''
  scriptForm.module = props.defaultModule || ''
  scriptForm.priority = ''
  scriptForm.baseUrl = ''
  visible.value = true
}

const handleParseScript = async () => {
  if (scriptText.value.trim().length === 0) {
    ElMessage.warning('请先粘贴 Playwright 脚本')
    return
  }
  parsing.value = true
  try {
    parseResult.value = await parseScript(scriptText.value)
    if (parseResult.value.suggestedName && !scriptForm.name) scriptForm.name = parseResult.value.suggestedName
    if (parseResult.value.suggestedBaseUrl && !scriptForm.baseUrl) scriptForm.baseUrl = parseResult.value.suggestedBaseUrl
    ElMessage.success(`解析出 ${parseResult.value.steps.length} 个步骤`)
  } catch {
    parseResult.value = null
  } finally {
    parsing.value = false
  }
}

const handleImportScript = async () => {
  if (!parseResult.value) return
  if (!scriptForm.projectId) {
    ElMessage.warning('请选择所属项目')
    return
  }
  importingScript.value = true
  try {
    const result = await importScript({
      projectId: scriptForm.projectId,
      name: scriptForm.name.trim() || parseResult.value.suggestedName || '导入的用例',
      script: scriptText.value,
      module: scriptForm.module.trim() || null,
      priority: scriptForm.priority || null,
      baseUrl: scriptForm.baseUrl.trim() || null,
    })
    ElMessage.success(`已创建用例「${result.name}」（${result.stepCount} 个步骤）`)
    visible.value = false
    emit('imported', scriptForm.projectId)
  } finally {
    importingScript.value = false
  }
}

defineExpose({ open })
</script>

<style scoped>
.script-tip {
  margin-bottom: 12px;
}

.script-actions {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 6px;
}

.script-summary {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.script-warnings {
  margin-bottom: 8px;
}

.script-warning {
  margin-bottom: 4px;
}

.script-note {
  margin-left: 6px;
}

.script-preview {
  margin-bottom: 12px;
}

.script-form {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0 16px;
}

/* 窄屏：表单改单列（弹窗在 375px 下只有 ~345px 宽，两栏各剩 160px 不够填） */
@media (max-width: 767px) {
  .script-form {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
