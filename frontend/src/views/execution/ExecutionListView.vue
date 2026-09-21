<template>
  <div class="execution-list">
    <el-card class="list-card">
      <div class="toolbar">
        <el-select v-model="projectId" placeholder="选择项目" clearable filterable class="project-select" @change="load(1)">
          <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-input v-model="testCaseName" placeholder="用例名称搜索" clearable class="w-180" @keyup.enter="load(1)"
          @clear="load(1)" />
        <el-select v-model="status" placeholder="选择状态" clearable class="status-select" @change="onStatusChange">
          <el-option v-for="(label, value) in EXECUTION_STATUS_LABELS" :key="value" :label="label"
            :value="Number(value)" />
        </el-select>
        <el-date-picker v-model="dateRange" type="daterange" start-placeholder="开始日期" end-placeholder="结束日期"
          value-format="YYYY-MM-DD" class="w-260" @change="load(1)" />
        <el-button type="primary" :icon="Download" :disabled="selectedRows.length === 0" :loading="reporting"
          @click="handleBatchReport">生成报告（{{ selectedRows.length }}）</el-button>
        <el-button type="danger" :icon="Delete" :disabled="selectedRows.length === 0" :loading="deleting"
          @click="handleBatchDelete">
          批量删除（{{ selectedRows.length }}）
        </el-button>
        <el-tag v-for="f in activeFilters" :key="f" class="filter-tag" type="primary" closable
          @close="clearQuickFilters">{{ f }}</el-tag>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" @click="load()">刷新</el-button>
      </div>

      <div v-if="!isMobile" class="table-wrap">
        <el-table ref="tableRef" v-loading="loading" :data="executions" row-key="id" height="100%"
          @selection-change="onSelectionChange" @row-click="handleRowSelectionClick">
          <el-table-column type="selection" width="44" :selectable="isSelectable" />
          <el-table-column label="项目" min-width="140" show-overflow-tooltip>
            <template #default="{ row }">
              <el-button v-if="row.projectName" link type="primary"
                @click.stop="router.push(`/projects/${row.projectId}`)">
                {{ row.projectName }}
              </el-button>
              <span v-else class="muted">(项目已删除)</span>
            </template>
          </el-table-column>
          <el-table-column label="用例名称" min-width="180" show-overflow-tooltip fixed="left">
            <template #default="{ row }">
              <el-link type="primary" :underline="false" @click.stop="router.push(`/executions/${row.id}`)">
                {{ row.testCaseName }}
              </el-link>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="170">
            <template #default="{ row }">
              <el-tag :type="executionStatusTagType(row.status)">
                {{ EXECUTION_STATUS_LABELS[row.status] ?? '未知' }}
              </el-tag>
              <el-tag v-if="row.aiDiagnosis" size="small" type="warning" class="ai-tag"
                :title="row.aiDiagnosis">AI</el-tag>
              <!-- 编排跳过的原因：状态只写「跳过」没法解释「为什么没跑」，看着像执行丢了 -->
              <el-tooltip v-if="row.skipReason" :content="row.skipReason" placement="top">
                <el-tag size="small" type="warning" effect="plain" class="ai-tag">编排</el-tag>
              </el-tooltip>
              <!-- 有前置用例的执行：说明这条在等谁，否则「待执行」久了会被当成卡住 -->
              <el-tooltip v-else-if="row.dependsOnTestCaseId" content="该执行设了前置用例，等前置通过后才开跑" placement="top">
                <el-tag size="small" type="info" effect="plain" class="ai-tag">前置</el-tag>
              </el-tooltip>
            </template>
          </el-table-column>
          <el-table-column label="触发方式" min-width="170">
            <template #default="{ row }">
              <div class="trigger-cell">
                <span>{{ triggerLabels[row.triggerType] ?? '—' }}</span>
                <el-tag v-if="row.triggerSource" size="small" type="info" class="source-tag"
                  :title="row.triggerSource">{{ row.triggerSource }}</el-tag>
              </div>
              <div v-if="row.commitSha || row.branch" class="ci-context" :title="ciTitle(row)">
                <span v-if="row.branch">{{ row.branch }}</span>
                <code v-if="row.commitSha">{{ shortSha(row.commitSha) }}</code>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="浏览器" width="120">
            <template #default="{ row }">
              <el-tag size="small" :type="browserTagType(row.browserName)">
                {{ browserText(row.browserName) }}
              </el-tag>
              <div v-if="row.browserVersion" class="sub-text">{{ row.browserVersion }}</div>
            </template>
          </el-table-column>
          <el-table-column label="数据行" min-width="150" show-overflow-tooltip>
            <template #default="{ row }">
              <span v-if="row.dataSetRowLabel" class="data-row">{{ row.dataSetRowLabel }}</span>
              <span v-else class="sub-text">—</span>
            </template>
          </el-table-column>
          <el-table-column label="耗时" width="100">
            <template #default="{ row }">{{ formatDuration(row.durationMs) }}</template>
          </el-table-column>
          <el-table-column label="开始时间" width="180">
            <template #default="{ row }">{{ formatDateTime(row.startedAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="130" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click="router.push(`/executions/${row.id}`)">详情</el-button>
              <el-button v-if="isRunningRow(row)" link type="danger" :loading="cancelingId === row.id"
                @click="handleCancelRow(row)">终止</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- 窄屏：表格换成卡片（列宽合计 1214px，是 375px 视口的 3.2 倍）。
           批量删除依赖复选框，窄屏不提供；逐条的「详情」保留。 -->
      <MobileCardList v-else v-loading="loading" :items="executions" :row-key="(row) => row.id" empty-text="暂无执行记录">
        <template #title="{ item }">
          <el-link type="primary" :underline="false" @click="router.push(`/executions/${item.id}`)">
            {{ item.testCaseName }}
          </el-link>
        </template>

        <template #badge="{ item }">
          <el-tag :type="executionStatusTagType(item.status)">
            {{ EXECUTION_STATUS_LABELS[item.status] ?? '未知' }}
          </el-tag>
          <el-tag v-if="item.aiDiagnosis" size="small" type="warning">AI 诊断</el-tag>
          <!-- 桌面上这两个标记的说明挂在 tooltip 里；触屏没有 hover，原因直接写到卡上 -->
          <el-tag v-if="item.skipReason" size="small" type="warning" effect="plain">编排跳过</el-tag>
          <el-tag v-else-if="item.dependsOnTestCaseId" size="small" type="info" effect="plain">有前置</el-tag>
        </template>

        <template #meta="{ item }">
          <span><span class="mcl-label">触发</span>{{ triggerLabels[item.triggerType] ?? '—' }}</span>
          <span v-if="item.triggerSource"><span class="mcl-label">来源</span>{{ item.triggerSource }}</span>
          <span v-if="item.branch || item.commitSha">
            <span class="mcl-label">构建</span>{{ [item.branch, item.commitSha ? shortSha(item.commitSha) :
              ''].filter(Boolean).join(' / ') }}
          </span>
          <span><span class="mcl-label">浏览器</span>{{ browserText(item.browserName) }}{{ item.browserVersion ? `
            ${item.browserVersion}` : '' }}</span>
          <span v-if="item.dataSetRowLabel"><span class="mcl-label">数据行</span>{{ item.dataSetRowLabel }}</span>
          <span><span class="mcl-label">耗时</span>{{ formatDuration(item.durationMs) }}</span>
          <span><span class="mcl-label">开始</span>{{ formatDateTime(item.startedAt) }}</span>
        </template>

        <template #actions="{ item }">
          <el-button link type="primary" @click="router.push(`/executions/${item.id}`)">详情</el-button>
          <el-button v-if="isRunningRow(item)" link type="danger" :loading="cancelingId === item.id"
            @click="handleCancelRow(item)">终止</el-button>
        </template>
      </MobileCardList>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize" :total="total"
        :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" @current-change="load()"
        @size-change="load(1)" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, toRefs } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Download, Refresh } from '@element-plus/icons-vue'
