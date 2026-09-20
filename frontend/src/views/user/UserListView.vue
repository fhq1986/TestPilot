<template>
  <div class="user-list">
    <el-card class="list-card">
      <div class="toolbar">
        <div class="toolbar-left">
          <el-input v-model="search" placeholder="搜索用户名 / 姓名" clearable class="search-input" @keyup.enter="load(1)"
            @clear="load(1)" />
          <el-select v-model="roleFilter" placeholder="全部角色" clearable class="role-select" @change="load(1)">
            <el-option v-for="r in roleOptions" :key="r.value" :label="r.label" :value="r.value" />
          </el-select>
          <el-button type="primary" :icon="Search" @click="load(1)">查询</el-button>
        </div>
        <div class="toolbar-right">
          <span v-for="r in roleOptions" :key="r.value" class="role-count">
            {{ r.label }} <b>{{ roleCounts[r.key] ?? 0 }}</b>
          </span>
          <el-button type="primary" :icon="Grid" @click="matrixVisible = true" plain>角色权限矩阵</el-button>
          <el-button type="primary" :icon="Plus" @click="openCreate">新建用户</el-button>
          <el-button :icon="Refresh" @click="load()">刷新</el-button>
        </div>
      </div>

      <el-alert type="warning" :closable="false" class="policy-tip">
        <template #title>
          密码策略：至少 {{ MIN_PASSWORD_LENGTH }} 位，且包含字母、数字、符号中的至少 {{ MIN_CHAR_CLASSES }} 类。
          修改角色、停用账号或重置密码后，该用户已登录的会话会<b>立即失效</b>，需重新登录。
        </template>
      </el-alert>

      <div class="table-wrap">
        <el-table v-loading="loading" :data="users" row-key="id" height="100%">
          <el-table-column prop="username" label="用户名" min-width="140" />
          <el-table-column prop="displayName" label="姓名" min-width="120" />
          <el-table-column label="邮箱" min-width="200">
            <template #default="{ row }">
              <span v-if="row.email">{{ row.email }}</span>
              <!-- 邮箱是验收结果邮件的唯一投递地址，空着就是"收不到"，用提示色标出来 -->
              <el-tag v-else size="small" type="warning" effect="plain">未填写</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="角色" width="130">
            <template #default="{ row }">
              <el-tag :type="roleTagType(row.role)" effect="light" size="small">
                {{ row.roleName }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="row.isActive ? 'success' : 'info'" effect="plain" size="small">
                {{ row.isActive ? '启用' : '已停用' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="SSO" width="110">
            <template #default="{ row }">
              <el-tag v-if="row.ssoProvider" size="small" type="success" effect="plain">
                {{ SSO_PROVIDER_NAMES[row.ssoProvider] ?? row.ssoProvider }}
              </el-tag>
              <span v-else class="text-muted">—</span>
            </template>
          </el-table-column>
          <el-table-column label="最近登录" width="190">
            <template #default="{ row }">
              <div v-if="row.lastLoginAt" class="login-cell">
                <span>{{ formatDateTime(row.lastLoginAt) }}</span>
                <span class="login-ip">{{ row.lastLoginIp || '-' }}</span>
              </div>
              <span v-else class="text-muted">从未登录</span>
            </template>
          </el-table-column>
          <el-table-column label="创建时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="270" :fixed="isMobile ? false : 'right'">
            <template #default="{ row }">
              <el-button link type="primary" :disabled="isBuiltIn(row)" @click="openEdit(row)">编辑</el-button>
              <el-button link type="primary" @click="openReset(row)">重置密码</el-button>
              <el-button link :type="row.isActive ? 'warning' : 'success'" :disabled="isBuiltIn(row)" @click="toggleActive(row)">
                {{ row.isActive ? '停用' : '启用' }}
              </el-button>
              <el-button link type="danger" :disabled="isSelf(row) || isBuiltIn(row)" @click="handleDelete(row)">
                删除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize" :total="total"
        :page-sizes="[10, 20, 50, 100]" layout="total, sizes, prev, pager, next" @current-change="load()"
        @size-change="load(1)" />
    </el-card>

    <!-- 新建 / 编辑 -->
    <el-dialog v-model="dialogVisible" :title="editing ? '编辑用户' : '新建用户'" width="520px" @closed="resetForm">
      <el-form ref="formRef" :model="form" :rules="formRules" label-width="90px">
        <el-form-item label="用户名" prop="username">
          <el-input v-model="form.username" :disabled="editing" placeholder="登录账号，创建后不可修改" />
        </el-form-item>
        <el-form-item v-if="!editing" label="初始密码" prop="password">
          <el-input v-model="form.password" type="password" show-password
            :placeholder="`至少 ${MIN_PASSWORD_LENGTH} 位，含字母/数字/符号至少两类`" />
        </el-form-item>
        <el-form-item label="姓名" prop="displayName">
          <el-input v-model="form.displayName" placeholder="显示名称" />
        </el-form-item>
        <el-form-item label="邮箱" prop="email">
          <el-input v-model="form.email" placeholder="用于接收测试计划验收结果邮件（可选）" clearable />
        </el-form-item>
        <el-form-item label="角色" prop="role">
          <el-radio-group v-model="form.role">
            <el-radio-button v-for="r in roleOptions" :key="r.value" :value="r.value">
              {{ r.label }}
            </el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="editing" label="状态">
          <el-switch v-model="form.isActive" active-text="启用" inactive-text="停用" inline-prompt />
        </el-form-item>
        <el-alert v-if="editing" type="warning" :closable="false" class="dialog-tip">
          <template #title>{{ roleHint }}</template>
        </el-alert>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 重置密码 -->
    <el-dialog v-model="resetVisible" title="重置密码" width="460px" @closed="resetPwdForm">
      <el-alert type="warning" :closable="false" class="dialog-tip">
        <template #title>
          将为用户「{{ resetTarget?.displayName }}（{{ resetTarget?.username }}）」设置新密码，
          该用户已登录的会话会立即失效。
        </template>
      </el-alert>
      <el-form ref="resetFormRef" :model="pwdForm" :rules="pwdRules" label-width="90px">
        <el-form-item label="新密码" prop="newPassword">
          <el-input v-model="pwdForm.newPassword" type="password" show-password
            :placeholder="`至少 ${MIN_PASSWORD_LENGTH} 位，含字母/数字/符号至少两类`" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="resetVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleResetPassword">确认重置</el-button>
      </template>
    </el-dialog>

    <!-- 角色权限矩阵 -->
    <el-dialog v-model="matrixVisible" title="角色权限矩阵" width="90%">
      <p class="matrix-tip">
        下表由后端 <code>PermissionCatalog</code> 直接生成，是权限判定的唯一权威来源；
        前端菜单与按钮可见性只是它的投影。
      </p>
      <el-table v-loading="matrixLoading" :data="matrix" border size="small">
        <el-table-column prop="roleName" label="角色" width="120" :fixed="!isMobile" />
        <el-table-column v-for="p in matrixPermissions" :key="p" :label="p.label" width="110" align="center">
          <template #default="{ row }">
            <el-icon v-if="row.matrix[p.name]" class="yes">
              <CircleCheckFilled />
            </el-icon>
            <span v-else class="no">—</span>
          </template>
        </el-table-column>
      </el-table>
      <template #footer>
        <el-button @click="matrixVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, toRefs } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { CircleCheckFilled, Grid, Plus, Refresh, Search } from '@element-plus/icons-vue'
import {
  createUserApi, deleteUserApi, listUsersApi, resetUserPasswordApi,
  roleMatrixApi, updateUserApi, userRoleCountsApi,
} from '@/api/auth'
import { PermissionLabels } from '@/constants/permissions'
import { useAuthStore } from '@/stores/auth'
import { formatDateTime } from '@/utils/formatter'
import type { RoleMatrixRow, UserRoleValue, UserView } from '@/types/auth'
import { SSO_PROVIDER_NAMES, UserRole } from '@/types/auth'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { usePagedList } from '@/composables/usePagedList'

/**
 * 窄屏（< 1024px）：横向滚动保底（用户管理是低频页），但固定列必须取消 ——
 * fixed="right" 的操作列在 375px 下会占满可见宽度，数据列全被挤出去。
 */
const { isMobile } = useBreakpoint()

const MIN_PASSWORD_LENGTH = 8
const MIN_CHAR_CLASSES = 2

const roleOptions = [
  { value: 0 as UserRoleValue, label: '管理员', key: 'Admin' },
  { value: 1 as UserRoleValue, label: '测试工程师', key: 'Tester' },
  { value: 2 as UserRoleValue, label: '只读访客', key: 'Viewer' },
]

const roleTagType = (role: number) =>
  role === UserRole.SuperAdmin || role === UserRole.Admin
    ? 'danger'
    : role === UserRole.Tester ? 'primary' : 'info'

const auth = useAuthStore()
const saving = ref(false)
const search = ref('')
const roleFilter = ref<UserRoleValue | ''>('')
// 分页与服务端筛选（页码/页大小/总数/loading），见 composables/usePagedList.ts；
// 账号一多就不能再全量拉取，否则列表接口会变成一次全表读
const list = usePagedList<UserView>((p, ps) => listUsersApi({
  search: search.value.trim() || undefined,
  role: roleFilter.value === '' ? undefined : roleFilter.value,
  page: p,
  pageSize: ps,
}), { pageSize: 20 })
const load = (targetPage?: number) => list.load(targetPage)
const { items: users, total, page, pageSize, loading } = toRefs(list)
const roleCounts = ref<Record<string, number>>({})

const dialogVisible = ref(false)
const editing = ref<UserView | null>(null)
const formRef = ref<FormInstance>()
const form = reactive({
  username: '',
  password: '',
  displayName: '',
  email: '',
  role: 1 as UserRoleValue,
  isActive: true,
})

const resetVisible = ref(false)
const resetTarget = ref<UserView | null>(null)
const resetFormRef = ref<FormInstance>()
const pwdForm = reactive({ newPassword: '' })

const matrixVisible = ref(false)
const matrixLoading = ref(false)
const matrix = ref<RoleMatrixRow[]>([])

/** 密码强度：≥8 位 且 至少 2 类字符（与后端 UserService.ValidatePassword 保持一致） */
const validatePassword = (_r: unknown, value: string, cb: (e?: Error) => void) => {
  if (!value || value.length < MIN_PASSWORD_LENGTH) {
    return cb(new Error(`密码至少 ${MIN_PASSWORD_LENGTH} 位`))
  }
  const classes = [/[A-Za-z]/, /\d/, /[^A-Za-z0-9]/].filter((re) => re.test(value)).length
  if (classes < MIN_CHAR_CLASSES) {
    return cb(new Error('密码需包含字母、数字、符号中的至少两类'))
  }
  cb()
}

const formRules: FormRules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ validator: validatePassword, trigger: 'blur' }],
  displayName: [{ required: true, message: '请输入姓名', trigger: 'blur' }],
  // 邮箱可选；留空放行，填了才校验格式。
  // 不用内置的 type: 'email'：它会把空值也判成不合法，导致"不填邮箱"反而过不了表单。
  email: [{
    validator: (_rule, value: string, callback) => {
      if (!value || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim())) callback()
      else callback(new Error('邮箱格式不正确'))
    },
    trigger: 'blur',
  }],
}
const pwdRules: FormRules = {
  newPassword: [{ validator: validatePassword, trigger: 'blur' }],
}

