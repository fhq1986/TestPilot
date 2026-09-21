<template>
  <div class="project-list">
    <el-card class="list-card">
      <div class="toolbar">
        <el-input v-model="search" placeholder="搜索项目名称" clearable class="search-input" @keyup.enter="load(1)"
          @clear="load(1)" />
        <el-button type="danger" :icon="Delete" :disabled="selectedRows.length === 0" :loading="deleting"
          @click="handleBatchDelete">
          批量删除（{{ selectedRows.length }}）
        </el-button>
        <el-button type="primary" :icon="Plus" @click="openCreate">新建项目</el-button>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" @click="load()">刷新</el-button>
      </div>

      <div v-if="!isMobile" class="table-wrap">
        <el-table ref="tableRef" v-loading="loading" :data="projects" row-key="id" height="100%"
          @row-click="handleRowSelectionClick" @selection-change="onSelectionChange">
          <el-table-column type="selection" width="44" />
          <el-table-column label="项目名称" min-width="200" fixed="left">
            <template #default="{ row }">
              <!-- 行点击是勾选；名称链接进详情（.stop 防止误勾选） -->
              <el-link type="primary" :underline="false" @click.stop="goDetail(row)">{{ row.name }}</el-link>
            </template>
          </el-table-column>
          <el-table-column prop="description" label="描述" min-width="280" show-overflow-tooltip />
          <el-table-column prop="testCaseCount" label="用例数" width="100" />
          <el-table-column label="项目负责人" width="120">
            <template #default="{ row }">
              <span v-if="row.managerName">{{ row.managerName }}</span>
              <span v-else class="muted">—</span>
            </template>
          </el-table-column>
          <el-table-column label="测试负责人" width="150">
            <template #default="{ row }">
              <template v-if="row.testOwnerName">
                <span>{{ row.testOwnerName }}</span>
                <!-- 定了负责人却没填邮箱 → 验收邮件发不出去，这里必须显眼地提示 -->
                <el-tooltip v-if="!row.testOwnerEmail" placement="top" content="该用户未填邮箱，测试计划的验收结果邮件无法送达">
                  <el-tag size="small" type="warning" effect="plain">缺邮箱</el-tag>
                </el-tooltip>
              </template>
              <span v-else class="muted">—</span>
            </template>
          </el-table-column>
          <el-table-column label="创建人" width="110" show-overflow-tooltip>
            <template #default="{ row }">
              <span v-if="row.createdByName">{{ row.createdByName }}</span>
              <span v-else>-</span>
            </template>
          </el-table-column>
          <el-table-column label="更新时间" width="180">
            <template #default="{ row }">
              {{ formatDateTime(row.updatedAt) }}
            </template>
          </el-table-column>
          <el-table-column label="操作" width="230" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click.stop="goDetail(row)">详情</el-button>
              <el-button link type="primary" @click.stop="openEdit(row)">编辑</el-button>
              <el-button link type="success" @click.stop="openReport(row)">测试报告</el-button>
              <el-button link type="danger" @click.stop="handleDelete(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- 窄屏：表格换成卡片。桌面版点整行进详情；卡片上没有「点整行」的语义，
           改为明确的「详情」按钮 -->
      <MobileCardList v-else v-loading="loading" :items="projects" :row-key="(row) => row.id" empty-text="暂无项目">
        <template #title="{ item }">
          <el-link type="primary" :underline="false" @click="goDetail(item)">{{ item.name }}</el-link>
        </template>

        <template #badge="{ item }">
          <!-- 定了负责人却没填邮箱 → 验收邮件发不出去，桌面版靠 tooltip 提示，
               触屏没有 hover，直接把“缺邮箱”这个结论写在卡上 -->
          <el-tag v-if="item.testOwnerName && !item.testOwnerEmail" size="small" type="warning" effect="plain">
            缺邮箱
          </el-tag>
        </template>

        <template #meta="{ item }">
          <span v-if="item.description"><span class="mcl-label">描述</span>{{ item.description }}</span>
          <span><span class="mcl-label">用例数</span>{{ item.testCaseCount }}</span>
          <span><span class="mcl-label">项目负责人</span>{{ item.managerName || '—' }}</span>
          <span><span class="mcl-label">测试负责人</span>{{ item.testOwnerName || '—' }}</span>
          <span><span class="mcl-label">更新</span>{{ formatDateTime(item.updatedAt) }}</span>
        </template>

        <template #actions="{ item }">
          <el-button link type="primary" @click="goDetail(item)">详情</el-button>
          <el-button link type="primary" @click="openEdit(item)">编辑</el-button>
          <el-button link type="success" @click="openReport(item)">测试报告</el-button>
          <el-button link type="danger" @click="handleDelete(item)">删除</el-button>
        </template>
      </MobileCardList>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize" :total="total"
        :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" @current-change="load()"
        @size-change="load(1)" />
    </el-card>

    <el-dialog v-model="dialogVisible" :title="editing ? '编辑项目' : '新建项目'" width="60%" @closed="resetForm" top="2vh">
      <el-form ref="formRef" :model="form" :rules="formRules" label-width="150px">
        <el-form-item label="名称" prop="name">
          <el-input v-model="form.name" placeholder="请输入项目名称" />
        </el-form-item>
        <el-form-item label="项目负责人" prop="managerId">
          <el-select v-model="form.managerId" clearable filterable placeholder="从用户中选择（可选）" class="w-full">
            <el-option v-for="u in userOptions" :key="u.id" :label="u.name" :value="u.id">
              <span>{{ u.name }}</span>
              <span class="user-meta">
                <el-tag v-if="!u.isActive" size="small" type="info" effect="plain">已停用</el-tag>
                <el-tag v-if="!u.hasEmail" size="small" type="warning" effect="plain">无邮箱</el-tag>
              </span>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item label="测试负责人" prop="testOwnerId">
          <el-select v-model="form.testOwnerId" clearable filterable placeholder="从用户中选择（可选）" class="w-full">
            <el-option v-for="u in userOptions" :key="u.id" :label="u.name" :value="u.id">
              <span>{{ u.name }}</span>
              <span class="user-meta">
                <el-tag v-if="!u.isActive" size="small" type="info" effect="plain">已停用</el-tag>
                <el-tag v-if="!u.hasEmail" size="small" type="warning" effect="plain">无邮箱</el-tag>
              </span>
            </el-option>
          </el-select>
          <div class="field-hint">测试计划轮次完成后的验收结果邮件（含报告）会发给这位负责人，请确认其已填写邮箱。</div>
        </el-form-item>
        <el-form-item label="开发负责人" prop="developerOwnerId">
          <el-select v-model="form.developerOwnerId" clearable filterable placeholder="从用户中选择（可选）" class="w-full">
            <el-option v-for="u in userOptions" :key="u.id" :label="u.name" :value="u.id">
              <span>{{ u.name }}</span>
              <span class="user-meta">
                <el-tag v-if="!u.isActive" size="small" type="info" effect="plain">已停用</el-tag>
              </span>
            </el-option>
          </el-select>
          <!-- 与测试负责人的分工写清楚：两者容易混，而职责完全不同 -->
          <div class="field-hint">对<b>缺陷修复</b>负责（测试负责人对用例质量负责）。目前仅作登记与展示，不参与邮件与指派。</div>
        </el-form-item>
        <el-form-item label="描述" prop="description">
          <el-input v-model="form.description" type="textarea" :rows="3" placeholder="项目描述（可选）" />
        </el-form-item>
        <el-form-item label="Agent 自愈闭环">
          <el-switch v-model="form.agentLoopEnabled" />
          <div class="field-hint">
            开启后，本项目测试用例执行失败会尝试由 AI 归因并自动修复后重跑；还需在「系统配置 → Agent 自愈闭环」开启系统级总开关，二者同时开启才生效。默认关闭。
          </div>
        </el-form-item>
        <el-form-item label="自愈通过计入达标">
          <el-switch v-model="form.treatAgentHealedAsPass" :disabled="!form.agentLoopEnabled" />
          <div class="field-hint">
            默认关闭：Agent 自愈后的「通过」不计入测试计划达标，避免「把用例改松即通过」污染验收质量。
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="reportDialogVisible" title="导出测试报告" width="630px" @closed="onReportDialogClosed">
      <el-form label-width="96px">
        <el-form-item label="报告类型">
          <el-radio-group v-model="reportKind">
            <el-radio-button value="project">项目汇总报告</el-radio-button>
            <el-radio-button value="plan">测试计划验收报告</el-radio-button>
          </el-radio-group>
        </el-form-item>

        <!-- 项目汇总报告：只按日期区间收敛统计范围。
             刻意不再提供「按测试计划过滤」——要按计划看验收结果，就切到上面的
             「测试计划验收报告」，两种报告各自只回答一个问题，不要在一个入口里混。 -->
        <el-form-item v-if="reportKind === 'project'" label="统计区间">
          <el-date-picker v-model="reportRange" type="daterange" value-format="YYYY-MM-DD" start-placeholder="开始日期"
            end-placeholder="结束日期" unlink-panels class="w-full" />
        </el-form-item>

        <el-form-item v-else label="测试计划">
          <el-select v-model="reportPlanId" filterable remote reserve-keyword :remote-method="onPlanSearch"
            :loading="planLoading" :no-data-text="planEmptyText" placeholder="请选择要导出的测试计划" class="w-full">
            <el-option v-for="p in planOptions" :key="p.id" :label="p.name" :value="p.id">
              <span>{{ p.name }}</span>
              <span class="plan-meta">
                <el-tag v-if="p.id === reportPlanId" size="small" effect="plain" type="primary">当前</el-tag>
                {{ planStatusLabel[p.status] }} · {{ p.caseCount }} 条用例
              </span>
            </el-option>
          </el-select>
        </el-form-item>
      </el-form>

      <p class="report-tip">{{ reportTip }}</p>

      <el-alert v-if="reportKind === 'plan' && planLoaded && !planSearchTerm && planOptions.length === 0" type="info"
        :closable="false" title="该项目还没有测试计划" description="可先在「测试计划」页面创建计划并圈定验收范围。" show-icon />

      <template #footer>
        <el-button @click="reportDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="reporting" @click="handleDownloadReport">生成并下载</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Delete, Plus, Refresh } from '@element-plus/icons-vue'
