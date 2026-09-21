<template>
  <div class="dataset-page">
    <el-card class="list-card">
      <el-alert type="warning" :closable="false" class="boundary-tip">
        <template #title>
          数据集用于参数化：用例步骤里写 <code v-pre>{{列名}}</code>，执行时按数据行展开
        </template>
      </el-alert>
      <div class="toolbar">
        <el-select v-model="projectId" placeholder="选择项目" clearable filterable class="project-select" @change="load(1)">
          <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-input v-model="keyword" placeholder="搜索数据集名称" clearable class="keyword-input" :prefix-icon="Search"
          @keyup.enter="load(1)" @clear="load(1)" />
        <el-button type="primary" :icon="Plus" @click="openCreate">新建数据集</el-button>
        <el-button type="danger" :icon="Delete" :disabled="selectedRows.length === 0" :loading="deleting"
          @click="handleBatchDelete">
          批量删除（{{ selectedRows.length }}）
        </el-button>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" @click="load()">刷新</el-button>
      </div>

      <div class="table-wrap">
        <el-table ref="tableRef" v-loading="loading" :data="dataSets" row-key="id" height="100%"
          @selection-change="onSelectionChange" @row-click="handleRowSelectionClick">
          <el-table-column type="selection" width="44" />
          <el-table-column label="名称" min-width="180" show-overflow-tooltip fixed="left">
            <template #default="{ row }">
              <!-- 无独立详情页，编辑弹窗即该数据集的完整视图 -->
              <el-link type="primary" :underline="false" @click.stop="openEdit(row)">{{ row.name }}</el-link>
            </template>
          </el-table-column>
          <!-- 所属项目：跨项目看数据集时靠它区分归属；点项目名直接进项目详情 -->
          <el-table-column label="所属项目" min-width="140" show-overflow-tooltip>
            <template #default="{ row }">
              <el-button v-if="row.projectName" link type="primary"
                @click.stop="router.push(`/projects/${row.projectId}`)">
                {{ row.projectName }}
              </el-button>
              <span v-else class="muted">—</span>
            </template>
          </el-table-column>
          <el-table-column label="规模" width="150">
            <template #default="{ row }">
              <el-tag size="small" type="info">{{ row.columnCount }} 列</el-tag>
              <el-tag size="small" class="inline-tag">{{ row.rowCount }} 行</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="被引用" width="120">
            <template #default="{ row }">
              <el-tag v-if="row.usedByCaseCount > 0" size="small" type="warning">
                {{ row.usedByCaseCount }} 条用例
              </el-tag>
              <span v-else class="muted">未被引用</span>
            </template>
          </el-table-column>
          <el-table-column prop="description" label="说明" min-width="200" show-overflow-tooltip>
            <template #default="{ row }">{{ row.description || '—' }}</template>
          </el-table-column>
          <el-table-column label="创建人" width="110" show-overflow-tooltip>
            <template #default="{ row }">
              <span v-if="row.createdByName">{{ row.createdByName }}</span>
              <span v-else>-</span>
            </template>
          </el-table-column>
          <el-table-column label="创建时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="更新时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.updatedAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="180" :fixed="isMobile ? false : 'right'">
            <template #default="{ row }">
              <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
              <el-button link type="primary" @click="openUsage(row)">引用用例</el-button>
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
    <el-dialog v-model="dialogVisible" :title="editingId ? '编辑数据集' : '新建数据集'" width="900px" destroy-on-close top="6vh">
      <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
        <div class="form-row">
          <el-form-item label="名称" prop="name" class="form-name">
            <el-input v-model="form.name" placeholder="如：账号矩阵" maxlength="200" />
          </el-form-item>
          <el-form-item label="所属项目" prop="projectId" class="form-project">
            <el-select v-model="form.projectId" placeholder="选择项目" filterable :disabled="!!editingId">
              <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item label="说明">
          <el-input v-model="form.description" placeholder="用途说明（可选）" maxlength="2000" />
        </el-form-item>

        <el-form-item label="数据表">
          <div class="table-editor">
            <div class="editor-actions">
              <el-upload :auto-upload="false" :show-file-list="false" accept=".xlsx,.csv,.txt"
                :on-change="onImportFile">
                <el-button size="small" :icon="Upload" :loading="importing">从 Excel/CSV 导入</el-button>
              </el-upload>
              <el-button size="small" :icon="Plus" @click="addColumn">添加列</el-button>
              <el-button size="small" :icon="Plus" @click="addRow">添加行</el-button>
              <span class="editor-hint">
                首行是列名；共 {{ form.columns.length }} 列 / {{ form.rows.length }} 行（上限 50 列、500 行）
              </span>
            </div>

            <el-table :data="form.rows" size="small" max-height="360" border class="editor-table">
              <el-table-column type="index" label="#" width="50" />
              <el-table-column v-for="(column, index) in form.columns" :key="index" :label="column" min-width="150">
                <template #header>
                  <div class="col-header">
                    <el-input v-model="form.columns[index]" size="small" @change="onColumnRename(index, $event)" />
                    <el-button link type="danger" :icon="Delete" @click="removeColumn(index)" />
                  </div>
                </template>
                <template #default="{ row }">
                  <el-input v-model="row[column]" size="small" placeholder="（空）" />
                </template>
              </el-table-column>
              <el-table-column label="操作" width="70" :fixed="isMobile ? false : 'right'">
                <template #default="{ $index }">
                  <el-button link type="danger" :icon="Delete" @click="removeRow($index)" />
                </template>
              </el-table-column>
              <template #empty>
                <div class="editor-empty">
                  还没有数据。可「从 Excel/CSV 导入」，或先「添加列」再「添加行」手工填写。
                </div>
              </template>
            </el-table>
          </div>
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 引用用例 -->
    <el-dialog v-model="usageVisible" title="引用该数据集的用例" width="560px">
      <el-alert v-if="usageList.length === 0" type="info" :closable="false" title="暂无用例引用该数据集" />
      <el-table v-else :data="usageList" size="small" max-height="360">
        <el-table-column prop="name" label="用例名称" min-width="200" show-overflow-tooltip />
        <el-table-column prop="module" label="模块" width="160">
          <template #default="{ row }">{{ row.module || '—' }}</template>
        </el-table-column>
        <el-table-column label="操作" width="90">
          <template #default="{ row }">
            <el-button link type="primary" @click="router.push(`/testcases/${row.testCaseId}`)">查看</el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-alert v-if="usageList.length > 0" class="usage-tip" type="warning" :closable="false"
        title="修改列名会导致这些用例的变量无法替换，请同步调整用例步骤。" />
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Delete, Plus, Refresh, Search, Upload } from '@element-plus/icons-vue'
import type { UploadFile } from 'element-plus'
import { getProjects } from '@/api/project'
import {
  batchDeleteDataSets, createDataSet, deleteDataSet, getDataSet, getDataSets, importDataSetFile, updateDataSet,
} from '@/api/dataset'
import { formatDateTime } from '@/utils/formatter'
import type { DataSetSummary, DataSetUsage } from '@/types/dataset'
import type { Project } from '@/types/project'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { usePagedList } from '@/composables/usePagedList'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useRowSelection } from '@/composables/useRowSelection'

