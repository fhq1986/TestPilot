<template>
  <div class="schedule-list">
    <el-card class="list-card">
      <div class="toolbar">
        <el-select v-model="projectId" placeholder="选择项目" clearable filterable class="project-select" @change="load(1)">
          <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-select v-model="enabledFilter" placeholder="启用状态" clearable class="status-select" @change="load(1)">
          <el-option label="已启用" :value="true" />
          <el-option label="已停用" :value="false" />
        </el-select>
        <el-input v-model="keyword" placeholder="搜索任务名称" clearable class="keyword-input" :prefix-icon="Search"
          @keyup.enter="load(1)" @clear="load(1)" />
        <el-button type="primary" :icon="Plus" @click="openCreate">新建定时任务</el-button>
        <el-button type="danger" :icon="Delete" :disabled="selectedRows.length === 0" :loading="deleting"
          @click="handleBatchDelete">
          批量删除（{{ selectedRows.length }}）
        </el-button>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" @click="load()">刷新</el-button>
      </div>
      <div class="table-wrap">
        <el-table ref="tableRef" v-loading="loading" :data="schedules" row-key="id" height="100%"
          @selection-change="onSelectionChange" @row-click="handleRowSelectionClick">
          <el-table-column type="selection" width="44" />
          <el-table-column label="任务名称" min-width="180" show-overflow-tooltip fixed="left">
            <template #default="{ row }">
              <!-- 无独立详情页，编辑弹窗即该任务的完整配置视图 -->
              <el-link type="primary" :underline="false" @click.stop="openEdit(row)">{{ row.name }}</el-link>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="90">
            <template #default="{ row }">
              <el-tag :type="row.enabled ? 'success' : 'info'">{{ row.enabled ? '已启用' : '已停用' }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="调度规则" min-width="200">
            <template #default="{ row }">
              <div class="cron-cell">
                <span class="cron-desc">{{ row.cronDescription }}</span>
                <code class="cron-expr">{{ row.cronExpression }}</code>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="执行范围" min-width="220">
            <template #default="{ row }">
              <template v-if="row.scopeKind === ScheduleScopeKind.TestPlan">
                <el-tag size="small" type="warning" effect="plain">测试计划</el-tag>
                <el-tag v-for="p in row.testPlans || []" :key="p.id" size="small" class="scope-tag"
                  :type="p.status === 1 ? 'success' : 'info'">
                  {{ p.name }}<span v-if="p.status !== 1">（非进行中）</span>
                </el-tag>
                <span v-if="!(row.testPlans || []).length" class="scope-empty">未选择计划</span>
              </template>
              <template v-else>
                <span v-if="row.testCaseCount === 0" class="scope-empty">未匹配到用例</span>
                <template v-else>
                  <el-tag size="small" type="info">{{ row.testCaseCount }} 个用例</el-tag>
                  <el-tag v-if="row.module" size="small" class="scope-tag">模块：{{ row.module }}</el-tag>
                  <el-tag v-if="row.priority" size="small" class="scope-tag">{{ row.priority }}</el-tag>
                </template>
              </template>
              <div class="scope-env">{{ row.environmentName || '未指定环境（用用例/计划默认）' }}</div>
            </template>
          </el-table-column>
          <el-table-column label="上次执行" width="170">
            <template #default="{ row }">
              <div>{{ row.lastRunAt ? formatDateTime(row.lastRunAt) : '—' }}</div>
              <div v-if="row.lastRunAt" class="sub-text">
                创建 {{ row.lastCreatedCount }} 条
              </div>
            </template>
          </el-table-column>
          <el-table-column label="下次执行" width="170">
            <template #default="{ row }">
              <span v-if="!row.enabled">—</span>
              <span v-else-if="row.nextRunAt">{{ formatDateTime(row.nextRunAt) }}</span>
              <span v-else class="sub-text">待计算</span>
            </template>
          </el-table-column>
          <el-table-column label="最近异常" min-width="160" show-overflow-tooltip>
            <template #default="{ row }">
              <el-tooltip v-if="row.lastError" :content="row.lastError" placement="top">
                <span class="error-text">{{ row.lastError }}</span>
              </el-tooltip>
              <span v-else class="sub-text">—</span>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="220" :fixed="isMobile ? false : 'right'">
            <template #default="{ row }">
              <el-button link type="primary" :loading="runningId === row.id" @click="handleRun(row)">试跑</el-button>
              <el-button link :type="row.enabled ? 'warning' : 'success'" @click="handleToggle(row)">
                {{ row.enabled ? '停用' : '启用' }}
              </el-button>
              <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
              <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize" :total="total"
        :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" @current-change="load()"
        @size-change="load(1)" />
    </el-card>

    <!-- 新建 / 编辑 -->
    <el-dialog v-model="dialogVisible" :title="editingId ? '编辑定时任务' : '新建定时任务'" width="680px" destroy-on-close>
      <el-form ref="formRef" :model="form" :rules="rules" label-width="110px">
        <el-form-item label="任务名称" prop="name">
          <el-input v-model="form.name" placeholder="如：夜间全量回归" maxlength="200" show-word-limit />
        </el-form-item>

        <el-form-item label="所属项目" prop="projectId">
          <el-select v-model="form.projectId" placeholder="选择项目" filterable :disabled="!!editingId" class="full-width"
            @change="onProjectChange">
            <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
        </el-form-item>

        <el-form-item label="Cron 表达式" prop="cronExpression">
          <div class="cron-editor">
            <el-select v-model="presetValue" placeholder="常用规则" class="preset-select" @change="applyPreset">
              <el-option v-for="p in CRON_PRESETS" :key="p.value" :label="`${p.label}（${p.value}）`" :value="p.value" />
            </el-select>
            <el-input v-model="form.cronExpression" placeholder="分 时 日 月 周，如 0 2 * * *" @blur="refreshPreview" />
          </div>
          <div class="cron-hint">
            <template v-if="preview">
              <span :class="preview.valid ? 'hint-ok' : 'hint-error'">
                {{ preview.valid ? preview.cronDescription : preview.error }}
              </span>
              <span v-if="preview.valid && preview.occurrences.length" class="hint-next">
                接下来：{{preview.occurrences.slice(0, 3).map((t) => formatDateTime(t)).join('、')}}
              </span>
            </template>
            <span v-else class="hint-next">5 段格式：分钟 小时 日 月 星期（按服务器本地时间）</span>
          </div>
        </el-form-item>

        <el-form-item label="执行范围">
          <el-radio-group v-model="scopeMode">
            <el-radio value="filter">按模块 / 优先级筛选</el-radio>
            <el-radio value="cases">指定用例</el-radio>
            <el-radio value="plan">测试计划</el-radio>
          </el-radio-group>
        </el-form-item>

        <el-form-item v-if="scopeMode === 'plan'" label="选择计划">
          <el-select v-model="selectedPlanIds" multiple filterable class="full-width" placeholder="选择要定时执行的测试计划"
            :disabled="!form.projectId">
            <el-option v-for="p in planOptions" :key="p.id" :label="p.name" :value="p.id">
              <span>{{ p.name }}</span>
              <span class="option-note">
                {{ p.releaseName ? p.releaseName + ' · ' : '' }}{{ PLAN_STATUS_LABELS[p.status] }}
              </span>
            </el-option>
          </el-select>
          <div class="cron-hint">
            已选 {{ selectedPlanIds.length }} 个计划；只有<b>进行中</b>的计划会被定时触发开新一轮。
            计划不存在或已有进行中轮次时会跳过本次（不报错）。
          </div>
        </el-form-item>

        <template v-if="scopeMode === 'filter'">
          <el-form-item label="模块">
            <el-select v-model="form.module" placeholder="全部模块" clearable filterable class="full-width">
              <el-option v-for="m in moduleOptions" :key="m.module" :label="`${m.module}（${m.count}）`"
                :value="m.module" />
            </el-select>
          </el-form-item>
          <el-form-item label="优先级">
            <el-select v-model="form.priority" placeholder="全部优先级" clearable class="full-width">
              <el-option v-for="p in PRIORITY_OPTIONS" :key="p" :label="p" :value="p" />
            </el-select>
          </el-form-item>
        </template>

        <el-form-item v-else-if="scopeMode === 'cases'" label="选择用例">
          <el-select v-model="selectedCaseIds" multiple filterable remote reserve-keyword :remote-method="searchCases"
            :loading="caseSearching" placeholder="输入用例名称搜索" class="full-width" :disabled="!form.projectId">
            <el-option v-for="c in caseOptions" :key="c.id" :label="c.name" :value="c.id" />
          </el-select>
          <div class="cron-hint">已选 {{ selectedCaseIds.length }} 个用例</div>
        </el-form-item>

        <el-form-item label="执行环境">
          <el-select v-model="form.environmentId" :placeholder="scopeMode === 'plan' ? '留空则用计划自身配置的环境' : '未指定（用用例自身地址）'"
            clearable class="full-width" :disabled="!form.projectId">
            <el-option v-for="e in environmentOptions" :key="e.id" :label="e.name" :value="e.id" />
          </el-select>
        </el-form-item>

        <el-form-item label="立即启用">
          <el-switch v-model="form.enabled" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, toRefs, watch } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Delete, Plus, Refresh, Search } from '@element-plus/icons-vue'