import { getProjects } from '@/api/project'
import { cancelExecution, getExecutions, batchDeleteExecutions } from '@/api/execution'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useRowSelection } from '@/composables/useRowSelection'
import { usePagedList } from '@/composables/usePagedList'
import { downloadExecutionsReport, saveBlobAsFile } from '@/api/report'
import { formatDateTime, formatDuration } from '@/utils/formatter'
import {
  ExecutionStatus,
  EXECUTION_STATUS_LABELS,
  executionStatusTagType,
  TriggerType,
  type ExecutionSummary,
} from '@/types/execution'
import type { Project } from '@/types/project'
import MobileCardList from '@/components/common/MobileCardList.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'

const router = useRouter()
const route = useRoute()

/** 窄屏（< 1024px）：表格换成卡片形态，见下方模板 */
const { isMobile } = useBreakpoint()

// 来自仪表盘等入口的预设筛选：近 N 天 / 仅进行中
const days = ref<number | undefined>(
  route.query.days ? Number(route.query.days) : undefined,
)
const runningOnly = ref(route.query.runningOnly === 'true')
/** 从套件页跳转而来：按套件运行批次筛选 */
const suiteRunId = ref<string | undefined>(
  route.query.suiteRunId ? String(route.query.suiteRunId) : undefined,
)