const router = useRouter()

/**
 * 窄屏（< 1024px）。本页走「横向滚动」保底方案，不卡片化 —— 数据集是低频管理页。
 * 但操作列必须取消固定：fixed="right" 的列在 375px 下会占满整个可见宽度，
 * 数据列反而全被挤到屏幕外。桌面上 1024px 以上仍照旧固定。
 */
const { isMobile } = useBreakpoint()
const projectOptions = ref<Project[]>([])
const projectId = ref('')
const keyword = ref('')
// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<DataSetSummary>((p, ps) => getDataSets({
  projectId: projectId.value || undefined,
  keyword: keyword.value || undefined,
  page: p,
  pageSize: ps,
}))
const load = (targetPage?: number) => list.load(targetPage)
const { items: dataSets, total, page, pageSize, loading } = toRefs(list)

// 批量删除（引用数据集的用例不受影响，外键 SetNull 只是不再参数化）
const { deleting, selectedRows, onSelectionChange, handleBatchDelete } =
  useBatchDelete<DataSetSummary>({
    entity: '数据集',
    remove: batchDeleteDataSets,
    reload: load,
  })

// 点击行直接勾选/取消勾选（复选框列与操作列除外）
const { tableRef, handleRowSelectionClick } = useRowSelection()

const dialogVisible = ref(false)
const editingId = ref('')
const formRef = ref<FormInstance>()
const saving = ref(false)
const importing = ref(false)
const usageVisible = ref(false)
const usageList = ref<DataSetUsage[]>([])

const form = reactive({
  projectId: '',
  name: '',
  description: '',
  columns: [] as string[],
  rows: [] as Record<string, string>[],
})

const rules: FormRules = {
  name: [{ required: true, message: '请输入数据集名称', trigger: 'blur' }],
  projectId: [{ required: true, message: '请选择所属项目', trigger: 'change' }],
}

// ------------------------------------------------------------ 编辑

const resetForm = () => {
  form.projectId = projectId.value || projectOptions.value[0]?.id || ''
  form.name = ''
  form.description = ''
  // 默认给一组「账号密码」列，降低上手成本
  form.columns = ['username', 'password']
  form.rows = [{ username: '', password: '' }]
}

const openCreate = () => {
  resetForm()
  editingId.value = ''
  dialogVisible.value = true
}

const openEdit = async (row: DataSetSummary) => {
  const detail = await getDataSet(row.id)
  editingId.value = detail.id
  form.projectId = detail.projectId
  form.name = detail.name
  form.description = detail.description ?? ''
  form.columns = [...detail.columns]
  form.rows = detail.rows.map((r) => ({ ...r }))
  dialogVisible.value = true
}

