<template>
  <div class="suite-page">
    <el-card class="list-card">

      <div class="toolbar">
        <el-select v-model="projectId" placeholder="选择项目" clearable filterable class="project-select" @change="load(1)">
          <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-select v-model="kindFilter" placeholder="套件类型" clearable class="kind-select" @change="load(1)">
          <el-option v-for="(label, value) in SUITE_KIND_LABELS" :key="value" :label="label" :value="Number(value)" />
        </el-select>
        <el-input v-model="keyword" placeholder="搜索套件名称" clearable class="keyword-input" :prefix-icon="Search"
          @keyup.enter="load(1)" @clear="load(1)" />
        <el-button type="primary" :icon="Plus" @click="openCreate">新建套件</el-button>
        <el-button type="danger" :icon="Delete" :disabled="selectedRows.length === 0" :loading="deleting"
          @click="handleBatchDelete">
          批量删除（{{ selectedRows.length }}）
        </el-button>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" @click="load()">刷新</el-button>
      </div>
      <el-alert type="warning" :closable="false" class="boundary-tip">
        <template #title>
          <b>测试套件</b>是「一组用例」，长期存在、内容随用例演进，点一下就跑。
          需要带目标通过率、起止时间与多轮次执行记录的<b>验收过程</b>，请用
          <el-link type="primary" :underline="false" @click="router.push('/test-plans')">测试计划</el-link>。
        </template>
      </el-alert>
      <div v-if="!isMobile" class="table-wrap">
        <el-table ref="tableRef" v-loading="loading" :data="suites" row-key="id" height="100%"
          @selection-change="onSelectionChange" @row-click="handleRowSelectionClick">
          <el-table-column type="selection" width="44" />
          <el-table-column label="套件名称" min-width="180" show-overflow-tooltip fixed="left">
            <template #default="{ row }">
              <!-- 无独立详情页，编辑弹窗即该套件的完整配置视图 -->
              <el-link type="primary" :underline="false" @click.stop="openEdit(row)">{{ row.name }}</el-link>
            </template>
          </el-table-column>
          <el-table-column label="类型" width="110">
            <template #default="{ row }">
              <el-tag :type="kindTagType(row.kind)" size="small">{{ SUITE_KIND_LABELS[row.kind] }}</el-tag>
            </template>
          </el-table-column>
          <!-- 失败策略：快停会明显减少本轮执行条数，列表上要先看到，否则看到「跳过几十条」会以为是故障 -->
          <el-table-column label="失败策略" width="110">
            <template #default="{ row }">
              <el-tag v-if="row.failurePolicy === SuiteFailurePolicy.StopOnFailure" type="danger" size="small"
                effect="plain">失败快停</el-tag>
              <el-tag v-else type="info" size="small" effect="plain">继续执行</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="用例数" width="90">
            <template #default="{ row }">
              <el-tag size="small" type="info">{{ row.caseCount }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="环境" width="130">
            <template #default="{ row }">{{ row.environmentName || '未指定' }}</template>
          </el-table-column>
          <el-table-column label="上次运行" width="170">
            <template #default="{ row }">
              <div>{{ row.lastRunAt ? formatDateTime(row.lastRunAt) : '—' }}</div>
              <div v-if="row.lastRunAt" class="sub-text">创建 {{ row.lastCreatedCount }} 条执行</div>
            </template>
          </el-table-column>
          <el-table-column label="最近异常" min-width="150" show-overflow-tooltip>
            <template #default="{ row }">
              <el-tooltip v-if="row.lastError" :content="row.lastError" placement="top">
                <span class="error-text">{{ row.lastError }}</span>
              </el-tooltip>
              <span v-else class="sub-text">—</span>
            </template>
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
          <el-table-column label="操作" width="280" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" :loading="runningId === row.id" @click="openRun(row)">运行</el-button>
              <el-button link type="primary" @click="openHistory(row)">历史</el-button>
              <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
              <el-button link type="primary" @click="shareRun(row)">报告链接</el-button>
              <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- 窄屏：表格换成卡片（列宽合计 1220px，是 375px 视口的 3.3 倍）。
           桌面版操作列 280px 且 fixed="right"，窄屏固定列会占满宽度，卡片自己排按钮 -->
      <MobileCardList v-else v-loading="loading" :items="suites" :row-key="(row) => row.id" empty-text="暂无套件">
        <template #title="{ item }">
          <el-link type="primary" :underline="false" @click="openEdit(item)">{{ item.name }}</el-link>
        </template>

        <template #badge="{ item }">
          <el-tag :type="kindTagType(item.kind)" size="small">{{ SUITE_KIND_LABELS[item.kind] }}</el-tag>
          <el-tag v-if="item.failurePolicy === SuiteFailurePolicy.StopOnFailure" type="danger" size="small"
            effect="plain">失败快停</el-tag>
          <el-tag v-else type="info" size="small" effect="plain">继续执行</el-tag>
          <el-tag size="small" type="info">{{ item.caseCount }} 条用例</el-tag>
        </template>

        <template #meta="{ item }">
          <span><span class="mcl-label">环境</span>{{ item.environmentName || '未指定' }}</span>
          <span><span class="mcl-label">上次运行</span>{{ item.lastRunAt ? formatDateTime(item.lastRunAt) : '—' }}</span>
          <span v-if="item.lastRunAt"><span class="mcl-label">创建执行</span>{{ item.lastCreatedCount }} 条</span>
          <!-- 桌面版把异常挂在 tooltip 里；触屏没有 hover，直接展开在卡上 -->
          <span v-if="item.lastError" class="error-text">
            <span class="mcl-label">最近异常</span>{{ item.lastError }}
          </span>
        </template>

        <template #actions="{ item }">
          <el-button link type="primary" :loading="runningId === item.id" @click="openRun(item)">运行</el-button>
          <el-button link type="primary" @click="openHistory(item)">历史</el-button>
          <el-button link type="primary" @click="openEdit(item)">编辑</el-button>
          <el-button link type="primary" @click="shareRun(item)">报告链接</el-button>
          <el-button link type="danger" @click="handleDelete(item)">删除</el-button>
        </template>
      </MobileCardList>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize" :total="total"
        :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" @current-change="load()"
        @size-change="load(1)" />
    </el-card>

    <!-- 新建 / 编辑 -->
    <el-dialog v-model="dialogVisible" :title="editingId ? '编辑套件' : '新建套件'" width="80%" destroy-on-close top="4vh">
      <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
        <div class="form-row">
          <el-form-item label="名称" prop="name" class="form-name">
            <el-input v-model="form.name" placeholder="如：发版必跑回归集" maxlength="200" />
          </el-form-item>
          <el-form-item label="类型" prop="kind" class="form-kind">
            <el-select v-model="form.kind">
              <el-option v-for="(label, value) in SUITE_KIND_LABELS" :key="value" :label="label"
                :value="Number(value)" />
            </el-select>
          </el-form-item>
        </div>
        <div class="form-row">
          <el-form-item label="所属项目" prop="projectId" class="form-name">
            <el-select v-model="form.projectId" placeholder="选择项目" filterable :disabled="!!editingId"
              @change="onProjectChange">
              <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
            </el-select>
          </el-form-item>
          <el-form-item label="执行环境" class="form-kind">
            <el-select v-model="form.environmentId" placeholder="未指定" clearable :disabled="!form.projectId">
              <el-option v-for="e in environmentOptions" :key="e.id" :label="e.name" :value="e.id" />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item label="说明">
          <el-input v-model="form.description" placeholder="套件用途（可选）" maxlength="1000" />
        </el-form-item>
        <el-form-item label="失败策略">
          <el-radio-group v-model="form.failurePolicy">
            <el-radio-button :value="SuiteFailurePolicy.Continue">继续执行</el-radio-button>
            <el-radio-button :value="SuiteFailurePolicy.StopOnFailure">失败快停</el-radio-button>
          </el-radio-group>
          <div class="run-hint">{{ SUITE_FAILURE_POLICY_HINTS[form.failurePolicy] }}</div>
        </el-form-item>
        <el-form-item label="套件用例">
          <div class="case-picker">
            <div class="picker-toolbar">
              <el-input v-model="caseKeyword" placeholder="搜索用例名称/编号" clearable size="small" :prefix-icon="Search"
                class="picker-search" @keyup.enter="searchCases(1)" @clear="searchCases(1)" />
              <el-button type="primary" size="small" @click="searchCases(1)">搜索</el-button>
              <!-- 右侧每行能配的是执行编排里的**前置用例** -->
              <span class="picker-hint">已选 {{ form.testCaseIds.length }} 条；勾选左侧加入，右侧可配置前置用例</span>
            </div>
            <div class="picker-body">
              <div class="picker-candidates">
                <el-table :data="caseOptions" v-loading="caseLoading" size="small" height="288" row-key="id"
                  @row-click="toggleCase">
                  <el-table-column width="44" align="center">
                    <template #header>
                      <!-- 全选只作用于当前页（服务端分页下跨页全选语义含糊，且右侧编排配置也难以驾驭过大的集合） -->
                      <el-checkbox :model-value="allPageSelected" :indeterminate="pageIndeterminate" title="全选/取消本页"
                        @change="toggleSelectPage" @click.stop />
                    </template>
                    <template #default="{ row }">
                      <el-checkbox :model-value="form.testCaseIds.includes(row.id)" @change="toggleCase(row)"
                        @click.stop />
                    </template>
                  </el-table-column>
                  <el-table-column label="用例名称" min-width="150" show-overflow-tooltip>
                    <template #default="{ row }">
                      <span class="case-name" :class="{ 'is-picked': form.testCaseIds.includes(row.id) }">{{
                        row.name }}</span>
                    </template>
                  </el-table-column>
                  <el-table-column label="模块" width="96" show-overflow-tooltip>
                    <template #default="{ row }">{{ row.module || '未分类' }}</template>
                  </el-table-column>
                  <el-table-column label="优先级" width="64">
                    <template #default="{ row }">{{ row.priority || '—' }}</template>
                  </el-table-column>
                </el-table>
                <el-pagination v-model:current-page="casePage" v-model:page-size="casePageSize" :total="caseTotal"
                  size="small" layout="total, sizes, prev, pager, next" :page-sizes="[10, 20, 50, 100]"
                  class="picker-pagination" @current-change="searchCases()" @size-change="searchCases(1)" />
              </div>
              <div class="picker-selected">
                <div v-if="selectedCases.length === 0" class="picker-empty">尚未选择用例</div>
                <div v-for="(item, index) in selectedCases" :key="item.id" class="selected-item"
                  :class="{ 'is-flaky': item.isFlaky }">
                  <span>{{ index + 1 }}.</span>
                  <span class="selected-name" :title="item.name">{{ item.name }}</span>
                  <!-- 前置用例（执行编排）：留空的用法最多，所以默认就是「无前置」。
                       filterable：选中用例多时按名称打字过滤，比在长列表里翻快得多 -->
                  <el-select v-model="form.caseDeps[item.id]" placeholder="无前置" clearable filterable size="small"
                    class="selected-dep" :disabled="selectedCases.length < 2"
                    :title="`前置用例：等「${depName(form.caseDeps[item.id]) || '选中的用例'}」通过后才执行本条`">
                    <el-option v-for="other in dependencyOptions(item.id)" :key="other.id" :label="other.name"
                      :value="other.id" />
                  </el-select>
                  <el-button link type="danger" :icon="Delete" @click="removeCase(item.id)" />
                </div>
              </div>
            </div>
          </div>
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 运行套件 -->
    <el-dialog v-model="runVisible" title="运行套件" width="620px" destroy-on-close>
      <el-alert type="info" :closable="false" class="run-tip"
        :title="`将按顺序为「${runTarget?.name ?? ''}」的 ${runTarget?.caseCount ?? 0} 条用例创建执行记录`">
        <div v-if="runTarget?.failurePolicy === SuiteFailurePolicy.StopOnFailure" class="run-tip-line">
          本套件为<b>失败快停</b>：出现失败/错误后，未开始的用例会被跳过（状态记为「跳过」并给出原因）。
        </div>
        <div class="run-tip-line">
          设置了<b>前置用例</b>的用例，会等前置通过后才开跑；前置未通过则本条直接跳过。
        </div>
      </el-alert>
      <el-form label-width="110px">
        <el-form-item label="执行环境">
          <el-select v-model="runForm.environmentId" placeholder="跟随套件/用例配置" clearable class="full-width">
            <el-option v-for="e in environmentOptions" :key="e.id" :label="e.name" :value="e.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="浏览器矩阵">
          <el-select v-model="runForm.browsers" multiple placeholder="不指定则按用例/环境配置" class="full-width">
            <el-option v-for="b in BROWSER_OPTIONS" :key="b.id" :label="b.label" :value="b.id" />
          </el-select>
          <div class="run-hint">多选时同一条用例会在每个浏览器上各跑一次（用例 × 浏览器）</div>
        </el-form-item>
        <el-form-item label="数据驱动">
          <el-switch v-model="runForm.expandDataSets" active-text="按数据行展开" />
          <div class="run-hint">绑定了数据集的用例会按行展开，一轮跑完全部数据组合</div>
        </el-form-item>
        <el-form-item label="变量覆盖">
          <div class="kv-editor">
            <div v-for="(item, index) in runForm.variables" :key="index" class="kv-row">
              <el-input v-model="item.key" placeholder="变量名" size="small" class="kv-key" />
              <span class="kv-eq">=</span>
              <el-input v-model="item.value" placeholder="值（覆盖数据集行）" size="small" />
              <el-button link type="danger" :icon="Delete" @click="runForm.variables.splice(index, 1)" />
            </div>
            <el-button size="small" :icon="Plus" @click="runForm.variables.push({ key: '', value: '' })">
              添加变量
            </el-button>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="runVisible = false">取消</el-button>
        <el-button type="primary" :loading="running" @click="handleRun">开始运行</el-button>
      </template>
    </el-dialog>

    <!-- 历史运行 -->
    <el-drawer v-model="historyVisible" :title="`历史运行 · ${historySuite?.name ?? ''}`" size="620px">
      <div v-loading="historyLoading">
        <div v-if="history.length === 0" class="picker-empty">暂无历史运行记录</div>
        <div v-for="run in history" :key="run.suiteRunId" class="history-item">
          <div class="history-head">
            <span class="history-time">{{ formatDateTime(run.startedAt) }}</span>
            <el-tag :type="run.passRate >= 100 ? 'success' : run.failed + run.error > 0 ? 'danger' : 'info'"
              size="small">
              通过率 {{ run.passRate }}%
            </el-tag>
            <span class="sub-text">耗时 {{ formatDuration(run.durationMs) }}</span>
          </div>
          <div class="history-body">
            <el-tag size="small" type="success">通过 {{ run.passed }}</el-tag>
            <el-tag v-if="run.failed" size="small" type="danger">失败 {{ run.failed }}</el-tag>
            <el-tag v-if="run.error" size="small" type="danger">错误 {{ run.error }}</el-tag>
            <el-tag v-if="run.skipped" size="small" type="info">跳过 {{ run.skipped }}</el-tag>
            <!-- 编排跳过单独标出来：否则「跳过 N 条」无从解释，看着像有执行丢了 -->
            <el-tooltip v-if="run.orchestrationSkipped" placement="top" content="因前置用例未通过或套件失败快停而跳过的条数">
              <el-tag size="small" type="warning" effect="plain">编排跳过 {{ run.orchestrationSkipped }}</el-tag>
            </el-tooltip>
            <el-tag v-if="run.pending" size="small" type="warning">进行中 {{ run.pending }}</el-tag>
            <span class="sub-text">共 {{ run.total }} 条</span>
          </div>
          <div class="history-actions">
            <el-button link type="primary" @click="shareSuiteRun(run.suiteRunId)">生成报告链接</el-button>
            <el-button link type="primary" @click="viewExecutions(run.suiteRunId)">查看执行记录</el-button>
          </div>
        </div>
      </div>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Delete, Plus, Refresh, Search } from '@element-plus/icons-vue'
