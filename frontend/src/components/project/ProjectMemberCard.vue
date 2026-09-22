<template>
  <el-card class="project-member-card">
    <template #header>
      <div class="card-header">
        <span>项目成员（{{ members.length }}）</span>
        <el-button type="primary" size="small" :icon="Plus" @click="openAdd">添加成员</el-button>
      </div>
    </template>

    <el-alert type="info" :closable="false" class="scope-hint">
      <template #title>
        项目级授权采用「成员激活式」：<b>项目一旦有成员，即只对该项目成员（及平台管理员）可见、可操作</b>；
        尚未添加成员的项目保持原有全局权限行为。角色越高权限越大：
        所有者 &gt; 管理员 &gt; 测试工程师 &gt; 只读。
      </template>
    </el-alert>

    <el-table v-loading="loading" :data="members" row-key="id" size="small">
      <el-table-column label="成员" min-width="200" show-overflow-tooltip>
        <template #default="{ row }">
          <span>{{ row.displayName || row.username }}</span>
          <span class="muted">（{{ row.username }}）</span>
        </template>
      </el-table-column>
      <el-table-column label="项目角色" width="160">
        <template #default="{ row }">
          <el-tag :type="projectRoleTagType(row.role)" size="small">{{ projectRoleLabel(row.role) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="加入时间" width="160">
        <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="150" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click="openEdit(row)">改角色</el-button>
          <el-button link type="danger" size="small" @click="handleRemove(row)">移出</el-button>
        </template>
      </el-table-column>
      <template #empty>
        <el-empty description="还没有成员（当前项目对所有有权限的用户可见）" :image-size="60" />
      </template>
    </el-table>

    <!-- 添加 / 改角色 -->
    <el-dialog v-model="dialogVisible" :title="editing ? '修改项目角色' : '添加项目成员'" width="520px"
      @closed="resetForm">
      <el-form label-position="top">
        <el-form-item label="成员">
          <el-select v-if="!editing" v-model="form.userId" filterable remote reserve-keyword
            :remote-method="searchUsers" :loading="searching" placeholder="输入用户名或姓名搜索" style="width: 100%">
            <el-option v-for="u in candidates" :key="u.id" :label="`${u.displayName || u.username}（${u.username}）`"
              :value="u.id" />
          </el-select>
          <div v-else class="edit-target">{{ editing.displayName || editing.username }}（{{ editing.username }}）</div>
        </el-form-item>
        <el-form-item label="项目角色">
          <el-select v-model="form.role" style="width: 100%">
            <el-option v-for="o in PROJECT_ROLE_OPTIONS" :key="o.value" :label="o.label" :value="o.value">
              <span>{{ o.label }}</span>
              <span class="role-desc">{{ o.desc }}</span>
            </el-option>
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSubmit">确定</el-button>
      </template>
    </el-dialog>
  </el-card>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import {
  addProjectMember,
  listProjectMembers,
  removeProjectMember,
  searchMemberCandidates,
  updateProjectMemberRole,
} from '@/api/project'
import { formatDateTime } from '@/utils/formatter'
import {
  PROJECT_ROLE_OPTIONS,
  projectRoleLabel,
  projectRoleTagType,
  type ProjectMember,
  type ProjectRole,
  type UserCandidate,
} from '@/types/project'

const props = defineProps<{ projectId: string }>()

const members = ref<ProjectMember[]>([])
const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const editing = ref<ProjectMember | null>(null)
const candidates = ref<UserCandidate[]>([])
const searching = ref(false)

const form = reactive<{ userId: string; role: ProjectRole }>({ userId: '', role: 1 })

const load = async () => {
  loading.value = true
  try {
    members.value = await listProjectMembers(props.projectId)
  } finally {
    loading.value = false
  }
}

const openAdd = () => {
  editing.value = null
  dialogVisible.value = true
}

const openEdit = (row: ProjectMember) => {
  editing.value = row
  form.role = row.role
  dialogVisible.value = true
}

const resetForm = () => {
  editing.value = null
  form.userId = ''
  form.role = 1
  candidates.value = []
}

const searchUsers = async (query: string) => {
  searching.value = true
  try {
    candidates.value = await searchMemberCandidates(props.projectId, query)
  } finally {
    searching.value = false
  }
}

const handleSubmit = async () => {
  saving.value = true
  try {
    if (editing.value) {
      await updateProjectMemberRole(props.projectId, editing.value.userId, form.role)
      ElMessage.success('已更新角色')
    } else {
      if (!form.userId) {
        ElMessage.warning('请先选择成员')
        return
      }
      await addProjectMember(props.projectId, { userId: form.userId, role: form.role })
      ElMessage.success('已添加成员')
    }
    dialogVisible.value = false
    await load()
  } catch (e: unknown) {
    // 后端对"最后一个 Owner 不可降级/移除"返回 400，这里把原因原样透出
    const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message
    ElMessage.error(msg || '操作失败')
  } finally {
    saving.value = false
  }
}

const handleRemove = async (row: ProjectMember) => {
  await ElMessageBox.confirm(
    `移出后「${row.displayName || row.username}」将无法再访问本项目（平台管理员除外）。确定移出？`,
    '移出成员',
    { type: 'warning', confirmButtonText: '移出', cancelButtonText: '取消' },
  )
  try {
    await removeProjectMember(props.projectId, row.userId)
    ElMessage.success('已移出')
    await load()
  } catch (e: unknown) {
    const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message
    ElMessage.error(msg || '移出失败')
  }
}

onMounted(load)
</script>

<style scoped>
.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.scope-hint {
  margin-bottom: 12px;
}

.muted {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.role-desc {
  float: right;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  margin-left: 12px;
}

.edit-target {
  font-size: 14px;
}
</style>