const addColumn = () => {
  if (form.columns.length >= 50) {
    ElMessage.warning('列数不能超过 50')
    return
  }
  const name = `列${form.columns.length + 1}`
  form.columns.push(name)
  form.rows.forEach((r) => (r[name] = ''))
}

const removeColumn = (index: number) => {
  const [removed] = form.columns.splice(index, 1)
  form.rows.forEach((r) => delete r[removed])
}

/** 改列名时把已有数据一起搬到新列名下，避免数据丢失 */
const onColumnRename = (index: number, next: string) => {
  const previous = form.columns[index]
  const name = (next || '').trim()
  if (!name) {
    ElMessage.warning('列名不能为空')
    form.columns[index] = previous
    return
  }
  if (name !== previous) {
    form.rows.forEach((r) => {
      r[name] = r[previous] ?? ''
      delete r[previous]
    })
    form.columns[index] = name
  }
}

const addRow = () => {
  if (form.rows.length >= 500) {
    ElMessage.warning('数据行不能超过 500')
    return
  }
  const row: Record<string, string> = {}
  form.columns.forEach((c) => (row[c] = ''))
  form.rows.push(row)
}

const removeRow = (index: number) => form.rows.splice(index, 1)

const onImportFile = async (file: UploadFile) => {
  const raw = file.raw as File | undefined
  if (!raw) return
  importing.value = true
  try {
    const result = await importDataSetFile(raw)
    form.columns = result.columns
    form.rows = result.rows
    if (!form.name) form.name = raw.name.replace(/\.[^.]+$/, '')
    const skipped = result.skippedEmptyRows > 0 ? `，跳过空行 ${result.skippedEmptyRows}` : ''
    ElMessage.success(`已解析 ${result.columns.length} 列 / ${result.rows.length} 行${skipped}`)
    result.warnings.forEach((w) => ElMessage.warning(w))
  } catch {
    // 解析失败已由拦截器提示
  } finally {
    importing.value = false
  }
}

const handleSave = async () => {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return
  if (form.columns.length === 0) {
    ElMessage.warning('至少需要一列')
    return
  }

  saving.value = true
  try {
    const payload = {
      name: form.name.trim(),
      description: form.description.trim() || null,
      columns: form.columns.map((c) => c.trim()),
      rows: form.rows,
    }
    if (editingId.value) {
      await updateDataSet(editingId.value, payload)
      ElMessage.success('已保存')
    } else {
      await createDataSet({ projectId: form.projectId, ...payload })
      ElMessage.success('已创建')
    }
    dialogVisible.value = false
    await load()
  } finally {
    saving.value = false
  }
}

const openUsage = async (row: DataSetSummary) => {
  const detail = await getDataSet(row.id)
  usageList.value = detail.usedBy
  usageVisible.value = true
}

const handleDelete = async (row: DataSetSummary) => {
  if (row.usedByCaseCount > 0) {
    try {
      await ElMessageBox.confirm(
        `已有 ${row.usedByCaseCount} 条用例引用「${row.name}」，删除后这些用例将不再参数化（用例本身保留）。确认删除？`,
        '删除数据集', { type: 'warning' })
    } catch {
      return
    }
  } else {
    try {
      await ElMessageBox.confirm(`确认删除数据集「${row.name}」？`, '删除数据集', { type: 'warning' })
    } catch {
      return
    }
  }
  await deleteDataSet(row.id)
  ElMessage.success('已删除')
  await load()
}

onMounted(async () => {
  const projects = await getProjects({ page: 1, pageSize: 100 })
  projectOptions.value = projects.items
  // 搜索条件「项目」默认为空（全部项目），用户按需筛选
  await load()
})
</script>

<style scoped>
.dataset-page {
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
  align-items: center;
  flex-wrap: wrap;
}

/* 刷新按钮推到工具栏最右 */
.toolbar-spacer {
  flex: 1;
}

.project-select {
  width: 200px;
}

.keyword-input {
  width: 200px;
}

.toolbar-hint {
  margin-left: auto;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.toolbar-hint code {
  background: var(--el-fill-color-light);
  padding: 1px 5px;
  border-radius: 3px;
}

.inline-tag {
  margin-left: 4px;
}

.muted {
  color: var(--el-text-color-placeholder);
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}

.form-row {
  display: flex;
  gap: 16px;
}

.form-name {
  flex: 1;
}

.form-project {
  width: 260px;
}

.table-editor {
  width: 100%;
}

.editor-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.editor-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.editor-table {
  width: 100%;
}

.col-header {
  display: flex;
  align-items: center;
  gap: 4px;
}

.editor-empty {
  padding: 16px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.usage-tip {
  margin-top: 12px;
}

.boundary-tip {
  margin-bottom: 10px;
}
</style>