import { getProjects } from '@/api/project'
import { getEnvironments } from '@/api/environment'
import { getTestCases } from '@/api/testcase'
import {
  batchDeleteSuites, createSuite, deleteSuite, getSuite, getSuiteRuns, getSuites, runSuite, updateSuite,
} from '@/api/suite'
import { createShare } from '@/api/share'
import { formatDateTime, formatDuration } from '@/utils/formatter'
import { SUITE_KIND_LABELS, SUITE_FAILURE_POLICY_HINTS, SuiteFailurePolicy, SuiteKind, type SuiteRunSummary, type SuiteSummary } from '@/types/suite'
import { ReportShareKind } from '@/types/share'
import type { Project } from '@/types/project'
import type { EnvironmentView } from '@/types/environment'
import type { TestCaseSummary } from '@/types/testcase'
import MobileCardList from '@/components/common/MobileCardList.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { usePagedList } from '@/composables/usePagedList'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useRowSelection } from '@/composables/useRowSelection'
import { BROWSER_OPTIONS } from '@/types/testcase'

/** 窄屏（< 1024px）：表格换成卡片形态，见下方模板 */
const { isMobile } = useBreakpoint()

const router = useRouter()
const projectOptions = ref<Project[]>([])
const environmentOptions = ref<EnvironmentView[]>([])
const projectId = ref('')
const kindFilter = ref<number | undefined>(undefined)
const keyword = ref('')
// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<SuiteSummary>((p, ps) => getSuites({
  projectId: projectId.value || undefined,
  kind: kindFilter.value as SuiteKind | undefined,
  keyword: keyword.value || undefined,
  page: p,
  pageSize: ps,
}))
const load = (targetPage?: number) => list.load(targetPage)
const { items: suites, total, page, pageSize, loading } = toRefs(list)

