<template>
  <div class="requirement-page">

    <!-- 覆盖统计卡 -->
    <el-row :gutter="12" class="stats-row">
      <el-col :span="5">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">需求总数</div>
          <div class="stat-value">{{ coverage?.totalRequirements ?? '—' }}</div>
        </el-card>
      </el-col>
      <el-col :span="5">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">覆盖率</div>
          <div class="stat-value"
            :class="{ danger: (coverage?.totalRequirements ?? 0) > 0 && (coverage?.coverageRate ?? 0) < 80 }">
            {{ coverage ? `${coverage.coverageRate}%` : '—' }}
          </div>
          <div v-if="coverage" class="stat-sub">已覆盖 {{ coverage.coveredRequirements }} / {{ coverage.totalRequirements
            }}</div>
        </el-card>
      </el-col>
      <el-col :span="5">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">未覆盖需求</div>
          <div class="stat-value" :class="{ danger: (coverage?.uncoveredRequirements ?? 0) > 0 }">
            {{ coverage?.uncoveredRequirements ?? '—' }}
          </div>
          <div v-if="coverage" class="stat-sub">还没有任何用例关联</div>
        </el-card>
      </el-col>
      <el-col :span="9">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">覆盖缺口（未覆盖需求）</div>
          <div v-if="coverage && coverage.uncoveredList.length > 0" class="gap-list">
            <el-tag v-for="r in coverage.uncoveredList" :key="r.id" size="small" type="danger" effect="plain"
              class="gap-tag">{{ r.title }}</el-tag>
          </div>
          <div v-else class="stat-sub" style="margin-top: 12px">暂无缺口，所有需求都有用例覆盖 🎉</div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="list-card">
      <div class="filter-bar">
        <el-select v-model="filters.projectId" placeholder="项目" clearable filterable style="width: 200px"
          @change="reload">
          <el-option v-for="p in projects" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-input v-model="filters.search" placeholder="搜索标题 / 外部编号" clearable style="width: 220px"
          @keyup.enter="reload" @clear="reload" />
        <el-button type="primary" :icon="Search" @click="reload">查询</el-button>
        <el-button v-if="authStore.can(Permission.ManageTestCases)" type="primary" :icon="Plus"
          @click="openCreate">新建需求</el-button>
        <el-button v-if="authStore.can(Permission.ManageTestCases)" type="danger" :disabled="selectedRows.length === 0"
          :loading="deleting" @click="handleBatchDelete">
          批量删除（{{ selectedRows.length }}）
        </el-button>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" @click="load()">刷新</el-button>
      </div>

      <div v-if="!isMobile" class="table-wrap">
        <el-table ref="tableRef" :data="items" v-loading="loading" size="small" height="100%"
          @selection-change="onSelectionChange" @row-click="handleRowSelectionClick">
          <el-table-column type="selection" width="44" />
          <el-table-column label="需求标题" min-width="240" fixed="left">
            <template #default="{ row }">
              <el-link type="primary" @click.stop="openEdit(row)">{{ row.title }}</el-link>
            </template>
          </el-table-column>
          <el-table-column prop="priority" label="优先级" width="80">
            <template #default="{ row }">{{ row.priority || '—' }}</template>
          </el-table-column>
          <el-table-column prop="externalKey" label="外部编号" width="140" show-overflow-tooltip>
            <template #default="{ row }">{{ row.externalKey || '—' }}</template>
          </el-table-column>
          <el-table-column prop="projectName" label="项目" min-width="120" show-overflow-tooltip>
            <template #default="{ row }">{{ projectName(row.projectId) }}</template>
          </el-table-column>
          <el-table-column label="关联用例" width="100" align="center">
            <template #default="{ row }">
              <!-- 有挂用例时数字可点：直接跳用例列表并按该需求过滤。
                   .stop 防止同时触发整行勾选 -->
              <el-tooltip v-if="row.caseCount > 0" content="点击查看关联用例" placement="top">
                <el-tag size="small" type="success" effect="plain" class="clickable-tag"
                  @click.stop="goRequirementCases(row)">
                  {{ row.caseCount }}
                </el-tag>
              </el-tooltip>
              <el-tag v-else size="small" type="danger" effect="plain">{{ row.caseCount }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="最近验证通过" width="110" align="center">
            <template #default="{ row }">
              <span v-if="row.caseCount > 0">{{ row.passedCaseCount }} / {{ row.caseCount }}</span>
              <span v-else class="muted">—</span>
            </template>
          </el-table-column>
          <el-table-column label="创建时间" width="160">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="140" fixed="right">
            <template #default="{ row }">
              <!-- 编辑入口放在这里：原来只挂在需求标题的链接上（点标题弹编辑框），
                   而"点标题"在列表里通常被理解成"进详情"，没人会去点；
                   操作列才是大家默认找的地方。
                   权限与新建/后端保持一致（后端 PUT/DELETE 都要求 ManageTestCases），
                   否则只读账号能看到按钮、点了却 403 -->
              <template v-if="authStore.can(Permission.ManageTestCases)">
                <el-button size="small" type="primary" plain text @click="openEdit(row)">编辑</el-button>
                <el-popconfirm title="删除该需求？用例会保留但回到未关联状态" confirm-button-text="删除" @confirm="handleDelete(row.id)">
                  <template #reference>
                    <el-button size="small" type="danger" plain text>删除</el-button>
                  </template>
                </el-popconfirm>
              </template>
              <span v-else class="muted">—</span>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- 窄屏：表格换成卡片。桌面版的操作列 fixed="right"，在窄屏反而会占满宽度，
           所以卡片自己排操作按钮，不靠固定列 -->
      <MobileCardList v-else v-loading="loading" :items="items" :row-key="(row) => row.id" empty-text="暂无需求">
        <template #title="{ item }">
          <el-link type="primary" @click="openEdit(item)">{{ item.title }}</el-link>
        </template>

        <template #badge="{ item }">
          <el-tag v-if="item.priority" size="small">{{ item.priority }}</el-tag>
          <el-tooltip v-if="item.caseCount > 0" content="点击查看关联用例" placement="top">
            <el-tag size="small" type="success" effect="plain" class="clickable-tag"
              @click="goRequirementCases(item)">
              关联 {{ item.caseCount }}
            </el-tag>
          </el-tooltip>
          <el-tag v-else size="small" type="danger" effect="plain">关联 {{ item.caseCount }}</el-tag>
        </template>

        <template #meta="{ item }">
          <span><span class="mcl-label">项目</span>{{ projectName(item.projectId) }}</span>
          <span v-if="item.externalKey"><span class="mcl-label">外部编号</span>{{ item.externalKey }}</span>
          <span v-if="item.caseCount > 0">
            <span class="mcl-label">验证通过</span>{{ item.passedCaseCount }} / {{ item.caseCount }}
          </span>
          <span><span class="mcl-label">创建</span>{{ formatDateTime(item.createdAt) }}</span>
        </template>

        <template #actions="{ item }">
          <template v-if="authStore.can(Permission.ManageTestCases)">
            <el-button link type="primary" @click="openEdit(item)">编辑</el-button>
            <el-popconfirm title="删除该需求？用例会保留但回到未关联状态" confirm-button-text="删除" @confirm="handleDelete(item.id)">
              <template #reference>
                <el-button link type="danger">删除</el-button>
              </template>
            </el-popconfirm>
          </template>
          <span v-else class="muted">—</span>
        </template>
      </MobileCardList>

      <el-pagination v-model:current-page="page" :page-size="pageSize" :total="total" layout="total, prev, pager, next"
        class="pager" @current-change="load" />
    </el-card>

    <!-- 新建/编辑 -->
    <el-dialog v-model="formVisible" :title="editingId ? '编辑需求' : '新建需求'" width="80%">
      <el-form label-width="90px">
        <el-form-item label="项目" required>
          <el-select v-model="form.projectId" filterable :disabled="!!editingId" style="width: 100%">
            <el-option v-for="p in projects" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="标题" required>
          <el-input v-model="form.title" maxlength="300" show-word-limit placeholder="如：用户登录-密码错误提示" />
        </el-form-item>
        <el-form-item label="优先级">
          <el-select v-model="form.priority" clearable style="width: 160px" placeholder="P0 / P1 / P2">
            <el-option v-for="p in ['P0', 'P1', 'P2']" :key="p" :label="p" :value="p" />
          </el-select>
        </el-form-item>
        <el-form-item label="外部编号">
          <el-input v-model="form.externalKey" maxlength="200" placeholder="外部需求系统的标识（对接预留）" />
        </el-form-item>
        <el-form-item label="说明">
          <RichTextEditor v-model="form.description" :height="200" placeholder="验收要点、来源文档链接等（可粘贴或拖入图片）" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="formVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitForm">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { getProjects } from '@/api/project'
import {
  batchDeleteRequirements, createRequirement, deleteRequirement, getRequirementCoverage, getRequirements,
  updateRequirement,
} from '@/api/requirement'
import RichTextEditor from '@/components/common/RichTextEditor.vue'
import { useAuthStore } from '@/stores/auth'
import { Permission } from '@/constants/permissions'
import { formatDateTime } from '@/utils/formatter'
import type { Project } from '@/types/project'
import type { RequirementListItem, RequirementPayload } from '@/types/requirement'
import MobileCardList from '@/components/common/MobileCardList.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { usePagedList } from '@/composables/usePagedList'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useRowSelection } from '@/composables/useRowSelection'

/** 窄屏（< 1024px）：表格换成卡片形态，见下方模板 */
const { isMobile } = useBreakpoint()

const authStore = useAuthStore()
const router = useRouter()

const projects = ref<Project[]>([])
const coverage = ref<Awaited<ReturnType<typeof getRequirementCoverage>> | null>(null)

const filters = ref<{ projectId?: string; search?: string }>({})

const projectName = (projectId: string) => projects.value.find((p) => p.id === projectId)?.name ?? '—'

/** 点「关联用例」数字：跳用例列表，带需求 id（列表页会显示可清除的筛选标识）+ 项目 id */
const goRequirementCases = (row: RequirementListItem) => {
  void router.push({ path: '/testcases', query: { requirementId: row.id, projectId: row.projectId } })
}

// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<RequirementListItem>((p, ps) => getRequirements({
  projectId: filters.value.projectId,
  search: filters.value.search,
  page: p,
  pageSize: ps,
}), { pageSize: 20 })
const { items, total, page, pageSize, loading } = toRefs(list)
const load = () => list.load()
const reload = () => list.load(1)

