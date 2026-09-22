<template>
  <div class="project-detail">
    <PageHeaderBar title="项目详情" :subtitle="project?.name ?? ''" back-to="/projects" sticky />

    <el-card v-loading="loading" class="detail-card">
      <el-descriptions :column="2" border>
        <el-descriptions-item label="项目名称" :span="2">{{ project?.name }}</el-descriptions-item>
        <el-descriptions-item label="创建时间">{{ formatDateTime(project?.createdAt) }}</el-descriptions-item>
      
        <el-descriptions-item label="用例数">{{ project?.testCaseCount ?? 0 }}</el-descriptions-item>
        <el-descriptions-item label="更新时间">{{ formatDateTime(project?.updatedAt) }}</el-descriptions-item>
        <el-descriptions-item label="项目经理人">
          <span v-if="project?.managerName">{{ project.managerName }}</span>
          <span v-else class="muted">未指定</span>
        </el-descriptions-item>
        <el-descriptions-item label="测试负责人">
          <template v-if="project?.testOwnerName">
            <span>{{ project.testOwnerName }}</span>
            <!-- 验收邮件只发给测试负责人；没填邮箱等于收不到，这里必须显眼 -->
            <el-tooltip v-if="!project.testOwnerEmail" placement="top" content="该用户未填邮箱，测试计划的验收结果邮件无法送达">
              <el-tag size="small" type="warning" effect="plain" class="owner-tag">缺邮箱</el-tag>
            </el-tooltip>
          </template>
          <span v-else class="muted">未指定</span>
        </el-descriptions-item>
        <el-descriptions-item label="开发负责人">
          <span v-if="project?.developerOwnerName">{{ project.developerOwnerName }}</span>
          <span v-else class="muted">未指定</span>
        </el-descriptions-item>
        <el-descriptions-item label="描述" :span="2">{{ project?.description || '—' }}</el-descriptions-item>
      </el-descriptions>

      <el-button type="primary" class="detail-action"
        @click="router.push({ path: '/testcases', query: { projectId: project?.id } })">
        查看测试用例
      </el-button>
      <el-button type="success" class="detail-action" :loading="reportDownloading" @click="handleDownloadReport">
        下载测试报告
      </el-button>
    </el-card>

    <!-- 四块内容各自独立，用 tab 分开：一页里纵向堆四个表格会很难找。
         每个 tab 首次点开才拉数据（lazy），不必为看不见的内容付请求。 -->
    <el-card class="tab-card">
      <el-tabs v-model="activeTab" @tab-change="onTabChange">
        <!-- ==================== 测试用例 ==================== -->
        <el-tab-pane label="测试用例" name="cases">
          <div class="pane-head">
            <span class="pane-count">共 {{ caseList.total }} 条用例</span>
            <el-button  type="primary" @click="goCaseList">去管理</el-button>
          </div>

          <!-- 搜索条件与用例列表页对齐：搜索、模块、最近执行结果、不稳定用例。
               项目固定为当前项目，不提供项目下拉 -->
          <div class="case-toolbar">
            <el-input v-model="caseSearch" placeholder="搜索用例名称/编号" clearable class="case-search"
              @keyup.enter="caseList.load(1)" @clear="caseList.load(1)" />
            <el-select v-model="caseModuleFilter" placeholder="全部模块" clearable filterable class="case-module"
              @change="caseList.load(1)">
              <el-option v-for="m in caseModuleOptions" :key="m.module" :label="`${m.module}（${m.count}）`"
                :value="m.module" />
            </el-select>
            <el-select v-model="caseExecFilter" placeholder="全部执行结果" clearable class="case-exec"
              @change="caseList.load(1)">
              <el-option v-for="o in CASE_EXEC_FILTER_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
            </el-select>
            <el-checkbox v-model="caseFlakyOnly" @change="caseList.load(1)">仅看不稳定用例</el-checkbox>
          </div>

          <el-table v-loading="caseList.loading" :data="caseList.items" size="small" class="cases-table" @row-click="goCaseDetail">
            <el-table-column label="用例名称" min-width="240" show-overflow-tooltip>
              <template #default="{ row }">
                <el-tag v-if="row.priority" size="small" :type="priorityTagType(row.priority)" class="inline-tag">{{
                  row.priority }}</el-tag>
                <el-tag v-if="row.aiGenerated" size="small" type="warning" class="inline-tag">AI</el-tag>
                <el-tag v-if="row.isFlaky" size="small" type="danger" effect="plain" class="inline-tag">不稳定</el-tag>
                <span>{{ row.name }}</span>
              </template>
            </el-table-column>
            <el-table-column label="模块" width="140" show-overflow-tooltip>
              <template #default="{ row }">
                <span v-if="row.module">{{ row.module }}</span>
                <span v-else class="muted">-</span>
              </template>
            </el-table-column>
            <el-table-column label="编号" width="120" show-overflow-tooltip>
              <template #default="{ row }">
                <span v-if="row.caseCode" class="mono">{{ row.caseCode }}</span>
                <span v-else class="muted">-</span>
              </template>
            </el-table-column>
            <el-table-column label="类型" width="90">
              <template #default="{ row }">
                <el-tag size="small">{{ typeLabel(row.type) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="最近执行结果" width="140">
              <template #default="{ row }">
                <template v-if="row.latestExecutionStatus != null">
                  <el-tag :type="executionStatusTagType(row.latestExecutionStatus)" size="small">
                    {{ EXECUTION_STATUS_LABELS[row.latestExecutionStatus] ?? '未知' }}
                  </el-tag>
                  <div v-if="row.lastExecutedAt" class="exec-time">{{ formatDateTime(row.lastExecutedAt) }}</div>
                </template>
                <span v-else class="muted">从未执行</span>
              </template>
            </el-table-column>
            <el-table-column label="版本" width="70" align="center">
              <template #default="{ row }">{{ row.version }}</template>
            </el-table-column>
            <el-table-column label="更新时间" width="160">
              <template #default="{ row }">{{ formatDateTime(row.updatedAt) }}</template>
            </el-table-column>
            <el-table-column label="操作" width="110" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" @click.stop="goCaseDetail(row)">详情</el-button>
                <el-button link type="primary" @click.stop="goCaseEdit(row)">编辑</el-button>
              </template>
            </el-table-column>
            <template #empty>
              <el-empty description="该项目还没有测试用例" :image-size="60" />
            </template>
          </el-table>

          <el-pagination class="case-pagination" v-model:current-page="caseList.page" v-model:page-size="caseList.pageSize"
            :total="caseList.total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next"
            @current-change="caseList.load()" @size-change="caseList.load(1)" />
        </el-tab-pane>

        <!-- ==================== 测试计划 ==================== -->
        <el-tab-pane label="测试计划" name="plans">
          <div class="pane-head">
            <span class="pane-count">共 {{ planList.total }} 个计划</span>
            <el-button  type="primary" @click="goPlanList">去管理</el-button>
          </div>

          <!-- 搜索条件与测试计划列表页对齐：状态、版本、计划名称。
               项目固定为当前项目，不提供项目下拉 -->
          <div class="case-toolbar">
            <el-select v-model="planStatusFilter" placeholder="全部状态" clearable class="plan-status"
              @change="planList.load(1)">
              <el-option v-for="(label, value) in STATUS_LABELS" :key="value" :label="label" :value="Number(value)" />
            </el-select>
            <el-select v-model="planReleaseFilter" placeholder="全部版本" clearable filterable class="plan-release"
              @change="planList.load(1)">
              <el-option v-for="r in planReleases" :key="r" :label="r" :value="r" />
            </el-select>
            <el-input v-model="planSearch" placeholder="搜索计划名称" clearable class="case-search"
              @keyup.enter="planList.load(1)" @clear="planList.load(1)" />
          </div>

          <el-table v-loading="planList.loading" :data="planList.items" size="small" class="plans-table"
            @row-click="goPlanDetail">
            <el-table-column label="计划名称" min-width="200" show-overflow-tooltip>
              <template #default="{ row }">
                <span class="plan-name">{{ row.name }}</span>
                <el-tag v-if="row.releaseName" size="small" effect="plain" type="info" class="inline-tag">
                  {{ row.releaseName }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="状态" width="90">
              <template #default="{ row }">
                <el-tag size="small" effect="light" :type="statusTagType(row.status)">
                  {{ STATUS_LABELS[row.status] }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="目标通过率" width="100" align="center">
              <template #default="{ row }">{{ percent(row.targetPassRate) }}</template>
            </el-table-column>
            <el-table-column label="范围内用例" width="100" align="center">
              <template #default="{ row }">{{ row.caseCount }}</template>
            </el-table-column>
            <!-- 只报"第几轮、通过率"，不在这里显示"达标/未达标"：
                 达标判定涉及 allowErrors / excludeFlaky / GateMode，只有后端那一套算得对，
                 在列表里拿通过率跟目标一比就会造出第二套口径 -->
            <el-table-column label="最新轮次" min-width="150">
              <template #default="{ row }">
                <template v-if="row.lastRoundNo != null">
                  <span>第 {{ row.lastRoundNo }} 轮</span>
                  <span v-if="row.lastPassRate != null" class="muted">· 通过率 {{ percent(row.lastPassRate) }}</span>
                </template>
                <span v-else class="muted">尚未执行</span>
              </template>
            </el-table-column>
            <el-table-column label="负责人" width="100" show-overflow-tooltip>
              <template #default="{ row }">{{ row.ownerName || '—' }}</template>
            </el-table-column>
            <el-table-column label="操作" width="110" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" @click.stop="goPlanDetail(row)">查看</el-button>
                <el-button link type="primary" :loading="exportingId === row.id"
                  @click.stop="handleExportPlan(row)">导出</el-button>
              </template>
            </el-table-column>
            <template #empty>
              <el-empty description="该项目还没有测试计划" :image-size="60" />
            </template>
          </el-table>

          <el-pagination class="case-pagination" v-model:current-page="planList.page" v-model:page-size="planList.pageSize"
            :total="planList.total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next"
            @current-change="planList.load()" @size-change="planList.load(1)" />
        </el-tab-pane>

        <!-- ==================== 测试环境 ==================== -->
        <el-tab-pane label="测试环境" name="environments">
          <EnvironmentCard :project-id="projectId" />
        </el-tab-pane>

        <!-- ==================== 需求 ==================== -->
        <el-tab-pane label="需求" name="requirements">
          <div class="pane-head">
            <span class="pane-count">共 {{ requirementList.total }} 条需求</span>
            <el-button  type="primary" @click="goRequirementList">去管理</el-button>
          </div>

          <!-- 搜索条件与需求列表页对齐：搜索标题/外部编号。项目固定为当前项目 -->
          <div class="case-toolbar">
            <el-input v-model="requirementSearch" placeholder="搜索标题 / 外部编号" clearable class="case-search"
              @keyup.enter="requirementList.load(1)" @clear="requirementList.load(1)" />
          </div>

          <el-table v-loading="requirementList.loading" :data="requirementList.items" size="small">
            <el-table-column label="需求标题" min-width="220" show-overflow-tooltip prop="title" />
            <el-table-column label="优先级" width="90" align="center">
              <template #default="{ row }">{{ row.priority || '—' }}</template>
            </el-table-column>
            <el-table-column label="关联用例" width="100" align="center">
              <template #default="{ row }">
                <el-tag v-if="row.caseCount > 0" size="small" type="success" effect="plain">{{ row.caseCount }}</el-tag>
                <!-- 没关联用例的需求就是覆盖缺口，得显眼 -->
                <el-tag v-else size="small" type="danger" effect="plain">未覆盖</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="已验证通过" width="110" align="center">
              <template #default="{ row }">
                <span v-if="row.caseCount > 0">{{ row.passedCaseCount }} / {{ row.caseCount }}</span>
                <span v-else class="muted">—</span>
              </template>
            </el-table-column>
            <template #empty>
              <el-empty description="该项目还没有维护需求" :image-size="60" />
            </template>
          </el-table>

          <el-pagination class="case-pagination" v-model:current-page="requirementList.page"
            v-model:page-size="requirementList.pageSize" :total="requirementList.total" :page-sizes="[10, 20, 50]"
            layout="total, sizes, prev, pager, next" @current-change="requirementList.load()"
            @size-change="requirementList.load(1)" />
        </el-tab-pane>

        <!-- ==================== 缺陷 ==================== -->
        <el-tab-pane label="缺陷" name="defects">
          <div class="pane-head">
            <span class="pane-count">共 {{ defectList.total }} 个缺陷</span>
            <el-button  type="primary" @click="goDefectList">去管理</el-button>
          </div>

          <!-- 搜索条件与缺陷列表页对齐：状态、严重度、搜索标题。项目固定为当前项目 -->
          <div class="case-toolbar">
            <el-select v-model="defectStatusFilter" placeholder="全部状态" clearable class="plan-status"
              @change="defectList.load(1)">
              <el-option v-for="(label, value) in DEFECT_STATUS_LABELS" :key="value" :label="label"
                :value="Number(value)" />
            </el-select>
            <el-select v-model="defectSeverityFilter" placeholder="全部严重度" clearable class="plan-status"
              @change="defectList.load(1)">
              <el-option v-for="(label, value) in DEFECT_SEVERITY_LABELS" :key="value" :label="label"
                :value="Number(value)" />
            </el-select>
            <el-input v-model="defectSearch" placeholder="搜索标题" clearable class="case-search"
              @keyup.enter="defectList.load(1)" @clear="defectList.load(1)" />
          </div>

          <el-table v-loading="defectList.loading" :data="defectList.items" size="small">
            <el-table-column label="缺陷标题" min-width="220" show-overflow-tooltip prop="title" />
            <el-table-column label="严重度" width="90">
              <template #default="{ row }">
                <el-tag size="small" effect="plain"
                  :type="DEFECT_SEVERITY_TAG[row.severity] ?? 'info'">
                  {{ DEFECT_SEVERITY_LABELS[row.severity] ?? '未知' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="状态" width="100">
              <template #default="{ row }">
                <el-tag size="small" effect="light" :type="DEFECT_STATUS_TAG[row.status] ?? 'info'">
                  {{ DEFECT_STATUS_LABELS[row.status] ?? '未知' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column label="负责人" width="100" show-overflow-tooltip>
              <template #default="{ row }">{{ row.assignedToName || '未指派' }}</template>
            </el-table-column>
            <el-table-column label="发现时间" width="150">
              <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
            </el-table-column>
            <template #empty>
              <el-empty description="该项目还没有缺陷" :image-size="60" />
            </template>
          </el-table>

          <el-pagination class="case-pagination" v-model:current-page="defectList.page" v-model:page-size="defectList.pageSize"
            :total="defectList.total" :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next"
            @current-change="defectList.load()" @size-change="defectList.load(1)" />
        </el-tab-pane>

        <!-- ==================== API 令牌（仅 ManageProjects 可见） ==================== -->
        <el-tab-pane v-if="authStore.can(Permission.ManageProjects)" label="API 令牌" name="apiTokens" lazy>
          <ApiTokenCard :project-id="projectId" />
        </el-tab-pane>

        <!-- ==================== 扩展字段（仅 ManageProjects 可见） ==================== -->
        <el-tab-pane v-if="authStore.can(Permission.ManageProjects)" label="扩展字段" name="customFields" lazy>
          <CustomFieldCard :project-id="projectId" />
        </el-tab-pane>

        <!-- ==================== 项目成员（迭代 E·① 项目级授权；仅 ManageProjects 可见） ==================== -->
        <el-tab-pane v-if="authStore.can(Permission.ManageProjects)" label="成员" name="members" lazy>
          <ProjectMemberCard :project-id="projectId" />
        </el-tab-pane>
      </el-tabs>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import PageHeaderBar from '@/components/common/PageHeaderBar.vue'
import EnvironmentCard from '@/components/project/EnvironmentCard.vue'
import ApiTokenCard from '@/components/project/ApiTokenCard.vue'
import CustomFieldCard from '@/components/project/CustomFieldCard.vue'
import ProjectMemberCard from '@/components/project/ProjectMemberCard.vue'
import { useAuthStore } from '@/stores/auth'
import { Permission } from '@/constants/permissions'
import { usePagedList } from '@/composables/usePagedList'
import { getProject } from '@/api/project'
import { downloadProjectReport, saveBlobAsFile } from '@/api/report'
import { exportPlanReportApi, listTestPlansApi, testPlanReleasesApi } from '@/api/testPlan'
import { getTestCases, getTestCaseModules } from '@/api/testcase'
import { getRequirements } from '@/api/requirement'
import { getDefects } from '@/api/defect'
import { formatDateTime } from '@/utils/formatter'
import { EXECUTION_STATUS_LABELS, executionStatusTagType } from '@/types/execution'
import { TestPlanStatus, TEST_PLAN_STATUS_LABELS as STATUS_LABELS, type TestPlanStatusValue, type TestPlanSummary } from '@/types/testPlan'
import { TestType, CASE_EXEC_FILTER_OPTIONS, type TestCaseModuleStat, type TestCaseSummary } from '@/types/testcase'
import { DEFECT_SEVERITY_LABELS, DEFECT_STATUS_LABELS, type DefectListItem } from '@/types/defect'
import type { RequirementListItem } from '@/types/requirement'
import type { Project } from '@/types/project'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const projectId = route.params.id as string

const project = ref<Project | null>(null)
const loading = ref(false)
const activeTab = ref('cases')

// ------------------------------ 四个 tab 的分页列表（搜索条件各自的闭包里组装）
// usePagedList 收敛「页码/页大小/总数/loading + 切筛选回第一页」的样板，见 composables/usePagedList.ts
const loaded = reactive({ cases: false, plans: false, requirements: false, defects: false })

// 各 tab 的搜索条件，与各自列表页对齐；项目固定为当前项目，不提供项目下拉
const caseSearch = ref('')
const caseModuleFilter = ref('')
const planStatusFilter = ref<number | ''>('')
const planReleaseFilter = ref('')
const planSearch = ref('')
const requirementSearch = ref('')
const defectStatusFilter = ref<number | ''>('')
const defectSeverityFilter = ref<number | ''>('')
const defectSearch = ref('')

const caseList = usePagedList<TestCaseSummary>(async (page, pageSize) => {
  const res = await getTestCases({
    projectId,
    search: caseSearch.value || undefined,
    module: caseModuleFilter.value || undefined,
    flakyOnly: caseFlakyOnly.value || undefined,
    // 清空 el-select 后拿到的是 ''；只透传真数字（0=未执行 是合法值，不能用真值判断）
    execState: typeof caseExecFilter.value === 'number' ? caseExecFilter.value : undefined,
    page,
    pageSize,
  })
  loaded.cases = true
  return res
})
const caseModuleOptions = ref<TestCaseModuleStat[]>([])
/** 按「最近一次执行结果」筛选（见 CaseExecFilter），语义与用例列表页一致 */
const caseExecFilter = ref<number | undefined>(undefined)
const caseFlakyOnly = ref(false)

const planList = usePagedList<TestPlanSummary>(async (page, pageSize) => {
  const res = await listTestPlansApi({
    projectId,
    // 清空 el-select 后拿到的是 ''，只透传真数字（枚举绑定，'' 会被后端解析 400）
    status: planStatusFilter.value === '' ? undefined : (planStatusFilter.value as TestPlanStatusValue),
    releaseName: planReleaseFilter.value || undefined,
    search: planSearch.value.trim() || undefined,
    page,
    pageSize,
  })
  loaded.plans = true
  return res
})
const planReleases = ref<string[]>([])

const requirementList = usePagedList<RequirementListItem>(async (page, pageSize) => {
  const res = await getRequirements({
    projectId,
    search: requirementSearch.value.trim() || undefined,
    page,
    pageSize,
  })
  loaded.requirements = true
  return res
})

const defectList = usePagedList<DefectListItem>(async (page, pageSize) => {
  const res = await getDefects({
    projectId,
    // 清空 el-select 后拿到的是 ''，只透传真数字
    status: defectStatusFilter.value === '' ? undefined : defectStatusFilter.value,
    severity: defectSeverityFilter.value === '' ? undefined : defectSeverityFilter.value,
    search: defectSearch.value.trim() || undefined,
    page,
    pageSize,
  })
  loaded.defects = true
  return res
})

const exportingId = ref<string | null>(null)

const priorityTagType = (priority: string) =>
  ({ P0: 'danger', P1: 'warning', P2: 'primary', P3: 'info' }[priority] ?? 'info') as
  'danger' | 'warning' | 'primary' | 'info'

const typeLabel = (type: TestType) =>
  ({ [TestType.Web]: 'Web', [TestType.Api]: 'API', [TestType.Mobile]: '移动端' }[type] ?? '未知')

const goCaseList = () => router.push({ path: '/testcases', query: { projectId } })
const goCaseDetail = (row: TestCaseSummary) => router.push(`/testcases/${row.id}`)
const goCaseEdit = (row: TestCaseSummary) => router.push(`/testcases/${row.id}/edit`)

// 项目汇总报告：点击直接下载（xlsx，全量统计）；报告生成涉及大量执行数据，需 loading + 宽松超时
const reportDownloading = ref(false)
const handleDownloadReport = async () => {
  if (!project.value) return
  reportDownloading.value = true
  try {
    const blob = await downloadProjectReport(project.value.id)
    saveBlobAsFile(blob, `测试报告_${project.value.name}.xlsx`)
    ElMessage.success('测试报告已生成并开始下载')
  } catch {
    ElMessage.error('测试报告生成失败，请稍后重试')
  } finally {
    reportDownloading.value = false
  }
}

const statusTagType = (status: number) =>
  (({ 0: 'info', 1: 'primary', 2: 'success', 3: 'info' }[status] ?? 'info') as
    'info' | 'primary' | 'success')

/** 缺陷的严重度/状态配色。文案用 types/defect.ts 里的共享定义，别在这里再抄一份 */
const DEFECT_SEVERITY_TAG: Record<number, string> = { 0: 'danger', 1: 'warning', 2: 'info', 3: 'info' }
const DEFECT_STATUS_TAG: Record<number, string> = { 0: 'danger', 1: 'warning', 2: 'primary', 3: 'success', 4: 'info' }

const percent = (value: number) => `${(value * 100).toFixed(1)}%`

const goPlanList = () => router.push({ path: '/test-plans', query: { projectId } })
const goPlanDetail = (row: TestPlanSummary) => router.push(`/test-plans/${row.id}`)
const goRequirementList = () => router.push({ path: '/requirements', query: { projectId } })
const goDefectList = () => router.push({ path: '/defects', query: { projectId } })

const handleExportPlan = async (row: TestPlanSummary) => {
  exportingId.value = row.id
  try {
    await exportPlanReportApi(row.id, row.name)
    ElMessage.success('验收报告已生成')
  } finally {
    exportingId.value = null
  }
}

// ------------------------------ 每个 tab 首次打开才加载

const loadCaseModules = async () => {
  try {
    caseModuleOptions.value = await getTestCaseModules(projectId)
  } catch {
    caseModuleOptions.value = []
  }
}

const loadPlanReleases = async () => {
  try {
    planReleases.value = await testPlanReleasesApi(projectId)
  } catch {
    planReleases.value = []
  }
}

const onTabChange = (name: string | number) => {
  if (name === 'cases' && !loaded.cases) void caseList.load()
  else if (name === 'plans' && !loaded.plans) {
    void planList.load()
    void loadPlanReleases()
  } else if (name === 'requirements' && !loaded.requirements) void requirementList.load()
  else if (name === 'defects' && !loaded.defects) void defectList.load()
  // 测试环境由 EnvironmentCard 自己加载，这里不用管
}

onMounted(async () => {
  loading.value = true
  try {
    project.value = await getProject(projectId)
  } finally {
    loading.value = false
  }
  // 默认停在「测试用例」，列表和模块筛选一起拉出来
  await Promise.all([caseList.load(), loadCaseModules()])
})
</script>

<style scoped>
.page-header {
  margin-bottom: 16px;
}

.detail-card {
  min-height: 300px;
}

.detail-action {
  margin-top: 16px;
}

.tab-card {
  margin-top: 16px;
}

/* 负责人名字与「缺邮箱」标记之间留一点空隙 */
.owner-tag {
  margin-left: 6px;
}

.pane-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.pane-count {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.plan-name {
  margin-right: 6px;
}

.inline-tag {
  vertical-align: middle;
}

.muted {
  color: var(--el-text-color-secondary);
}

/* 只有计划表和用例表是整行可点的；需求/缺陷表的行点不动，
   给它们 pointer 光标会让人以为点了有用 */
.plans-table :deep(.el-table__row),
.cases-table :deep(.el-table__row) {
  cursor: pointer;
}

/* 用例 tab 的搜索条件行：与用例列表页的 toolbar 同款布局 */
.case-toolbar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  margin-bottom: 10px;
}

.case-search {
  width: 220px;
}

.case-module {
  width: 160px;
}

.case-exec {
  width: 150px;
}

.plan-status {
  width: 130px;
}

.plan-release {
  width: 160px;
}

.exec-time {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.case-pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 10px;
}

.mono {
  font-family: var(--el-font-family-mono, monospace);
}
</style>