// 批量删除（套件删除不影响用例本身）
const { deleting, selectedRows, onSelectionChange, handleBatchDelete } =
  useBatchDelete<SuiteSummary>({
    entity: '测试套件',
    remove: batchDeleteSuites,
    reload: load,
  })

// 点击行直接勾选/取消勾选（复选框列与操作列除外）
const { tableRef, handleRowSelectionClick } = useRowSelection()

const runningId = ref('')
const dialogVisible = ref(false)
const editingId = ref('')
const formRef = ref<FormInstance>()
const saving = ref(false)
const caseKeyword = ref('')
const caseOptions = ref<TestCaseSummary[]>([])
const caseCache = ref<Record<string, TestCaseSummary>>({})
// 候选用例为服务端分页：项目用例可能远超弹窗能一次展示的数量
const casePage = ref(1)
const casePageSize = ref(10)
const caseTotal = ref(0)
const caseLoading = ref(false)

const form = reactive({
  projectId: '',
  name: '',
  description: '',
  kind: SuiteKind.Regression as SuiteKind,
  environmentId: '',
  failurePolicy: SuiteFailurePolicy.Continue as SuiteFailurePolicy,
  testCaseIds: [] as string[],
  /** 前置用例：用例 id → 前置用例 id（留空/未出现 = 无前置） */
  caseDeps: {} as Record<string, string | undefined>,
})