import { getProjects, createProject, updateProject, deleteProject, batchDeleteProjects } from '@/api/project'
import { listUserOptionsApi } from '@/api/auth'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useRowSelection } from '@/composables/useRowSelection'
import { usePagedList } from '@/composables/usePagedList'
import { downloadProjectReport, saveBlobAsFile } from '@/api/report'
import { exportPlanReportApi, listTestPlansApi } from '@/api/testPlan'
import { formatDateTime } from '@/utils/formatter'
import type { Project } from '@/types/project'
import type { UserOption } from '@/types/auth'
import type { TestPlanSummary } from '@/types/testPlan'
import MobileCardList from '@/components/common/MobileCardList.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'

const router = useRouter()

/** 窄屏（< 1024px）：表格换成卡片形态，见下方模板 */
const { isMobile } = useBreakpoint()

// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<Project>((p, ps) => getProjects({
  search: search.value || undefined,
  page: p,
  pageSize: ps,
}))
const load = (targetPage?: number) => list.load(targetPage)
const { items: projects, total, page, pageSize, loading } = toRefs(list)
const search = ref('')

// 点击行直接勾选/取消勾选（复选框列与操作列除外）；进详情走名称链接
const { tableRef, handleRowSelectionClick } = useRowSelection()

const { deleting, selectedRows, onSelectionChange, handleBatchDelete } =
  useBatchDelete<Project>({
    entity: '项目',
    remove: batchDeleteProjects,
    reload: () => load(),
  })

