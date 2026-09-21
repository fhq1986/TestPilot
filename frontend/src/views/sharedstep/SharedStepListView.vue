<template>
  <div class="shared-steps">
    <el-card class="list-card">
      <div class="toolbar">
        <div class="toolbar-left">
          <el-select v-model="projectId" placeholder="全部项目" clearable class="w-200" @change="() => load(1)">
            <el-option v-for="p in projects" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
          <el-input v-model="search" placeholder="搜索名称" clearable class="w-200" @keyup.enter="load" @clear="load" />
        </div>
        <div class="toolbar-right">
          <el-button type="primary" :icon="Plus" :disabled="!projectId" @click="openCreate">
            新建共享步骤组
          </el-button>
          <el-button type="danger" :icon="Delete" :disabled="selectedRows.length === 0" :loading="deleting"
            @click="handleBatchDelete">
            批量删除（{{ selectedRows.length }}）
          </el-button>
          <el-button :icon="Refresh" @click="load()">刷新</el-button>
        </div>
      </div>

      <el-alert type="warning" :closable="false" class="tip">
        <template #title>
          共享步骤组是「改一处、全部用例生效」的复用单元。用例通过引用它的方式使用，
          步骤在<b>运行时</b>才展开——因此这里改了内容，所有引用它的用例下次执行就都用新内容。
        </template>
      </el-alert>

      <div class="table-wrap">
        <el-table ref="tableRef" v-loading="loading" :data="groups" row-key="id" height="100%"
          @selection-change="onSelectionChange" @row-click="handleRowSelectionClick">
          <el-table-column type="selection" width="44" />
          <el-table-column label="名称" min-width="180" fixed="left">
            <template #default="{ row }">
              <!-- 无独立详情页，编辑弹窗即该步骤组的完整视图（步骤、变量、被引用） -->
              <el-link type="primary" :underline="false" @click.stop="openEdit(row)">{{ row.name }}</el-link>
            </template>
          </el-table-column>
          <el-table-column prop="description" label="描述" min-width="240" show-overflow-tooltip />
          <el-table-column label="步骤数" width="90" align="center">
            <template #default="{ row }">{{ row.itemCount }}</template>
          </el-table-column>
          <el-table-column label="变量" width="110">
            <template #default="{ row }">
              <el-tag v-if="row.variables.length" size="small" effect="plain" type="info">
                {{ row.variables.length }} 个
              </el-tag>
              <span v-else class="muted">—</span>
            </template>
          </el-table-column>
          <el-table-column label="被引用" width="120" align="center">
            <template #default="{ row }">
              <el-button v-if="row.usedByCaseCount > 0" link type="primary" @click.stop="showUsages(row)">
                {{ row.usedByCaseCount }} 个用例
              </el-button>
              <span v-else class="muted">未被引用</span>
            </template>
          </el-table-column>
          <el-table-column label="创建人" width="110" show-overflow-tooltip>
            <template #default="{ row }">
              <span v-if="row.createdByName">{{ row.createdByName }}</span>
              <span v-else>-</span>
            </template>
          </el-table-column>
          <el-table-column label="更新时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.updatedAt ?? row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="190" :fixed="isMobile ? false : 'right'">
            <template #default="{ row }">
              <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
              <el-button link type="primary" @click="openCopy(row)">复制</el-button>
              <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize" :total="total"
        :page-sizes="[10, 20, 50, 100]" layout="total, sizes, prev, pager, next" @current-change="load()"
        @size-change="load(1)" />
    </el-card>

    <!-- 新建 / 编辑 -->
    <el-dialog v-model="dialogVisible" :title="editingId ? '编辑共享步骤组' : '新建共享步骤组'" width="820px" top="6vh"
      @closed="resetForm">
      <el-form :model="form" label-width="88px">
        <el-form-item label="名称" required>
          <el-input v-model="form.name" placeholder="例如「登录并进入工作台」" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" type="textarea" :rows="2" placeholder="可选，说明适用场景" />
        </el-form-item>

        <el-form-item label="变量">
          <div class="var-block">
            <div class="var-hint">
              组内步骤可用 <code v-pre>{{变量名}}</code> 引用；引用它的用例可以逐项覆盖。
              未被覆盖时使用这里的默认值。
            </div>
            <div v-for="(v, i) in form.variables" :key="i" class="var-row">
              <el-input v-model="v.name" placeholder="变量名，如 username" class="var-name" />
              <el-input v-model="v.value" placeholder="默认值" class="var-value" />
              <el-button link type="danger" :icon="Delete" @click="form.variables.splice(i, 1)" />
            </div>
            <el-button link type="primary" :icon="Plus" @click="addVariable">添加变量</el-button>
          </div>
        </el-form-item>

        <el-form-item label="步骤">
          <div class="steps-block">
            <el-table :data="form.items" size="small" border max-height="320">
              <el-table-column type="index" label="#" width="46" />
              <el-table-column label="动作" width="120">
                <template #default="{ row }">
                  <el-select v-model="row.actionType" size="small">
                    <el-option v-for="(label, value) in ACTION_TYPE_LABELS" :key="value" :label="label"
                      :value="Number(value)" />
                  </el-select>
                </template>
              </el-table-column>
              <el-table-column label="配置（地址 / 选择器 / 输入值）" min-width="280">
                <template #default="{ row }">
                  <el-input v-model="row.cfgText" size="small" :placeholder="configPlaceholder(row.actionType)" />
                </template>
              </el-table-column>
              <el-table-column label="" width="60" align="center">
                <template #default="{ $index }">
                  <el-button link type="danger" :icon="Delete" @click="removeItem($index)" />
                </template>
              </el-table-column>
            </el-table>
            <el-button link type="primary" :icon="Plus" class="add-step" @click="addItem">
              添加步骤
            </el-button>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 引用清单 -->
    <el-dialog v-model="usageVisible" title="被以下用例引用" width="560px">
      <el-table v-loading="usageLoading" :data="usages" size="small">
        <el-table-column prop="name" label="用例名称" min-width="240" />
        <el-table-column label="所在步骤" width="180">
          <template #default="{ row }">
            {{row.stepOrders.map((o: number) => `第 ${o + 1} 步`).join('、')}}
          </template>
        </el-table-column>
        <el-table-column label="" width="90">
          <template #default="{ row }">
            <el-button link type="primary" @click="goCase(row.testCaseId)">查看</el-button>
          </template>
        </el-table-column>
      </el-table>
      <p class="usage-hint">
        删除该组后，这些用例中引用它的步骤会变成失效引用，执行时会告警并跳过，需重新编辑。
      </p>
      <template #footer>
        <el-button @click="usageVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Plus, Refresh } from '@element-plus/icons-vue'