const rules: FormRules = {
  name: [{ required: true, message: '请输入套件名称', trigger: 'blur' }],
  projectId: [{ required: true, message: '请选择所属项目', trigger: 'change' }],
}

/** 已选用例（按选择顺序；缺失的条目用缓存占位） */
const selectedCases = computed(() =>
  form.testCaseIds.map((id) => caseCache.value[id] ?? {
    id, name: id.slice(0, 8), module: null, priority: null, isFlaky: false,
  } as unknown as TestCaseSummary))

/** 某条用例可选的前置用例：套件内除它自己以外的用例。
    只给同套件内的选项，是为了让「前置必须在本套件内」这条后端规则在界面上根本无从违反。 */
const dependencyOptions = (caseId: string) => selectedCases.value.filter((c) => c.id !== caseId)

const depName = (caseId?: string) =>
  caseId ? (caseCache.value[caseId]?.name ?? caseId.slice(0, 8)) : ''

const kindTagType = (kind: number) =>
  ({ [SuiteKind.Smoke]: 'success', [SuiteKind.Regression]: 'primary', [SuiteKind.Release]: 'danger' }[kind] ?? 'info') as
  'success' | 'primary' | 'danger' | 'info'

const loadEnvironments = async (targetProjectId?: string) => {
  const id = targetProjectId || projectId.value || form.projectId
  environmentOptions.value = id ? await getEnvironments(id) : []
}