const filteredUsers = computed(() => users.value)

/** 角色变更影响面提示——让管理员在点击保存前就知道会踢掉对方会话 */
const roleHint = computed(() => {
  if (!editing.value) return ''
  const roleChanged = editing.value.role !== form.role
  const activeChanged = editing.value.isActive !== form.isActive
  if (roleChanged) return '角色已变更：保存后该用户的登录会话将立即失效，需重新登录。'
  if (activeChanged && !form.isActive) return '账号将被停用：保存后该用户无法登录，已有会话立即失效。'
  if (activeChanged) return '账号将被启用：该用户可以重新登录。'
  return '未修改角色或状态，现有会话不受影响。'
})

/** 权限点列（排除与角色无关的全 0 项不必要，但这里全列以免遗漏） */
const matrixPermissions = computed(() =>
  Object.entries(PermissionLabels).map(([name, label]) => ({ name, label })),
)

const isSelf = (row: UserView) => row.id === auth.user?.id

/** 内置超级管理员：角色/状态不可改、不可删除（后端同样拦截，这里只是不给出入口） */
const isBuiltIn = (row: UserView) => row.role === UserRole.SuperAdmin

async function loadRoleCounts() {
  roleCounts.value = await userRoleCountsApi()
}

async function loadMatrix() {
  matrixLoading.value = true
  try {
    matrix.value = await roleMatrixApi()
  } finally {
    matrixLoading.value = false
  }
}