import {
  batchDeleteSharedStepsApi, createSharedStepGroupApi, deleteSharedStepGroupApi, getSharedStepGroupApi,
  listSharedStepGroupsApi, sharedStepUsagesApi, updateSharedStepGroupApi,
  type SharedStepGroupView, type SharedStepUsage, type SharedVariable,
} from '@/api/sharedStep'
import { getProjects } from '@/api/project'
import { ActionType, ACTION_TYPE_LABELS, type StepConfig } from '@/types/testcase'
import { formatDateTime } from '@/utils/formatter'
import type { Project } from '@/types/project'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { usePagedList } from '@/composables/usePagedList'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useRowSelection } from '@/composables/useRowSelection'

/** 编辑期的步骤行：把 Config 摊平成单个输入框，保存时再归位到对应字段 */
interface ItemRow {
  actionType: ActionType
  cfgText: string
}

const router = useRouter()

/** 窄屏（< 1024px）：横向滚动保底，但操作列取消固定（固定列在 375px 下会占满可见宽度） */
const { isMobile } = useBreakpoint()
const projects = ref<Project[]>([])
const saving = ref(false)
const projectId = ref('')
const search = ref('')
// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<SharedStepGroupView>((p, ps) => listSharedStepGroupsApi({
  projectId: projectId.value || undefined,
  search: search.value.trim() || undefined,
  page: p,
  pageSize: ps,
}), { pageSize: 20 })
const load = (targetPage?: number) => list.load(targetPage)
const { items: groups, total, page, pageSize, loading } = toRefs(list)

// 批量删除（引用方不阻断：外键 SetNull，运行时展开器会告警）
const { deleting, selectedRows, onSelectionChange, handleBatchDelete } =
  useBatchDelete<SharedStepGroupView>({
    entity: '共享步骤组',
    remove: batchDeleteSharedStepsApi,
    reload: load,
  })

// 点击行直接勾选/取消勾选（复选框列与操作列除外）
const { tableRef, handleRowSelectionClick } = useRowSelection()

const dialogVisible = ref(false)
const editingId = ref<string | null>(null)
const form = reactive({
  name: '',
  description: '',
  variables: [] as SharedVariable[],
  items: [] as ItemRow[],
})

const usageVisible = ref(false)
const usageLoading = ref(false)
const usages = ref<SharedStepUsage[]>([])

const configPlaceholder = (action: ActionType) =>
  action === ActionType.Navigate ? 'https://example.com/login'
    : action === ActionType.Fill ? '选择器｜输入值（可用 {{变量}}）'
      : action === ActionType.Wait ? '毫秒数'
        : '支持 CSS / xpath= / {{变量}}'

/** 把行内文本按动作归位到 StepConfig 的对应字段（与用例编辑页保持同一套约定） */
function toConfig(action: ActionType, text: string): StepConfig {
  const value = text.trim()
  switch (action) {
    case ActionType.Navigate:
      return { url: value }
    case ActionType.Wait:
      return { value }
    case ActionType.Fill: {
      // 「选择器｜输入值」双段写法，未写分隔符时整体当选择器
      const [selector, ...rest] = value.split('｜')
      return {
        selector: { type: selector.includes('xpath=') ? 'xpath' : 'css', value: selector.trim() },
        value: rest.join('｜').trim(),
      }
    }
    default:
      return {
        selector: {
          type: value.includes('xpath=') ? 'xpath' : 'css',
          value: value.replace(/^xpath=/, ''),
        },
      }
  }
}