const searchCases = async (targetPage?: number) => {
  if (!form.projectId) return
  if (typeof targetPage === 'number') casePage.value = targetPage
  caseLoading.value = true
  try {
    const res = await getTestCases({
      projectId: form.projectId,
      search: caseKeyword.value || undefined,
      page: casePage.value,
      pageSize: casePageSize.value,
    })
    caseOptions.value = res.items
    caseTotal.value = res.total
    res.items.forEach((c) => (caseCache.value[c.id] = c))
  } finally {
    caseLoading.value = false
  }
}

/** 勾选/取消一条候选用例：加入保持「按勾选顺序」的语义（与右侧编号一致）。
    同时作为行点击与复选框 change 的处理器（checkbox 上已 stop 冒泡，不会双触发） */
const toggleCase = (row: TestCaseSummary) => {
  if (form.testCaseIds.includes(row.id)) {
    removeCase(row.id)
    return
  }
  form.testCaseIds.push(row.id)
  caseCache.value[row.id] = row
}

/** 当前页的全选/半选状态（服务端分页下「全选」只覆盖当前页） */
const allPageSelected = computed(() =>
  caseOptions.value.length > 0 && caseOptions.value.every((c) => form.testCaseIds.includes(c.id)))
const pageIndeterminate = computed(() => {
  const picked = caseOptions.value.filter((c) => form.testCaseIds.includes(c.id)).length
  return picked > 0 && picked < caseOptions.value.length
})