import { getProjects } from '@/api/project'
import { getEnvironments } from '@/api/environment'
import { getTestCaseModules, getTestCases } from '@/api/testcase'
import { listTestPlansApi } from '@/api/testPlan'
import { ScheduleScopeKind } from '@/types/schedule'
import {
  batchDeleteSchedules, createSchedule, deleteSchedule, getSchedule, getSchedules,
  previewCron, runSchedule, toggleSchedule, updateSchedule,
} from '@/api/schedule'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useRowSelection } from '@/composables/useRowSelection'
import { usePagedList } from '@/composables/usePagedList'
import { formatDateTime } from '@/utils/formatter'
import { CRON_PRESETS, type CronPreviewResult, type ScheduleSummary } from '@/types/schedule'
import { TEST_PLAN_STATUS_LABELS as PLAN_STATUS_LABELS } from '@/types/testPlan'
import type { Project } from '@/types/project'
import type { EnvironmentView } from '@/types/environment'
import type { TestCaseModuleStat, TestCaseSummary } from '@/types/testcase'
import { useBreakpoint } from '@/composables/useBreakpoint'

/** 窄屏（< 1024px）：横向滚动保底，但操作列取消固定（固定列在 375px 下会占满可见宽度） */
const { isMobile } = useBreakpoint()