function fromConfig(action: ActionType, config: StepConfig): string {
  if (action === ActionType.Navigate) return config.url ?? ''
  if (action === ActionType.Wait) return config.value ?? ''
  if (action === ActionType.Fill) {
    return [config.selector?.value ?? '', config.value ?? ''].filter(Boolean).join('｜')
  }
  return config.selector?.value ?? config.selector?.description ?? ''
}

async function loadProjects() {
  const res = await getProjects({ page: 1, pageSize: 100 })
  projects.value = res.items
  // 搜索条件「项目」默认为空（全部项目），用户按需筛选
}

function resetForm() {
  editingId.value = null
  Object.assign(form, { name: '', description: '', variables: [], items: [] })
}

function openCreate() {
  resetForm()
  dialogVisible.value = true
}

function addItem() {
  form.items.push({ actionType: ActionType.Click, cfgText: '' })
}

function removeItem(index: number) {
  form.items.splice(index, 1)
}

function addVariable() {
  form.variables.push({ name: '', value: '' })
}

async function openEdit(row: SharedStepGroupView) {
  const detail = await getSharedStepGroupApi(row.id)
  editingId.value = detail.id
  form.name = detail.name
  form.description = detail.description ?? ''
  form.variables = detail.variables.map((v) => ({ name: v.name, value: v.value ?? '' }))
  form.items = detail.items.map((i) => ({
    actionType: i.actionType,
    cfgText: fromConfig(i.actionType, i.config),
  }))
  dialogVisible.value = true
}

async function openCopy(row: SharedStepGroupView) {
  await openEdit(row)
  // 复制语义：换个名字，落到当前选中的项目
  editingId.value = null
  form.name = `${row.name} 副本`
}

async function handleSave() {
  if (!form.name.trim()) {
    ElMessage.warning('请填写名称')
    return
  }
  const items = form.items.map((row, index) => ({
    stepOrder: index,
    actionType: row.actionType,
    config: toConfig(row.actionType, row.cfgText),
  }))

  saving.value = true
  try {
    const payload = {
      projectId: projectId.value,
      name: form.name.trim(),
      description: form.description || null,
      items,
      variables: form.variables.filter((v) => v.name.trim()),
    }
    if (editingId.value) {
      await updateSharedStepGroupApi(editingId.value, payload)
      ElMessage.success('已保存，引用它的用例下次执行即生效')
    } else {
      await createSharedStepGroupApi(payload)
      ElMessage.success('已创建')
    }
    dialogVisible.value = false
    await load()
  } finally {
    saving.value = false
  }
}

async function showUsages(row: SharedStepGroupView) {
  usageVisible.value = true
  usageLoading.value = true
  try {
    usages.value = await sharedStepUsagesApi(row.id)
  } finally {
    usageLoading.value = false
  }
}

function goCase(testCaseId: string) {
  router.push(`/testcases/${testCaseId}`)
}

async function handleDelete(row: SharedStepGroupView) {
  // 有引用时先让用户看到影响面，确认成本比一句"确定删除吗"高得多，但能避免误删
  if (row.usedByCaseCount > 0) {
    const list = await sharedStepUsagesApi(row.id)
    const names = list.slice(0, 5).map((u) => `「${u.name}」`).join('、')
    const more = list.length > 5 ? ` 等 ${list.length} 个用例` : ''
    await ElMessageBox.confirm(
      `共享步骤组「${row.name}」正被 ${names}${more} 引用。删除后这些用例中引用它的步骤会变成失效引用（执行时告警并跳过），需要手工重新编辑。确定删除？`,
      '删除被引用的共享步骤组',
      { type: 'warning', confirmButtonText: '仍然删除', confirmButtonClass: 'el-button--danger' },
    )
  } else {
    await ElMessageBox.confirm(`确定删除共享步骤组「${row.name}」？`, '删除', { type: 'warning' })
  }

  const result = await deleteSharedStepGroupApi(row.id)
  ElMessage.success(result.message || '已删除')
  usageVisible.value = false
  await load()
}

onMounted(async () => {
  await loadProjects()
  await load()
})
</script>

<style scoped>
.shared-steps {
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
  flex-wrap: wrap;
}

.toolbar-left {
  display: flex;
  gap: 10px;
}

.toolbar-right {
  display: flex;
  gap: 10px;
}

.w-200 {
  width: 200px;
}

.pagination {
  margin-top: 12px;
  justify-content: flex-end;
}

.tip {
  margin-bottom: 12px;
}

.table-wrap {
  flex: 1;
  min-height: 0;
}

.muted {
  color: #9aa2ae;
}

.var-block,
.steps-block {
  width: 100%;
}

.var-hint {
  color: #7a8290;
  font-size: 13px;
  margin-bottom: 8px;
  line-height: 1.7;
}

.var-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
  align-items: center;
}

.var-name {
  width: 200px;
}

.var-value {
  flex: 1;
}

.add-step {
  margin-top: 8px;
}

.usage-hint {
  color: #7a8290;
  font-size: 13px;
  margin: 12px 0 0;
  line-height: 1.7;
}
</style>