// 批量删除（用例保留，只是回到未关联状态——需求 ↔ 用例映射级联清理）
const { deleting, selectedRows, onSelectionChange, handleBatchDelete } =
  useBatchDelete<RequirementListItem>({
    entity: '需求',
    remove: batchDeleteRequirements,
    reload: load,
  })

// 点击行直接勾选/取消勾选（复选框列与操作列除外）
const { tableRef, handleRowSelectionClick } = useRowSelection()

const loadCoverage = async () => {
  coverage.value = await getRequirementCoverage(filters.value.projectId)
}

const loadProjects = async () => {
  const result = await getProjects({ page: 1, pageSize: 100 })
  projects.value = result.items
}

// ------------------------------ 新建 / 编辑

const formVisible = ref(false)
const saving = ref(false)
const editingId = ref<string | null>(null)
const form = ref<RequirementPayload & { projectId?: string }>({ title: '' })

const openCreate = () => {
  editingId.value = null
  form.value = {
    projectId: filters.value.projectId ?? projects.value[0]?.id,
    title: '',
    description: '',
    externalKey: '',
    priority: '',
  }
  formVisible.value = true
}

const openEdit = async (row: RequirementListItem) => {
  editingId.value = row.id
  form.value = {
    projectId: row.projectId,
    title: row.title,
    description: row.description ?? '',
    externalKey: row.externalKey ?? '',
    priority: row.priority ?? '',
  }
  formVisible.value = true
}