// 测试报告导出
type ReportKind = 'project' | 'plan'

const reportDialogVisible = ref(false)
const reporting = ref(false)
const reportProject = ref<Project | null>(null)
const reportRange = ref<[string, string] | null>(null)
const reportKind = ref<ReportKind>('project')
const reportPlanId = ref('')

const planOptions = ref<TestPlanSummary[]>([])
const planLoading = ref(false)
/** 计划列表是否已拉过一次——用来区分"还没加载"和"确实一个计划都没有" */
const planLoaded = ref(false)
/** 当前生效的计划搜索词；用来区分"搜索无结果"和"项目下确实没有计划" */
const planSearchTerm = ref('')

const planStatusLabel: Record<number, string> = {
  0: '草稿',
  1: '进行中',
  2: '已完成',
  3: '已归档',
}

const selectedPlan = computed(() =>
  planOptions.value.find((p) => p.id === reportPlanId.value) ?? null)

// 搜索无结果时说"没有匹配的计划"，而不是误报"该项目还没有计划"
const planEmptyText = computed(() =>
  planSearchTerm.value ? '没有匹配的测试计划' : '该项目还没有测试计划')

const reportTip = computed(() => {
  const projectName = reportProject.value?.name ?? ''

  if (reportKind.value === 'plan') {
    const planName = selectedPlan.value?.name
    return planName
      ? `将导出「${planName}」的验收报告：测试概览 + 达标判定 + 轮次趋势 + 模块明细 + 阻塞用例。`
      : '请选择要导出的测试计划。'
  }

  return `将汇总 ${projectName} 下所有用例的最新执行结果：测试概览 + 各模块明细（含步骤截图）+ 缺陷报告。`
})

