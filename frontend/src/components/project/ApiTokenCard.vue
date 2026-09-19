<template>
  <el-card class="api-token-card">
    <template #header>
      <div class="card-header">
        <span>API 令牌（{{ tokens.length }}）</span>
        <el-button type="primary" size="small" :icon="Plus" @click="openCreate">新建令牌</el-button>
      </div>
    </template>

    <el-alert type="warning" :closable="false" class="usage-hint">
      <template #title>
        CI / 外部系统用 <code>Authorization: Bearer atp_...</code> 调用
        <code>POST /api/webhooks/executions</code> 触发本项目执行。
        令牌隐含本项目的用例范围，跨项目一律 403。明文只在创建时显示一次，请立即保存。
      </template>
    </el-alert>

    <el-table v-loading="loading" :data="tokens" row-key="id" size="small">
      <el-table-column prop="name" label="用途" min-width="150" show-overflow-tooltip />
      <el-table-column label="令牌前缀" width="140">
        <template #default="{ row }">
          <code class="prefix">{{ row.prefix }}…</code>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="90">
        <template #default="{ row }">
          <el-tag v-if="row.revokedAt" type="info" size="small">已吊销</el-tag>
          <el-tag v-else-if="isExpired(row)" type="danger" size="small">已过期</el-tag>
          <el-tag v-else type="success" size="small">可用</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="过期时间" width="120">
        <template #default="{ row }">
          {{ row.expiresAt ? formatDateTime(row.expiresAt) : '永不' }}
        </template>
      </el-table-column>
      <el-table-column label="最近使用" width="160">
        <template #default="{ row }">
          {{ row.lastUsedAt ? formatDateTime(row.lastUsedAt) : '—' }}
        </template>
      </el-table-column>
      <el-table-column label="创建时间" width="160">
        <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="90">
        <template #default="{ row }">
          <el-button v-if="!row.revokedAt" link type="danger" size="small" @click="handleRevoke(row)">
            吊销
          </el-button>
        </template>
      </el-table-column>
    </el-table>
    <!-- <el-empty v-if="!loading && tokens.length === 0" description="还没有 API 令牌" :image-size="60" /> -->

    <!-- 新建：填名称与有效期 -->
    <el-dialog v-model="createVisible" title="新建 API 令牌" width="520px" @closed="resetCreate">
      <el-form ref="formRef" :model="form" :rules="rules" label-position="top">
        <el-form-item label="用途（给谁用的）" prop="name">
          <el-input v-model="form.name" placeholder="例如 Jenkins 流水线 / GitHub Actions" maxlength="100" />
        </el-form-item>
        <el-form-item label="有效期">
          <el-radio-group v-model="form.expiresInDays">
            <el-radio-button :value="30">30 天</el-radio-button>
            <el-radio-button :value="90">90 天</el-radio-button>
            <el-radio-button :value="365">1 年</el-radio-button>
            <el-radio-button :value="0">永不</el-radio-button>
          </el-radio-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleCreate">创建</el-button>
      </template>
    </el-dialog>

    <!-- 创建成功：明文只此一次 -->
    <el-dialog v-model="createdVisible" title="令牌已创建" width="640px" :close-on-click-modal="false">
      <el-alert type="warning" :closable="false" class="once-warn" show-icon title="这是令牌明文唯一一次展示，关闭后无法再查看——请立即复制保存" />
      <div class="token-plain">
        <code>{{ created?.plainToken }}</code>
      </div>
      <template #footer>
        <el-button type="primary" @click="copyPlain">复制令牌</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { createApiToken, listApiTokens, revokeApiToken, type ApiTokenCreated, type ApiTokenItem } from '@/api/project'
import { formatDateTime } from '@/utils/formatter'

const props = defineProps<{ projectId: string }>()

const tokens = ref<ApiTokenItem[]>([])
const loading = ref(false)
const saving = ref(false)
const createVisible = ref(false)
const createdVisible = ref(false)
const created = ref<ApiTokenCreated | null>(null)
const formRef = ref<FormInstance>()

const form = reactive<{ name: string; expiresInDays: number }>({ name: '', expiresInDays: 90 })
const rules = {
  name: [{ required: true, message: '请填写令牌用途', trigger: 'blur' }],
}

const isExpired = (row: ApiTokenItem) => !!row.expiresAt && new Date(row.expiresAt).getTime() <= Date.now()

const load = async () => {
  loading.value = true
  try {
    tokens.value = await listApiTokens(props.projectId)
  } finally {
    loading.value = false
  }
}

const openCreate = () => {
  createVisible.value = true
}

const resetCreate = () => {
  form.name = ''
  form.expiresInDays = 90
  formRef.value?.clearValidate()
}

const handleCreate = async () => {
  await formRef.value?.validate().catch(() => Promise.reject())
  saving.value = true
  try {
    created.value = await createApiToken(props.projectId, {
      name: form.name.trim(),
      // 0 = 永不过期；后端约定 null 为不过期
      expiresInDays: form.expiresInDays === 0 ? undefined : form.expiresInDays,
    })
    createVisible.value = false
    createdVisible.value = true
    await load()
  } finally {
    saving.value = false
  }
}

const copyPlain = async () => {
  if (!created.value) return
  try {
    await navigator.clipboard.writeText(created.value.plainToken)
    ElMessage.success('已复制到剪贴板')
  } catch {
    // 非安全上下文（http）下 clipboard API 不可用：退回选中文本让用户手动 Ctrl+C
    ElMessage.info('自动复制失败，请手动选中文本复制')
  }
}

const handleRevoke = async (row: ApiTokenItem) => {
  await ElMessageBox.confirm(
    `吊销后使用该令牌的调用会立即失败（如 Jenkins 流水线「${row.name}」）。确定吊销？`,
    '吊销令牌',
    { type: 'warning', confirmButtonText: '吊销', cancelButtonText: '取消' },
  )
  await revokeApiToken(props.projectId, row.id)
  ElMessage.success('已吊销')
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

.usage-hint code {
  background: var(--el-fill-color-light);
  padding: 0 4px;
  border-radius: 3px;
}

.prefix {
  font-size: 12px;
}

.once-warn {
  margin-bottom: 12px;
}

.token-plain {
  background: var(--el-fill-color-light);
  border-radius: 6px;
  padding: 12px;
  word-break: break-all;
  user-select: all;
}

.token-plain code {
  font-size: 13px;
}
</style>