function openCreate() {
  editing.value = null
  dialogVisible.value = true
}

function openEdit(row: UserView) {
  editing.value = row
  form.username = row.username
  form.displayName = row.displayName
  form.email = row.email ?? ''
  form.role = row.role
  form.isActive = row.isActive
  form.password = ''
  dialogVisible.value = true
}

function openReset(row: UserView) {
  resetTarget.value = row
  resetVisible.value = true
}

function resetForm() {
  formRef.value?.clearValidate()
  Object.assign(form, {
    username: '', password: '', displayName: '', email: '',
    role: 1, isActive: true,
  })
  editing.value = null
}

function resetPwdForm() {
  resetFormRef.value?.clearValidate()
  pwdForm.newPassword = ''
  resetTarget.value = null
}

async function handleSave() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return
  // 新建时密码是必填的（编辑不在此处改密码）
  if (!editing.value && !form.password) {
    ElMessage.warning('请填写初始密码')
    return
  }

  saving.value = true
  try {
    if (editing.value) {
      const updated = await updateUserApi(editing.value.id, {
        displayName: form.displayName,
        email: form.email.trim() || null,
        role: form.role,
        isActive: form.isActive,
      })
      ElMessage.success(
        updated.role !== editing.value.role || updated.isActive !== editing.value.isActive
          ? '已保存，该用户的登录会话已失效'
          : '已保存',
      )
    } else {
      await createUserApi({
        username: form.username.trim(),
        password: form.password,
        displayName: form.displayName,
        email: form.email.trim() || null,
        role: form.role,
      })
      ElMessage.success('用户已创建')
    }
    dialogVisible.value = false
    await load()
    await loadRoleCounts()
  } finally {
    saving.value = false
  }
}