/**
 * 拉取该项目的计划供下拉选择。
 *
 * 走**服务端搜索**（filterable + remote）而不是本地 `filterable`：
 * 项目下的计划可能超过一页，本地过滤只能搜到"已经加载进来的那些"，
 * 用户搜一个确实存在的计划却搜不到，比没有搜索更让人困惑。
 *
 * 失败时静默降级为空列表：无 ViewTestPlans 权限时这个接口会 403，
 * 但"按项目导出"是原有能力，不该因为没计划权限就整个用不了。
 */
const loadPlanOptions = async (projectId: string, searchTerm = '') => {
  if (!projectId) {
    planOptions.value = []
    planSearchTerm.value = searchTerm
    return
  }
  planLoading.value = true
  planLoaded.value = false
  // 搜索会整表替换选项。已选中的计划必须留在列表里，否则两个后果都很糟：
  //   1) el-select 找不到对应 option，会把 GUID 当标签显示出来
  //   2) selectedPlan 取不到值 → 报告提示变回"请选择计划" → 明明选了却导不了
  // 把它**置顶并单独标注**，而不是混进搜索结果里——
  // 否则搜「冒烟」时结果里出现一个不匹配的「回归验收」，用户会以为搜索坏了
  const previous = planOptions.value.find((p) => p.id === reportPlanId.value) ?? null
  try {
    const res = await listTestPlansApi({
      projectId,
      search: searchTerm.trim() || undefined,
      page: 1,
      pageSize: 50,
    })
    const rest = res.items
      .filter((p) => p.id !== reportPlanId.value)
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    const pinned = res.items.find((p) => p.id === reportPlanId.value) ?? previous
    planOptions.value = pinned ? [pinned, ...rest] : rest
  } catch {
    planOptions.value = previous ? [previous] : []
  } finally {
    planSearchTerm.value = searchTerm
    planLoading.value = false
    planLoaded.value = true
  }
}

// el-select 不做防抖，逐字符触发会把请求打满；这里自己压 300ms
let planSearchTimer: ReturnType<typeof setTimeout> | undefined

const onPlanSearch = (query: string) => {
  clearTimeout(planSearchTimer)
  planSearchTimer = setTimeout(() => {
    void loadPlanOptions(reportProject.value?.id ?? '', query)
  }, 300)
}

const openReport = (row: Project) => {
  reportProject.value = row
  reportRange.value = null
  reportKind.value = 'project'
  reportPlanId.value = ''
  planOptions.value = []
  planSearchTerm.value = ''
  reportDialogVisible.value = true
  void loadPlanOptions(row.id)
}

/** 关窗时取消待发的搜索请求：用户已经关掉弹窗了，再打一次接口没有意义 */
const onReportDialogClosed = () => {
  clearTimeout(planSearchTimer)
}

const handleDownloadReport = async () => {
  const project = reportProject.value
  if (!project) return

  // 计划验收报告：走后端已有的计划导出接口，产物是"概览 + 达标判定 + 趋势 + 模块 + 阻塞用例"
  if (reportKind.value === 'plan') {
    const plan = selectedPlan.value
    if (!plan) {
      ElMessage.warning('请选择要导出的测试计划')
      return
    }
    reporting.value = true
    try {
      await exportPlanReportApi(plan.id, plan.name)
      ElMessage.success('报告已生成')
      reportDialogVisible.value = false
    } finally {
      reporting.value = false
    }
    return
  }

  reporting.value = true
  try {
    // 项目汇总报告只按日期区间收敛；要按计划看结果请用「测试计划验收报告」
    const blob = await downloadProjectReport(project.id, {
      from: reportRange.value?.[0],
      to: reportRange.value?.[1],
    })
    saveBlobAsFile(blob, `测试报告_${project.name}.xlsx`)
    ElMessage.success('报告已生成')
    reportDialogVisible.value = false
  } finally {
    reporting.value = false
  }
}