const PRIORITY_OPTIONS = ['P0', 'P1', 'P2', 'P3']

const projectOptions = ref<Project[]>([])
const projectId = ref('')
const enabledFilter = ref<boolean | undefined>(undefined)
const keyword = ref('')
// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<ScheduleSummary>((p, ps) => getSchedules({
  projectId: projectId.value || undefined,
  enabled: enabledFilter.value,
  keyword: keyword.value || undefined,
  page: p,
  pageSize: ps,
}))
const load = (targetPage?: number) => list.load(targetPage)
const { items: schedules, total, page, pageSize, loading } = toRefs(list)
const runningId = ref('')

const { deleting, selectedRows, onSelectionChange, handleBatchDelete } =
  useBatchDelete<ScheduleSummary>({
    entity: '定时任务',
    remove: batchDeleteSchedules,
    reload: () => load(),
  })

// 点击行直接勾选/取消勾选（复选框列与操作列除外）
const { tableRef, handleRowSelectionClick } = useRowSelection()

// ------------------------------------------------------------ 试跑 / 启停 / 删除

const handleRun = async (row: ScheduleSummary) => {
  runningId.value = row.id
  try {
    const res = await runSchedule(row.id)
    if (res.error) {
      ElMessage.error(res.error)
    } else if (res.message) {
      // 信息性提示（如「计划已有进行中的轮次，本次跳过」）不该弹成错误
      ElMessage.warning(res.message)
    } else {
      // 可能是用例批次，也可能是计划轮次，措辞保持中性
      ElMessage.success(`本次触发已创建 ${res.createdCount} 项内容，可在「执行记录」或计划轮次查看进度`)
    }
    await load()
  } finally {
    runningId.value = ''
  }
}

const handleToggle = async (row: ScheduleSummary) => {
  await toggleSchedule(row.id)
  ElMessage.success(row.enabled ? '已停用' : '已启用')
  await load()
}