const toggleSelectPage = (checked: boolean | string | number) => {
  const want = Boolean(checked)
  for (const c of caseOptions.value) {
    const inSuite = form.testCaseIds.includes(c.id)
    if (want && !inSuite) {
      form.testCaseIds.push(c.id)
      caseCache.value[c.id] = c
    } else if (!want && inSuite) {
      removeCase(c.id)
    }
  }
}

const onProjectChange = async () => {
  form.environmentId = ''
  form.testCaseIds = []
  form.caseDeps = {}
  caseOptions.value = []
  caseCache.value = {}
  casePage.value = 1
  await loadEnvironments(form.projectId)
  await searchCases()
}

const removeCase = (id: string) => {
  form.testCaseIds = form.testCaseIds.filter((item) => item !== id)
  // 连带清掉与被删用例相关的依赖：指向它的（会变成套件外依赖被后端拒绝）、
  // 以及它自己的（重新加入时不该继承旧配置）
  delete form.caseDeps[id]
  Object.keys(form.caseDeps).forEach((caseId) => {
    if (form.caseDeps[caseId] === id) delete form.caseDeps[caseId]
  })
}

const openCreate = async () => {
  editingId.value = ''
  form.projectId = projectId.value || projectOptions.value[0]?.id || ''
  form.name = ''
  form.description = ''
  form.kind = SuiteKind.Regression
  form.environmentId = ''
  form.failurePolicy = SuiteFailurePolicy.Continue
  form.testCaseIds = []
  form.caseDeps = {}
  caseCache.value = {}
  caseKeyword.value = ''
  casePage.value = 1
  dialogVisible.value = true
  await loadEnvironments(form.projectId)
  await searchCases()
}

