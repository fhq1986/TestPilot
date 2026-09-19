<template>
  <el-dialog v-model="visible" title="导入测试用例（Excel）" width="80%" @closed="resetImport">
    <el-form label-width="110px">
      <el-form-item label="所属项目" required>
        <el-select v-model="importForm.projectId" placeholder="选择项目" filterable class="w-full">
          <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
      </el-form-item>
      <el-form-item label="Excel 文件" required>
        <div class="import-file-row">
          <el-upload ref="uploadRef" :auto-upload="false" :limit="1" accept=".xlsx" :on-change="onImportFileChange"
            :on-remove="() => (importFile = null)">
            <el-button :icon="Upload">选择文件</el-button>
          </el-upload>
          <el-button class="template-btn" type="primary" plain :icon="Download" :loading="downloadingTemplate"
            @click="handleDownloadTemplate">下载导入模板</el-button>
        </div>
        <div class="el-upload__tip">
          <el-alert type="warning" :closable="false" class="script-tip" title="仅支持 .xlsx；模板要求：每个模块一个工作表，表头含「用例编号/测试场景/操作步骤/预期结果/优先级/类别」。
            首次使用建议先下载模板，按示例整理后上传。" />
        </div>
      </el-form-item>
      <el-form-item label="AI 生成步骤">
        <el-switch v-model="importForm.useAi" />
        <span class="hint">开启后调用 AI Worker 把「操作步骤+预期结果」转成可执行步骤（用例较多时耗时较长，失败的行按基础信息导入）</span>
      </el-form-item>
      <el-form-item label="被测地址">
        <el-input v-model="importForm.baseUrl" placeholder="http://localhost:3000（可选，作为步骤默认基地址）" />
      </el-form-item>
      <el-form-item label="同名编号">
        <el-radio-group v-model="importForm.overwrite">
          <el-radio :value="false">跳过</el-radio>
          <el-radio :value="true">覆盖更新</el-radio>
        </el-radio-group>
        <span class="hint">以「模块 + 用例编号」为去重键</span>
      </el-form-item>
    </el-form>

    <div v-if="importResult" class="import-result">
      <el-alert :type="importResult.failed > 0 ? 'warning' : 'success'" :closable="false" show-icon
        :title="`共 ${importResult.totalRows} 行：新增 ${importResult.imported}，更新 ${importResult.updated}，跳过 ${importResult.skipped}，失败 ${importResult.failed}`" />
      <p v-if="importForm.useAi" class="hint">
        AI 已生成可执行步骤 {{ importResult.aiParsed }} / {{ importResult.aiCaseCount }} 条
      </p>
      <el-table :data="importResult.modules" size="small" max-height="220" class="result-table">
        <el-table-column prop="module" label="模块" min-width="160" show-overflow-tooltip />
        <el-table-column prop="total" label="行数" width="70" />
        <el-table-column prop="imported" label="导入" width="70" />
        <el-table-column prop="skipped" label="跳过" width="70" />
        <el-table-column prop="failed" label="失败" width="70" />
      </el-table>
      <el-alert v-for="(w, i) in importResult.warnings" :key="`w${i}`" type="info" :closable="false"
        class="result-line" :title="w" />
      <el-alert v-for="(e, i) in importResult.errors.slice(0, 5)" :key="`e${i}`" type="error" :closable="false"
        class="result-line" :title="`${e.module} 第 ${e.rowNumber} 行：${e.message}`" />
    </div>

    <template #footer>
      <el-button @click="visible = false">关闭</el-button>
      <el-button type="primary" :loading="importing" @click="handleImport">
        {{ importing ? '导入中（AI 解析可能需要数分钟）' : '开始导入' }}
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { ElMessage, type UploadFile, type UploadInstance } from 'element-plus'
import { Download, Upload } from '@element-plus/icons-vue'
import { downloadImportTemplate, importTestCases } from '@/api/testcase'
import { saveBlobAsFile } from '@/api/report'
import type { TestCaseImportResult } from '@/types/testcase'
import type { Project } from '@/types/project'

const props = defineProps<{
  projectOptions: Project[]
  /** 当前项目筛选：打开弹窗时的默认所属项目 */
  projectId: string
}>()

/** 导入成功后通知父级刷新（弹窗保持打开展示逐模块结果） */
const emit = defineEmits<{ imported: [projectId: string] }>()

const visible = ref(false)
const importing = ref(false)
const importFile = ref<File | null>(null)
const uploadRef = ref<UploadInstance>()
const importResult = ref<TestCaseImportResult | null>(null)
const importForm = reactive({
  projectId: '',
  useAi: true,
  overwrite: false,
  baseUrl: '',
})

const downloadingTemplate = ref(false)

const open = () => {
  importForm.projectId = props.projectId || props.projectOptions[0]?.id || ''
  visible.value = true
}

/** 下载导入模板（含填写说明与示例模块） */
const handleDownloadTemplate = async () => {
  downloadingTemplate.value = true
  try {
    const blob = await downloadImportTemplate()
    saveBlobAsFile(blob, '测试用例导入模板.xlsx')
    ElMessage.success('模板已开始下载')
  } finally {
    downloadingTemplate.value = false
  }
}

const onImportFileChange = (file: UploadFile) => {
  importFile.value = (file.raw as File) ?? null
}

const resetImport = () => {
  importFile.value = null
  importResult.value = null
  uploadRef.value?.clearFiles()
}

const handleImport = async () => {
  if (!importForm.projectId) {
    ElMessage.warning('请选择所属项目')
    return
  }
  if (!importFile.value) {
    ElMessage.warning('请选择要导入的 Excel 文件')
    return
  }
  const form = new FormData()
  form.append('file', importFile.value)
  form.append('projectId', importForm.projectId)
  form.append('useAi', String(importForm.useAi))
  form.append('overwrite', String(importForm.overwrite))
  if (importForm.baseUrl) form.append('baseUrl', importForm.baseUrl)

  importing.value = true
  try {
    importResult.value = await importTestCases(form)
    ElMessage.success(
      `导入完成：新增 ${importResult.value.imported}，更新 ${importResult.value.updated}，跳过 ${importResult.value.skipped}`,
    )
    emit('imported', importForm.projectId)
  } finally {
    importing.value = false
  }
}

defineExpose({ open })
</script>

<style scoped>
.w-full {
  width: 100%;
}

.hint {
  margin-left: 8px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.script-tip {
  margin-bottom: 12px;
}

.import-result {
  margin-top: 8px;
}

/* 注意：el-upload 容器内含「已选文件列表」，整体比按钮高；
   若用 align-items:center 会让右侧按钮按容器高度居中而显得偏低，故顶部对齐并固定按钮高度 */
.import-file-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

.import-file-row :deep(.el-upload) {
  display: flex;
  align-items: center;
}

.import-file-row .template-btn {
  height: 32px;
  margin-top: 0;
}

.result-table {
  margin: 10px 0;
}

.result-line {
  margin-top: 6px;
}
</style>
