<template>
  <el-card class="custom-field-card">
    <template #header>
      <div class="card-header">
        <span>扩展字段（{{ fields.length }}）</span>
        <el-button type="primary" size="small" :icon="Plus" @click="openCreate">新建字段</el-button>
      </div>
    </template>

    <el-alert type="warning" :closable="false" class="usage-hint">
      <template #title>
        扩展字段会在本项目的用例编辑页动态出现（如「关联需求单号」「所属客户」），用于记录团队自定义的信息。
        删除字段后，用例中已填写的值保留在数据里但不再展示。
      </template>
    </el-alert>

    <el-table v-loading="loading" :data="fields" row-key="id" size="small">
      <el-table-column prop="name" label="字段名" min-width="150" show-overflow-tooltip />
      <el-table-column label="类型" width="100">
        <template #default="{ row }">{{ TYPE_LABELS[row.fieldType] ?? '文本' }}</template>
      </el-table-column>
      <el-table-column label="候选值" min-width="160">
        <template #default="{ row }">
          <span v-if="row.fieldType === 3" class="options">{{ row.options }}</span>
          <span v-else>—</span>
        </template>
      </el-table-column>
      <el-table-column label="创建时间" width="160">
        <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="90">
        <template #default="{ row }">
          <el-button link type="danger" size="small" @click="handleDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
    <!-- <el-empty v-if="!loading && fields.length === 0" description="还没有扩展字段" :image-size="60" /> -->

    <el-dialog v-model="createVisible" title="新建扩展字段" width="480px" @closed="resetForm">
      <el-form ref="formRef" :model="form" :rules="rules" label-position="top">
        <el-form-item label="字段名" prop="name">
          <el-input v-model="form.name" placeholder="例如 关联需求单号" maxlength="50" />
        </el-form-item>
        <el-form-item label="类型">
          <el-radio-group v-model="form.fieldType">
            <el-radio-button :value="0">文本</el-radio-button>
            <el-radio-button :value="1">数字</el-radio-button>
            <el-radio-button :value="2">日期</el-radio-button>
            <el-radio-button :value="3">下拉</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="form.fieldType === 3" label="候选值（JSON 字符串数组）" prop="options">
          <el-input v-model="form.options" placeholder='["内部","外部"]' />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleCreate">创建</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { createCustomField, deleteCustomField, listCustomFields, type CustomFieldDef } from '@/api/testcase'
import { formatDateTime } from '@/utils/formatter'

const props = defineProps<{ projectId: string }>()

const TYPE_LABELS: Record<number, string> = { 0: '文本', 1: '数字', 2: '日期', 3: '下拉' }

const fields = ref<CustomFieldDef[]>([])
const loading = ref(false)
const saving = ref(false)
const createVisible = ref(false)
const formRef = ref<FormInstance>()
const form = reactive<{ name: string; fieldType: number; options: string }>({
  name: '', fieldType: 0, options: '',
})
const rules = {
  name: [{ required: true, message: '请填写字段名', trigger: 'blur' }],
}

const load = async () => {
  loading.value = true
  try {
    fields.value = await listCustomFields(props.projectId)
  } finally {
    loading.value = false
  }
}

const openCreate = () => { createVisible.value = true }
const resetForm = () => {
  form.name = ''
  form.fieldType = 0
  form.options = ''
  formRef.value?.clearValidate()
}

const handleCreate = async () => {
  await formRef.value?.validate().catch(() => Promise.reject())
  saving.value = true
  try {
    await createCustomField(props.projectId, {
      name: form.name.trim(),
      fieldType: form.fieldType,
      options: form.fieldType === 3 ? form.options : undefined,
    })
    ElMessage.success('已创建')
    createVisible.value = false
    await load()
  } finally {
    saving.value = false
  }
}

const handleDelete = async (row: CustomFieldDef) => {
  await ElMessageBox.confirm(
    `删除后本项目的用例编辑页不再出现「${row.name}」，已填写的值保留在数据里。确定删除？`,
    '删除扩展字段', { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' })
  await deleteCustomField(props.projectId, row.id)
  ElMessage.success('已删除')
  await load()
}

onMounted(load)
</script>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.usage-hint {
  margin-bottom: 12px;
}

.options {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
</style>