const handleDelete = async (row: ScheduleSummary) => {
  await ElMessageBox.confirm(`确定删除定时任务「${row.name}」？`, '删除确认', { type: 'warning' })
  await deleteSchedule(row.id)
  ElMessage.success('已删除')
  await load()
}

// ------------------------------------------------------------ 新建 / 编辑

const dialogVisible = ref(false)
const editingId = ref('')
const formRef = ref<FormInstance>()
const saving = ref(false)
const presetValue = ref('')
const scopeMode = ref<'filter' | 'cases' | 'plan'>('filter')
const selectedPlanIds = ref<string[]>([])
const planOptions = ref<{ id: string; name: string; releaseName?: string | null; status: number }[]>([])

const preview = ref<CronPreviewResult | null>(null)
const moduleOptions = ref<TestCaseModuleStat[]>([])
const environmentOptions = ref<EnvironmentView[]>([])
const caseOptions = ref<TestCaseSummary[]>([])
const selectedCaseIds = ref<string[]>([])
const caseSearching = ref(false)

const form = reactive({
  projectId: '',
  name: '',
  cronExpression: '0 2 * * *',
  enabled: true,
  module: '',
  priority: '',
  environmentId: '',
})

const rules: FormRules = {
  name: [{ required: true, message: '请输入任务名称', trigger: 'blur' }],
  projectId: [{ required: true, message: '请选择所属项目', trigger: 'change' }],
  cronExpression: [
    { required: true, message: '请输入 Cron 表达式', trigger: 'blur' },
    {
      validator: (_rule, value: string, callback) => {
        if (preview.value && !preview.value.valid && preview.value.error) callback(new Error(preview.value.error))
        else callback()
      },
      trigger: 'blur',
    },
  ],
}

const refreshPreview = async () => {
  const expr = form.cronExpression.trim()
  if (!expr) {
    preview.value = null
    return
  }
  try {
    preview.value = await previewCron(expr, 5)
  } catch {
    preview.value = null
  }
}

// 表达式变化时自动预览（简单防抖由输入失焦 + 监听共同承担）
watch(() => form.cronExpression, () => {
  void refreshPreview()
})

const applyPreset = (value: string) => {
  if (value) form.cronExpression = value
}

const resetForm = () => {
  form.projectId = projectId.value || ''
  form.name = ''
  form.cronExpression = '0 2 * * *'
  form.enabled = true
  form.module = ''
  form.priority = ''
  form.environmentId = ''
  presetValue.value = ''
  selectedCaseIds.value = []
  caseOptions.value = []
  scopeMode.value = 'filter'
  selectedPlanIds.value = []
  preview.value = null
}

const onProjectChange = async () => {
  moduleOptions.value = []
  environmentOptions.value = []
  caseOptions.value = []
  selectedCaseIds.value = []
  selectedPlanIds.value = []
  planOptions.value = []
  form.module = ''
  form.priority = ''
  form.environmentId = ''
  if (!form.projectId) return
  await loadPlanOptions()
  const [modules, environments] = await Promise.all([
    getTestCaseModules(form.projectId),
    getEnvironments(form.projectId),
  ])
  moduleOptions.value = modules
  environmentOptions.value = environments
}

const searchCases = async (query: string) => {
  if (!form.projectId) return
  caseSearching.value = true
  try {
    const res = await getTestCases({ projectId: form.projectId, search: query || undefined, page: 1, pageSize: 50 })
    caseOptions.value = res.items
  } finally {
    caseSearching.value = false
  }
}

const openCreate = async () => {
  resetForm()
  editingId.value = ''
  dialogVisible.value = true
  if (form.projectId) await onProjectChange()
  await refreshPreview()
}

/**
 * 加载计划下拉。用分页接口取前 100 条：定时任务一般只关心少数几个计划，
 * 这里不做远程搜索，避免为边缘场景增加一次交互复杂度。
 */
const loadPlanOptions = async () => {
  if (!form.projectId) {
    planOptions.value = []
    return
  }
  try {
    const res = await listTestPlansApi({ projectId: form.projectId, page: 1, pageSize: 100 })
    planOptions.value = res.items.map((p) => ({
      id: p.id, name: p.name, releaseName: p.releaseName, status: p.status,
    }))
  } catch {
    // 没有 ViewTestPlans 权限时留空，不影响按用例的定时任务
    planOptions.value = []
  }
}