const dialogVisible = ref(false)
const editing = ref<Project | null>(null)
const saving = ref(false)
const formRef = ref<FormInstance>()
const form = reactive({
  name: '',
  description: '',
  managerId: '' as string,
  testOwnerId: '' as string,
  developerOwnerId: '' as string,
  // M8 Agent 自愈（项目级）
  agentLoopEnabled: false,
  treatAgentHealedAsPass: false,
})
const formRules: FormRules = {
  name: [{ required: true, message: '请输入项目名称', trigger: 'blur' }],
}

// 负责人下拉的候选。走 /users/options 而不是 /users——
// 后者要求 ManageUsers（实际只有管理员有），非管理员建项目时下拉会是空的
const userOptions = ref<UserOption[]>([])
const userOptionsLoaded = ref(false)

const loadUserOptions = async () => {
  if (userOptionsLoaded.value) return
  try {
    userOptions.value = await listUserOptionsApi()
  } catch {
    userOptions.value = []
  } finally {
    userOptionsLoaded.value = true
  }
}

const openCreate = () => {
  editing.value = null
  void loadUserOptions()
  dialogVisible.value = true
}

const openEdit = (row: Project) => {
  editing.value = row
  form.name = row.name
  form.description = row.description ?? ''
  form.managerId = row.managerId ?? ''
  form.testOwnerId = row.testOwnerId ?? ''
  form.developerOwnerId = row.developerOwnerId ?? ''
  form.agentLoopEnabled = row.agentLoopEnabled ?? false
  form.treatAgentHealedAsPass = row.treatAgentHealedAsPass ?? false
  void loadUserOptions()
  dialogVisible.value = true
}

const resetForm = () => {
  form.name = ''
  form.description = ''
  form.managerId = ''
  form.testOwnerId = ''
  form.developerOwnerId = ''
  form.agentLoopEnabled = false
  form.treatAgentHealedAsPass = false
  formRef.value?.clearValidate()
}

const handleSave = async () => {
  try {
    await formRef.value?.validate()
  } catch {
    return
  }
  saving.value = true
  try {
    // 空串是 el-select 清空后的值，转成 null 才是「不指定」；
    // 直接传空串会让后端的 Guid? 绑定失败
    const payload = {
      name: form.name,
      description: form.description || undefined,
      managerId: form.managerId || null,
      testOwnerId: form.testOwnerId || null,
      developerOwnerId: form.developerOwnerId || null,
      agentLoopEnabled: form.agentLoopEnabled,
      treatAgentHealedAsPass: form.treatAgentHealedAsPass,
    }

    if (editing.value) {
      await updateProject(editing.value.id, payload)
      ElMessage.success('项目已更新')
    } else {
      await createProject(payload)
      ElMessage.success('项目已创建')
    }
    dialogVisible.value = false
    await load()
  } finally {
    saving.value = false
  }
}

const handleDelete = async (row: Project) => {
  try {
    await ElMessageBox.confirm(`确认删除项目「${row.name}」？`, '提示', { type: 'warning' })
  } catch {
    return
  }
  await deleteProject(row.id)
  ElMessage.success('项目已删除')
  await load()
}

const goDetail = (row: Project) => {
  router.push(`/projects/${row.id}`)
}

onMounted(() => load())
</script>

<style scoped>
/* 页面撑满视口：卡片自适应高度，表格区域内部滚动，分页固定在底部 */
.project-list {
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

.report-tip {
  margin: 0 0 12px;
  color: var(--el-text-color-regular);
  font-size: 13px;
  line-height: 1.6;
}

/* 负责人下拉右侧的标记（已停用 / 无邮箱），靠右弱化，别和姓名抢注意力 */
.user-meta {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-left: 16px;
  white-space: nowrap;
}

/* 表格里"没有负责人"的占位，用弱色区分「空」和「名字很短的真人」 */
.muted {
  color: var(--el-text-color-placeholder);
}

.field-hint {
  margin-top: 4px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.5;
}

/* 计划下拉里的状态与用例数靠右弱化显示，避免和计划名抢注意力 */
.plan-meta {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-left: 16px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  white-space: nowrap;
}

.toolbar {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
  flex-shrink: 0;
}

/* 刷新按钮推到工具栏最右 */
.toolbar-spacer {
  flex: 1;
}

.search-input {
  width: 300px;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
  flex-shrink: 0;
}
</style>