/** 生效中的快捷筛选（用于顶部提示与一键清除） */
const activeFilters = computed(() => {
  const list: string[] = []
  if (days.value) list.push(`近 ${days.value} 天`)
  if (runningOnly.value) list.push('进行中')
  if (suiteRunId.value) list.push('套件运行')
  return list
})

/** 状态下拉与路由同步：切换后写回 query，便于分享/刷新保持筛选 */
const syncStatusQuery = () => {
  void router.replace({
    path: '/executions',
    query: {
      ...route.query,
      ...(status.value !== undefined ? { status: String(status.value) } : { status: undefined }),
    },
  })
}

const clearQuickFilters = () => {
  days.value = undefined
  runningOnly.value = false
  suiteRunId.value = undefined
  void router.replace({ path: '/executions', query: {} })
  void load(1)
}

const projectOptions = ref<Project[]>([])
const projectId = ref('')
/** 用例名称模糊搜索（执行列表新增筛选） */
const testCaseName = ref<string | undefined>(undefined)
/** 开始时间范围 */
const dateRange = ref<string[] | null>(null)
// 支持从仪表盘等入口带预设状态（如「进行中执行」→ 状态=执行中）
const status = ref<number | undefined>(
  route.query.status !== undefined && route.query.status !== ''
    ? Number(route.query.status)
    : undefined,
)
// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<ExecutionSummary>((p, ps) => getExecutions({
  projectId: projectId.value || undefined,
  status: status.value as ExecutionStatus | undefined,
  days: days.value,
  runningOnly: runningOnly.value || undefined,
  suiteRunId: suiteRunId.value,
  testCaseName: testCaseName.value?.trim() || undefined,
  dateFrom: dateRange.value?.[0],
  dateTo: dateRange.value?.[1],
  page: p,
  pageSize: ps,
}))
const load = (targetPage?: number) => list.load(targetPage)
const { items: executions, total, page, pageSize, loading } = toRefs(list)

/** 执行中的记录不可勾选（后端也会二次校验） */
const isSelectable = (row: ExecutionSummary) =>
  row.status !== ExecutionStatus.Pending && row.status !== ExecutionStatus.Running

/** 待执行/执行中的记录可终止 */
const isRunningRow = (row: ExecutionSummary) => !isSelectable(row)