const openEdit = async (row: SuiteSummary) => {
  const detail = await getSuite(row.id)
  editingId.value = detail.id
  form.projectId = detail.projectId
  form.name = detail.name
  form.description = detail.description ?? ''
  form.kind = detail.kind
  form.environmentId = detail.environmentId ?? ''
  form.failurePolicy = detail.failurePolicy ?? SuiteFailurePolicy.Continue
  form.testCaseIds = detail.cases.map((c) => c.testCaseId)
  form.caseDeps = {}
  detail.cases.forEach((c) => {
    if (c.dependsOnTestCaseId) form.caseDeps[c.testCaseId] = c.dependsOnTestCaseId
  })
  caseCache.value = {}
  detail.cases.forEach((c) => {
    caseCache.value[c.testCaseId] = {
      id: c.testCaseId, name: c.name, module: c.module, priority: c.priority, isFlaky: c.isFlaky,
    } as unknown as TestCaseSummary
  })
  dialogVisible.value = true
  casePage.value = 1
  caseKeyword.value = ''
  await loadEnvironments(detail.projectId)
  await searchCases()
}

const handleSave = async () => {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return
  if (form.testCaseIds.length === 0) {
    ElMessage.warning('请至少选择一条用例')
    return
  }

  saving.value = true
  try {
    const payload = {
      name: form.name.trim(),
      description: form.description.trim() || null,
      kind: form.kind,
      environmentId: form.environmentId || null,
      failurePolicy: form.failurePolicy,
      // 依赖随成员一起提交：顺序 + 前置属于同一份编排配置（见后端 SuiteCaseSpec 注释）
      cases: form.testCaseIds.map((testCaseId) => ({
        testCaseId,
        dependsOnTestCaseId: form.caseDeps[testCaseId] || null,
      })),
    }
    if (editingId.value) {
      await updateSuite(editingId.value, payload)
      ElMessage.success('已保存')
    } else {
      await createSuite({ projectId: form.projectId, ...payload })
      ElMessage.success('已创建')
    }
    dialogVisible.value = false
    await load()
  } finally {
    saving.value = false
  }
}

// ------------------------------------------------------------ 运行

const runVisible = ref(false)
const running = ref(false)
const runTarget = ref<SuiteSummary | null>(null)
const runForm = reactive({
  environmentId: '',
  browsers: [] as string[],
  expandDataSets: true,
  variables: [] as { key: string; value: string }[],
})

const openRun = async (row: SuiteSummary) => {
  runTarget.value = row
  runForm.environmentId = ''
  runForm.browsers = []
  runForm.expandDataSets = true
  runForm.variables = []
  runVisible.value = true
  await loadEnvironments(row.projectId)
}

const handleRun = async () => {
  if (!runTarget.value) return
  running.value = true
  runningId.value = runTarget.value.id
  try {
    const variables: Record<string, string> = {}
    runForm.variables
      .filter((item) => item.key.trim())
      .forEach((item) => (variables[item.key.trim()] = item.value))

    const result = await runSuite(runTarget.value.id, {
      environmentId: runForm.environmentId || null,
      browsers: runForm.browsers.length > 0 ? runForm.browsers : null,
      expandDataSets: runForm.expandDataSets,
      variables: Object.keys(variables).length > 0 ? variables : null,
    })
    runVisible.value = false
    const extra = result.casesWithoutData > 0 ? `，${result.casesWithoutData} 条用例的数据集为空` : ''
    ElMessage.success(`已创建 ${result.createdCount} 条执行${extra}`)
    await load()
    router.push({ path: '/executions', query: { suiteRunId: result.suiteRunId } })
  } finally {
    running.value = false
    runningId.value = ''
  }
}