async function handleResetPassword() {
  const valid = await resetFormRef.value?.validate().catch(() => false)
  if (!valid || !resetTarget.value) return
  saving.value = true
  try {
    await resetUserPasswordApi(resetTarget.value.id, pwdForm.newPassword)
    ElMessage.success('密码已重置，该用户需重新登录')
    resetVisible.value = false
  } finally {
    saving.value = false
  }
}

async function toggleActive(row: UserView) {
  const next = !row.isActive
  await ElMessageBox.confirm(
    next
      ? `确定启用「${row.displayName}」？启用后该用户可以登录。`
      : `确定停用「${row.displayName}」？停用后该用户无法登录，已有会话立即失效。`,
    next ? '启用账号' : '停用账号',
    { type: 'warning' },
  )
  await updateUserApi(row.id, {
    displayName: row.displayName,
    role: row.role,
    isActive: next,
  })
  ElMessage.success(next ? '已启用' : '已停用，会话已失效')
  await load()
}

async function handleDelete(row: UserView) {
  await ElMessageBox.confirm(
    `确定删除用户「${row.displayName}（${row.username}）」？此操作不可恢复。`,
    '删除用户',
    { type: 'warning' },
  )
  await deleteUserApi(row.id)
  ElMessage.success('已删除')
  await load()
  await loadRoleCounts()
}

onMounted(() => {
  load()
  loadRoleCounts()
  loadMatrix()
})
</script>

<style scoped>
.user-list {
  height: 100%;
}

.list-card {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.list-card :deep(.el-card__body) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.toolbar-left {
  display: flex;
  gap: 10px;
}

.toolbar-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

.role-count {
  color: #7a8290;
  font-size: 13px;
  white-space: nowrap;
}

.role-count b {
  color: var(--el-text-color-primary);
}

.pagination {
  margin-top: 12px;
  justify-content: flex-end;
}

.search-input {
  width: 240px;
}

.role-select {
  width: 150px;
}

.policy-tip {
  margin-bottom: 12px;
}

.table-wrap {
  flex: 1;
  min-height: 0;
}

.login-cell {
  display: flex;
  flex-direction: column;
  line-height: 1.4;
}

.login-ip {
  color: #9aa2ae;
  font-size: 12px;
}

.text-muted {
  color: #9aa2ae;
}

.dialog-tip {
  margin-top: 4px;
}

.matrix-tip {
  color: #7a8290;
  margin-bottom: 12px;
  line-height: 1.7;
}

.yes {
  color: #16a34a;
  font-size: 16px;
}

.no {
  color: #c8ced8;
}
</style>