const openEdit = async (row: ScheduleSummary) => {
  const detail = await getSchedule(row.id)
  editingId.value = detail.id
  form.projectId = detail.projectId
  form.name = detail.name
  form.cronExpression = detail.cronExpression
  form.enabled = detail.enabled
  form.module = detail.module ?? ''
  form.priority = detail.priority ?? ''
  form.environmentId = detail.environmentId ?? ''
  presetValue.value = CRON_PRESETS.some((p) => p.value === detail.cronExpression)
    ? detail.cronExpression : ''
  scopeMode.value = detail.scopeKind === ScheduleScopeKind.TestPlan
    ? 'plan'
    : detail.testCaseIds.length > 0 ? 'cases' : 'filter'
  selectedCaseIds.value = [...detail.testCaseIds]
  const planIdsToRestore = [...(detail.testPlanIds ?? [])]
  dialogVisible.value = true

  // 放在 onProjectChange 之后回填：那个方法会重置范围内的一切（含计划），先设会被它清掉
  await onProjectChange()
  selectedPlanIds.value = planIdsToRestore
  if (selectedCaseIds.value.length > 0) {
    // 回显已选用例名称
    const res = await getTestCases({ projectId: form.projectId, page: 1, pageSize: 100 })
    caseOptions.value = res.items
  }
  await refreshPreview()
}

const handleSave = async () => {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return

  saving.value = true
  try {
    const payload = {
      name: form.name.trim(),
      cronExpression: form.cronExpression.trim(),
      enabled: form.enabled,
      module: scopeMode.value === 'filter' ? form.module || null : null,
      priority: scopeMode.value === 'filter' ? form.priority || null : null,
      testCaseIds: scopeMode.value === 'cases' ? selectedCaseIds.value : null,
      environmentId: form.environmentId || null,
      scopeKind: scopeMode.value === 'plan' ? ScheduleScopeKind.TestPlan : ScheduleScopeKind.Cases,
      testPlanIds: scopeMode.value === 'plan' ? selectedPlanIds.value : null,
    }
    if (editingId.value) {
      await updateSchedule(editingId.value, payload)
      ElMessage.success('已保存')
    } else {
      await createSchedule({ projectId: form.projectId, ...payload })
      ElMessage.success('已创建')
    }
    dialogVisible.value = false
    await load()
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  const projects = await getProjects({ page: 1, pageSize: 100 })
  projectOptions.value = projects.items
  // 搜索条件「项目」默认为空（全部项目），用户按需筛选
  await load()
})
</script>

<style scoped>
/* 页面撑满视口：卡片自适应高度，表格区域内部滚动，分页固定在底部 */
.schedule-list {
  height: 100%;
  display: flex;
  flex-direction: column;
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
  gap: 12px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

/* 刷新按钮推到工具栏最右 */
.toolbar-spacer {
  flex: 1;
}

.project-select {
  width: 200px;
}

.status-select {
  width: 130px;
}

.keyword-input {
  width: 200px;
}

.cron-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.cron-desc {
  font-weight: 500;
}

.cron-expr {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.option-note {
  margin-left: 10px;
  color: #9aa2ae;
  font-size: 12px;
}

.scope-tag {
  margin-left: 4px;
}

.scope-env {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin-top: 2px;
}

.scope-empty {
  color: var(--el-color-danger);
  font-size: 13px;
}

.sub-text {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.error-text {
  color: var(--el-color-warning);
  font-size: 13px;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}

.full-width {
  width: 100%;
}

.cron-editor {
  display: flex;
  gap: 8px;
  width: 100%;
}

.preset-select {
  width: 230px;
  flex: none;
}

.cron-hint {
  margin-top: 4px;
  font-size: 12px;
  line-height: 1.6;
}

.hint-ok {
  color: var(--el-color-success);
}

.hint-error {
  color: var(--el-color-danger);
}

.hint-next {
  color: var(--el-text-color-secondary);
  margin-left: 8px;
}
</style>
