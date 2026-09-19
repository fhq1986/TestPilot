<template>
  <div class="mock-list">
    <el-card class="list-card">
      <div class="toolbar">
        <el-select v-model="projectId" placeholder="选择项目" clearable filterable class="project-select"
          @change="load">
          <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <div class="toolbar-spacer" />
        <el-button type="primary" :icon="Plus" @click="openCreate">创建 Mock</el-button>
        <el-button :icon="Refresh" @click="load">刷新</el-button>
      </div>

      <div class="table-wrap">
        <el-table v-loading="loading" :data="mocks" row-key="id" height="100%">
          <el-table-column prop="name" label="名称" min-width="200" show-overflow-tooltip />
          <el-table-column label="端口" width="110">
            <template #default="{ row }">{{ row.port ?? '—' }}</template>
          </el-table-column>
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="row.status === MockStatus.Running ? 'success' : 'info'">
                {{ row.status === MockStatus.Running ? '运行中' : '已停止' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="地址" min-width="180">
            <template #default="{ row }">
              <span v-if="row.status === MockStatus.Running && row.port" class="mock-url">
                http://127.0.0.1:{{ row.port }}
              </span>
              <span v-else>—</span>
            </template>
          </el-table-column>
          <el-table-column label="创建时间" width="180">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="100">
            <template #default="{ row }">
              <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
            </template>
          </el-table-column>
          <template #empty>
            <el-empty description="暂无 Mock，点击右上角「创建 Mock」" :image-size="80" />
          </template>
        </el-table>
      </div>
    </el-card>

    <el-dialog v-model="createVisible" title="创建 Mock" width="620px" @closed="resetCreateForm">
      <el-form ref="createFormRef" :model="createForm" :rules="createRules" label-position="top">
        <el-form-item label="所属项目" prop="projectId">
          <el-select v-model="createForm.projectId" placeholder="选择项目" filterable class="w-full">
            <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="Mock 名称" prop="name">
          <el-input v-model="createForm.name" placeholder="例如 用户服务 Mock" maxlength="200" />
        </el-form-item>
        <el-form-item label="Swagger / OpenAPI Spec（粘贴 JSON）" prop="spec">
          <el-input v-model="createForm.spec" type="textarea" :rows="10"
            placeholder="粘贴 OpenAPI JSON，Mock 将按端点自动生成语义示例响应" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" :loading="creating" @click="handleCreate">创建并启动</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Plus, Refresh } from '@element-plus/icons-vue'
import { getProjects } from '@/api/project'
import { createMock, deleteMock, getMocks } from '@/api/mock'
import { MockStatus, type MockDto } from '@/types/mock'
import type { Project } from '@/types/project'
import { formatDateTime } from '@/utils/formatter'

const projectOptions = ref<Project[]>([])
const projectId = ref('')
const mocks = ref<MockDto[]>([])
const loading = ref(false)
const creating = ref(false)
const createVisible = ref(false)
const createFormRef = ref<FormInstance>()

const createForm = reactive({
  projectId: '',
  name: '',
  spec: '',
})

const createRules: FormRules = {
  projectId: [{ required: true, message: '请选择项目', trigger: 'change' }],
  name: [{ required: true, message: '请输入 Mock 名称', trigger: 'blur' }],
  spec: [{ required: true, message: '请粘贴 Swagger/OpenAPI Spec', trigger: 'blur' }],
}

const load = async () => {
  loading.value = true
  try {
    mocks.value = await getMocks({ projectId: projectId.value || undefined })
  } finally {
    loading.value = false
  }
}

const openCreate = () => {
  if (projectOptions.value.length === 0) {
    ElMessage.warning('请先创建项目')
    return
  }
  createForm.projectId = projectId.value || ''
  createVisible.value = true
}

const resetCreateForm = () => {
  createForm.projectId = ''
  createForm.name = ''
  createForm.spec = ''
  createFormRef.value?.clearValidate()
}

const handleCreate = async () => {
  try {
    await createFormRef.value?.validate()
  } catch {
    return
  }
  creating.value = true
  try {
    const mock = await createMock({
      projectId: createForm.projectId,
      name: createForm.name,
      spec: createForm.spec,
    })
    createVisible.value = false
    projectId.value = mock.projectId
    ElMessage.success(`Mock「${mock.name}」已启动：http://127.0.0.1:${mock.port}`)
    await load()
  } finally {
    creating.value = false
  }
}

const handleDelete = async (row: MockDto) => {
  try {
    await ElMessageBox.confirm(`确认删除 Mock「${row.name}」？删除后服务将停止。`, '提示', { type: 'warning' })
  } catch {
    return
  }
  await deleteMock(row.id)
  ElMessage.success('已删除')
  await load()
}

onMounted(async () => {
  const projects = await getProjects({ page: 1, pageSize: 100 })
  projectOptions.value = projects.items
  await load()
})
</script>

<style scoped>
/* 整页纵向布局：计算固定高度（56=顶栏，32=main 上下内边距），工具条固定、表格吃掉剩余高度 */
.mock-list {
  height: calc(100vh - 56px - 32px);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.list-card {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.list-card :deep(.el-card__body) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.table-wrap {
  flex: 1;
  min-height: 0;
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
  flex-shrink: 0;
}

.project-select {
  width: 220px;
}

.toolbar-spacer {
  flex: 1;
}

.mock-url {
  color: #67c23a;
  font-family: monospace;
  font-size: 13px;
}

.w-full {
  width: 100%;
}
</style>