// ------------------------------------------------------------ 历史 / 报告链接

const historyVisible = ref(false)
const historyLoading = ref(false)
const historySuite = ref<SuiteSummary | null>(null)
const history = ref<SuiteRunSummary[]>([])

const openHistory = async (row: SuiteSummary) => {
  historySuite.value = row
  historyVisible.value = true
  historyLoading.value = true
  try {
    history.value = await getSuiteRuns(row.id, 20)
  } finally {
    historyLoading.value = false
  }
}

const viewExecutions = (suiteRunId: string) => {
  router.push({ path: '/executions', query: { suiteRunId } })
}

/** 生成套件运行报告的分享链接并新窗口打开 */
const shareSuiteRun = async (suiteRunId: string) => {
  const link = await createShare({ kind: ReportShareKind.SuiteRun, refId: suiteRunId, expiresInDays: 7 })
  window.open(link.url, '_blank')
  ElMessage.success('已生成报告链接并在新窗口打开')
}

/** 用最近一次套件运行生成报告链接 */
const shareRun = async (row: SuiteSummary) => {
  if (!row.lastSuiteRunId) {
    ElMessage.warning('该套件还没有可分享的运行记录')
    return
  }
  await shareSuiteRun(row.lastSuiteRunId)
}

const handleDelete = async (row: SuiteSummary) => {
  try {
    await ElMessageBox.confirm(`确认删除套件「${row.name}」？用例本身不受影响。`, '删除套件', { type: 'warning' })
  } catch {
    return
  }
  await deleteSuite(row.id)
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
.suite-page {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.boundary-tip {
  margin-bottom: 12px;
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

.kind-select {
  width: 140px;
}

.keyword-input {
  width: 200px;
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

.form-row {
  display: flex;
  gap: 16px;
}

.form-name {
  flex: 1;
}

.form-kind {
  width: 220px;
}

.case-picker {
  width: 100%;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
}

.picker-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.picker-hint {
  margin-left: auto;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.picker-search {
  width: 220px;
}

.picker-body {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0;
}

/* 窄屏：左右两栏选择器并排会让每栏只剩一百多像素，退回上下一列 */
@media (max-width: 767px) {
  .picker-body {
    grid-template-columns: minmax(0, 1fr);
  }
}

.picker-candidates,
.picker-selected {
  height: 324px;
  overflow: auto;
  padding-left: 5px;
}

.picker-candidates {
  border-right: 1px solid var(--el-border-color-lighter);
}

/* 已选中的候选用例弱化显示：右侧列表里能看到完整条目，这里不必再抢视线 */
.case-name.is-picked {
  opacity: 0.65;
}

.picker-pagination {
  padding: 4px 8px;
}

.picker-empty {
  padding: 16px;
  text-align: center;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.selected-item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 3px 0;
  font-size: 13px;
}

.selected-item.is-flaky .selected-name {
  color: var(--el-color-danger);
}

.selected-order {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: var(--el-fill-color-dark);
  font-size: 11px;
  flex: none;
}

.selected-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* 前置用例选择器：窄一点、不参与伸展——它只是每条用例的一个附加配置，
   抢走宽度会挤压用例名（长名字更容易被截断，而名字才是这一行的主体） */
.selected-dep {
  width: 132px;
  flex: none;
}

.run-tip {
  margin-bottom: 12px;
}

.run-tip-line {
  margin-top: 4px;
  font-size: 12px;
  line-height: 1.6;
}

.run-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.6;
}

.kv-editor {
  width: 100%;
}

.kv-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
}

.kv-key {
  width: 160px;
}

.kv-eq {
  color: var(--el-text-color-secondary);
}

.full-width {
  width: 100%;
}

.history-item {
  padding: 10px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.history-head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.history-time {
  font-weight: 500;
}

.history-body {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 6px;
  flex-wrap: wrap;
}

.history-actions {
  margin-top: 4px;
}
</style>
