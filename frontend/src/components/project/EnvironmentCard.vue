<template>
  <el-card class="environment-card">
    <template #header>
      <div class="card-header">
        <span>测试环境（{{ environments.length }}）</span>
        <el-button type="primary" size="small" :icon="Plus" @click="openCreate">新建环境</el-button>
      </div>
    </template>

    <el-table v-loading="loading" :data="environments" row-key="id" size="small">
      <el-table-column prop="name" label="名称" min-width="140" show-overflow-tooltip />
      <el-table-column label="BaseUrl" min-width="220">
        <template #default="{ row }">
          <span class="base-url">{{ row.baseUrl }}</span>
        </template>
      </el-table-column>
      <el-table-column label="自动登录" width="100">
        <template #default="{ row }">
          <el-tag v-if="row.autoLogin" type="success" size="small">自动登录</el-tag>
          <span v-else>—</span>
        </template>
      </el-table-column>
      <el-table-column label="更新时间" width="180">
        <template #default="{ row }">{{ formatDateTime(row.updatedAt) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="140">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click="openEdit(row)">编辑</el-button>
          <el-button link type="danger" size="small" @click="handleDelete(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-empty v-if="!loading && environments.length === 0" description="暂无环境配置" :image-size="60" />

    <el-dialog v-model="dialogVisible" :title="editing ? '编辑环境' : '新建环境'" width="640px" @closed="resetForm">
      <el-form ref="formRef" :model="form" :rules="rules" label-position="top">
        <el-form-item label="环境名称" prop="name">
          <el-input v-model="form.name" placeholder="例如 测试环境 / 预发布" maxlength="100" />
        </el-form-item>
        <el-form-item label="被测系统地址（Base URL）" prop="baseUrl">
          <el-input v-model="form.baseUrl" placeholder="https://example.com" maxlength="500" />
        </el-form-item>
        <el-form-item label="登录页地址（可相对 BaseUrl）" prop="loginUrl">
          <el-input v-model="form.loginUrl" placeholder="/login" maxlength="500" />
        </el-form-item>
        <el-form-item label="登录账号" prop="loginUsername">
          <el-input v-model="form.loginUsername" maxlength="200" />
        </el-form-item>
        <el-form-item label="登录密码" prop="loginPassword">
          <el-input v-model="form.loginPassword" type="password" show-password autocomplete="new-password"
            :placeholder="passwordPlaceholder" maxlength="200" />
        </el-form-item>
        <el-form-item label="登录成功标志（AI 智能断言，留空跳过验证）" prop="loginSuccessIndicator">
          <el-input v-model="form.loginSuccessIndicator" placeholder="例如 页面出现「欢迎」文本" maxlength="300" />
        </el-form-item>
        <el-form-item label="默认浏览器">
          <el-select v-model="form.browser" placeholder="跟随用例配置" clearable class="browser-select">
            <el-option label="Chromium（Blink）" value="chromium" />
            <el-option label="Firefox（Gecko）" value="firefox" />
            <el-option label="WebKit（Safari 内核）" value="webkit" />
          </el-select>
          <div class="browser-hint">该环境下的执行统一使用此浏览器；留空则按用例自身配置</div>
        </el-form-item>
        <el-form-item label="自动登录">
          <el-switch v-model="form.autoLogin" />
          <span class="form-hint">执行 Web 用例时自动完成登录</span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { createEnvironment, deleteEnvironment, getEnvironments, updateEnvironment } from '@/api/environment'
import type { EnvironmentView } from '@/types/environment'
import { formatDateTime } from '@/utils/formatter'

const props = defineProps<{ projectId: string }>()

const emit = defineEmits<{
  refresh: []
}>()

const environments = ref<EnvironmentView[]>([])
const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editing = ref<EnvironmentView | null>(null)
const formRef = ref<FormInstance>()

const form = reactive({
  name: '',
  baseUrl: '',
  loginUrl: '',
  loginUsername: '',
  loginPassword: '',
  loginSuccessIndicator: '',
  autoLogin: true,
  browser: '',
})

const rules: FormRules = {
  name: [{ required: true, message: '请输入环境名称', trigger: 'blur' }],
  baseUrl: [
    { required: true, message: '请输入被测系统地址', trigger: 'blur' },
    { pattern: /^https?:\/\/.+/i, message: '需以 http:// 或 https:// 开头', trigger: 'blur' },
  ],
}

const passwordPlaceholder = computed(() => {
  if (!editing.value) return '未配置'
  return editing.value.hasLoginPassword
    ? `已配置（${editing.value.loginPasswordMasked}），留空保持不变`
    : '未配置'
})

const load = async () => {
  loading.value = true
  try {
    environments.value = await getEnvironments(props.projectId)
  } finally {
    loading.value = false
  }
}

const openCreate = () => {
  editing.value = null
  dialogVisible.value = true
}

const openEdit = (row: EnvironmentView) => {
  editing.value = row
  form.name = row.name
  form.baseUrl = row.baseUrl
  form.loginUrl = row.loginUrl ?? ''
  form.loginUsername = row.loginUsername ?? ''
  form.loginPassword = ''
  form.loginSuccessIndicator = row.loginSuccessIndicator ?? ''
  form.browser = row.browser ?? ''
  form.autoLogin = row.autoLogin
  dialogVisible.value = true
}

const resetForm = () => {
  form.name = ''
  form.baseUrl = ''
  form.loginUrl = ''
  form.loginUsername = ''
  form.loginPassword = ''
  form.loginSuccessIndicator = ''
  form.autoLogin = true
  editing.value = null
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
    const payload = {
      name: form.name.trim(),
      baseUrl: form.baseUrl.trim(),
      loginUrl: form.loginUrl.trim() || null,
      loginUsername: form.loginUsername.trim() || null,
      loginPassword: form.loginPassword || null,
      loginSuccessIndicator: form.loginSuccessIndicator.trim() || null,
      autoLogin: form.autoLogin,
      browser: form.browser || null,
    }
    if (editing.value) {
      await updateEnvironment(editing.value.id, payload)
      ElMessage.success('环境已保存')
    } else {
      await createEnvironment(props.projectId, payload)
      ElMessage.success('环境已创建')
    }
    dialogVisible.value = false
    await load()
    emit('refresh')
  } finally {
    saving.value = false
  }
}

const handleDelete = async (row: EnvironmentView) => {
  try {
    await ElMessageBox.confirm(`确认删除环境「${row.name}」？`, '提示', { type: 'warning' })
  } catch {
    return
  }
  await deleteEnvironment(row.id)
  ElMessage.success('已删除')
  await load()
  emit('refresh')
}

onMounted(load)
</script>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.base-url {
  font-family: monospace;
  font-size: 13px;
  color: #606266;
}

.form-hint {
  margin-left: 12px;
  color: #909399;
  font-size: 13px;
}
.browser-select {
  width: 220px;
}

.browser-hint {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
</style>