const submitForm = async () => {
  if (!form.value.projectId) {
    ElMessage.warning('请选择项目')
    return
  }
  if (!form.value.title.trim()) {
    ElMessage.warning('请填写标题')
    return
  }
  saving.value = true
  try {
    const payload: RequirementPayload = {
      title: form.value.title,
      description: form.value.description || null,
      externalKey: form.value.externalKey || null,
      priority: form.value.priority || null,
    }
    if (editingId.value) {
      await updateRequirement(editingId.value, payload)
      ElMessage.success('需求已更新')
    } else {
      await createRequirement({ ...payload, projectId: form.value.projectId })
      ElMessage.success('需求已创建')
    }
    formVisible.value = false
    reload()
    void loadCoverage()
  } finally {
    saving.value = false
  }
}

const handleDelete = async (id: string) => {
  await ElMessageBox.confirm('确认删除该需求？', '删除', { type: 'warning' }).catch(() => Promise.reject())
  await deleteRequirement(id)
  ElMessage.success('已删除')
  reload()
  void loadCoverage()
}

onMounted(() => {
  void loadProjects()
  reload()
  void loadCoverage()
})
</script>

<style scoped>
/* 与缺陷管理页同一套整页布局：统计卡固定，列表自适应撑满 */
.requirement-page {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 56px - 32px);
  overflow: hidden;
}

.stats-row {
  margin-bottom: 12px;
  flex-shrink: 0;
}

.stat-card {
  height: 100%;
}

.stat-card :deep(.el-card__body) {
  padding: 14px 16px;
}

.stat-label {
  font-size: 12px;
  color: #909399;
}

.stat-value {
  font-size: 26px;
  font-weight: 600;
  margin-top: 4px;
}

.stat-value.danger {
  color: #f56c6c;
}

.stat-sub {
  font-size: 12px;
  color: #909399;
  margin-top: 2px;
}

.gap-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 8px;
}

.gap-tag {
  max-width: 100%;
}

/* 「关联用例」数字：有挂用例时可点，光标要给出可点的暗示 */
.clickable-tag {
  cursor: pointer;
}

.list-card {
  flex: 1;
  min-height: 0;
}

.list-card :deep(.el-card__body) {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.table-wrap {
  flex: 1;
  min-height: 0;
}

.filter-bar {
  display: flex;
  gap: 10px;
  margin-bottom: 12px;
  flex-shrink: 0;
}

/* 刷新按钮推到工具栏最右 */
.toolbar-spacer {
  flex: 1;
}

.pager {
  margin-top: 12px;
  justify-content: flex-end;
  flex-shrink: 0;
}

.muted {
  color: #909399;
  font-size: 12px;
}
</style>