/** 列表内直接终止执行，无需进入详情页 */
const cancelingId = ref('')
const handleCancelRow = async (row: ExecutionSummary) => {
  const confirmed = await ElMessageBox.confirm(
    `确定终止「${row.testCaseName}」的执行吗？正在执行的步骤将被中断，未执行的步骤将标记为跳过。`,
    '终止执行',
    { type: 'warning', confirmButtonText: '终止', cancelButtonText: '取消' },
  ).catch(() => false)
  if (confirmed === false) return
  cancelingId.value = row.id
  try {
    await cancelExecution(row.id)
    ElMessage.success('已发送终止指令')
    load()
  } finally {
    cancelingId.value = ''
  }
}

// 点击行直接勾选/取消勾选（复选框列与操作列除外），与列上的 :selectable 保持一致
const { tableRef, handleRowSelectionClick } = useRowSelection(isSelectable)

const { deleting, selectedRows, onSelectionChange, handleBatchDelete } =
  useBatchDelete<ExecutionSummary>({
    entity: '执行记录',
    remove: batchDeleteExecutions,
    reload: () => load(),
  })

// 批量导出：把选中的执行记录合成一份测试报告
const reporting = ref(false)

const handleBatchReport = async () => {
  const rows = selectedRows.value
  if (rows.length === 0) {
    ElMessage.warning('请先选择要导出的执行记录')
    return
  }
  reporting.value = true
  try {
    const blob = await downloadExecutionsReport(rows.map((row) => row.id))
    saveBlobAsFile(blob, `测试报告_选中${rows.length}条执行.xlsx`)
    ElMessage.success(`已生成包含 ${rows.length} 条执行记录的报告`)
  } finally {
    reporting.value = false
  }
}

/** 切换状态下拉：同步到 URL 并重新查询 */
const onStatusChange = () => {
  syncStatusQuery()
  void load(1)
}

/** 浏览器展示名与配色（多浏览器矩阵结果对照） */
const browserText = (name?: string | null) =>
  ({ chromium: 'Chromium', firefox: 'Firefox', webkit: 'WebKit' }[name ?? ''] ?? name ?? '—')

const browserTagType = (name?: string | null) =>
  ({ firefox: 'warning', webkit: 'success' }[name ?? ''] ?? 'info') as 'warning' | 'success' | 'info'

const triggerLabels: Record<number, string> = {
  [TriggerType.Manual]: '手动',
  [TriggerType.Scheduled]: '定时',
  [TriggerType.CIWebhook]: 'CI',
  [TriggerType.AIRegression]: 'AI 回归',
}

/** 短提交号（前 8 位） */
const shortSha = (sha: string) => (sha.length <= 8 ? sha : sha.slice(0, 8))

/** CI 上下文悬浮完整信息（分支 + 提交 + 构建号 + 来源） */
const ciTitle = (row: ExecutionSummary) => {
  const parts: string[] = []
  if (row.triggerSource) parts.push(`来源：${row.triggerSource}`)
  if (row.branch) parts.push(`分支：${row.branch}`)
  if (row.commitSha) parts.push(`提交：${row.commitSha}`)
  if (row.buildNumber) parts.push(`构建号：${row.buildNumber}`)
  return parts.join('\n')
}

onMounted(async () => {
  const projects = await getProjects({ page: 1, pageSize: 100 })
  projectOptions.value = projects.items
  await load()
})
</script>

<style scoped>
/* 页面撑满视口：卡片自适应高度，表格区域内部滚动，分页固定在底部 */
.execution-list {
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

/* el-card 主体参与纵向布局，表格才能占满剩余高度 */
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
}

/* 刷新按钮推到工具栏最右 */
.toolbar-spacer {
  flex: 1;
}

.project-select {
  width: 200px;
}

.filter-tag {
  margin-left: 4px;
}

.status-select {
  width: 140px;
}

.ai-tag {
  margin-left: 4px;
}

.trigger-cell {
  display: flex;
  align-items: center;
  gap: 6px;
}

.source-tag {
  max-width: 110px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ci-context {
  display: flex;
  gap: 6px;
  margin-top: 2px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.ci-context code {
  font-size: 12px;
}

.data-row {
  font-size: 12px;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
