<template>
  <div class="plan-list">
    <el-card class="list-card">
      <div class="toolbar">
        <div class="toolbar-left">
          <el-select v-model="projectId" placeholder="全部项目" clearable class="w-200" @change="load(1)">
            <el-option v-for="p in projects" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
          <el-select v-model="statusFilter" placeholder="全部状态" clearable class="w-140" @change="load(1)">
            <el-option v-for="(label, value) in STATUS_LABELS" :key="value" :label="label" :value="Number(value)" />
          </el-select>
          <el-select v-model="releaseFilter" placeholder="全部版本" clearable class="w-160" @change="load(1)">
            <el-option v-for="r in releases" :key="r" :label="r" :value="r" />
          </el-select>
          <el-input v-model="search" placeholder="搜索计划名称" clearable class="w-200" @keyup.enter="load(1)"
            @clear="load(1)" />
          <el-button type="primary" :icon="Search" @click="load(1)">查询</el-button>
          <el-button :icon="Refresh" @click="load()">刷新</el-button>
        </div>
        <div class="toolbar-right">
          <span v-for="(label, value) in STATUS_LABELS" :key="value" class="status-count">
            {{ label }} <b>{{ statusCounts[statusKeys[Number(value)]] ?? 0 }}</b>
          </span>
          <el-button type="danger" :icon="Delete" :disabled="selectedRows.length === 0" :loading="deleting"
            @click="handleBatchDelete">
            批量删除（{{ selectedRows.length }}）
          </el-button>
          <!-- 不再要求先选筛选区的项目：所属项目现在由表单自己选 -->
          <el-button type="primary" :icon="Plus" @click="openCreate">
            新建计划
          </el-button>

        </div>
      </div>

      <el-alert type="warning" :closable="false" class="boundary-tip">
        <template #title>
          <b>测试计划</b>管「验收」：有起止时间、版本标识、目标通过率与多轮执行，可判定达标。
          只想把一批用例存起来反复跑，请用
          <el-link type="primary" :underline="false" @click="router.push('/suites')">测试套件</el-link>。
        </template>
      </el-alert>

      <div v-if="!isMobile" class="table-wrap">
        <el-table ref="tableRef" v-loading="loading" :data="plans" row-key="id" height="100%"
          @selection-change="onSelectionChange" @row-click="handleRowSelectionClick">
          <el-table-column type="selection" width="44" />
          <el-table-column label="计划名称" min-width="200" fixed="left">
            <template #default="{ row }">
              <div class="name-cell">
                <!-- 行点击是勾选；名称链接进详情（.stop 防止误勾选） -->
                <el-link type="primary" :underline="false" @click.stop="goDetail(row)">{{ row.name }}</el-link>
                <el-tag v-if="row.releaseName" size="small" effect="plain" type="info">
                  {{ row.releaseName }}
                </el-tag>
              </div>
            </template>
          </el-table-column>
          <!-- 所属项目：筛「全部项目」时靠它区分计划归属；直接点进项目详情 -->
          <el-table-column label="所属项目" min-width="140" show-overflow-tooltip>
            <template #default="{ row }">
              <el-button v-if="row.projectName" link type="primary"
                @click.stop="router.push(`/projects/${row.projectId}`)">
                {{ row.projectName }}
              </el-button>
              <span v-else class="muted">—</span>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="statusTagType(row.status)" size="small" effect="light">
                {{ STATUS_LABELS[row.status] }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="进度" width="200">
            <template #default="{ row }">
              <div v-if="row.runningRoundNo" class="progress-cell">
                <el-progress :percentage="progressPercent(row)" :stroke-width="10" />
                <span class="progress-text">
                  第 {{ row.runningRoundNo }} 轮 {{ row.runningPassedCount }}/{{ row.runningCaseCount }}
                </span>
              </div>
              <div v-else-if="row.lastPassRate !== null && row.lastPassRate !== undefined" class="progress-cell">
                <el-tag :type="row.lastPassRate >= row.targetPassRate ? 'success' : 'danger'" size="small"
                  effect="plain">
                  第 {{ row.lastRoundNo }} 轮 {{ (row.lastPassRate * 100).toFixed(1) }}%
                </el-tag>
                <span class="progress-text">目标 {{ (row.targetPassRate * 100).toFixed(0) }}%</span>
              </div>
              <span v-else class="muted">尚未执行</span>
            </template>
          </el-table-column>
          <el-table-column label="用例数" width="80" align="center">
            <template #default="{ row }">{{ row.caseCount }}</template>
          </el-table-column>
          <el-table-column label="负责人" width="110">
            <template #default="{ row }">{{ row.ownerName || '—' }}</template>
          </el-table-column>
          <el-table-column label="周期" width="180">
            <template #default="{ row }">
              <span v-if="row.startsAt || row.endsAt" class="period">
                {{ formatDate(row.startsAt) }} ~ {{ formatDate(row.endsAt) }}
              </span>
              <span v-else class="muted">未设置</span>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="270" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click.stop="goDetail(row)">详情</el-button>
              <el-button link type="primary" :disabled="row.caseCount === 0"
                @click.stop="handleStartRound(row)">开新一轮</el-button>
              <el-button link type="primary" @click.stop="openEdit(row)">编辑</el-button>
              <el-button link type="primary" @click.stop="handleCopy(row)">复制</el-button>
              <el-button link type="danger" @click.stop="handleDeleteOne(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- 窄屏：表格换成卡片（列宽合计 1334px，是 375px 视口的 3.6 倍）。
           桌面版行点击进详情；卡片上没有「点整行」的语义，改为明确的「详情」按钮。 -->
      <MobileCardList v-else v-loading="loading" :items="plans" :row-key="(row) => row.id" empty-text="暂无测试计划">
        <template #title="{ item }">
          <el-link type="primary" :underline="false" @click="goDetail(item)">{{ item.name }}</el-link>
        </template>

        <template #badge="{ item }">
          <el-tag v-if="item.releaseName" size="small" effect="plain" type="info">{{ item.releaseName }}</el-tag>
          <el-tag :type="statusTagType(item.status)" size="small" effect="light">{{ STATUS_LABELS[item.status]
          }}</el-tag>
        </template>

        <template #meta="{ item }">
          <span><span class="mcl-label">项目</span>{{ item.projectName || '—' }}</span>
          <span><span class="mcl-label">用例数</span>{{ item.caseCount }}</span>
          <span><span class="mcl-label">负责人</span>{{ item.ownerName || '—' }}</span>
          <span v-if="item.runningRoundNo">
            <span class="mcl-label">进行中</span>第 {{ item.runningRoundNo }} 轮 {{ item.runningPassedCount }}/{{
              item.runningCaseCount }}
          </span>
          <span v-else-if="item.lastPassRate !== null && item.lastPassRate !== undefined">
            <span class="mcl-label">上轮</span>第 {{ item.lastRoundNo }} 轮 {{ (item.lastPassRate * 100).toFixed(1) }}%（目标
            {{ (item.targetPassRate * 100).toFixed(0) }}%）
          </span>
          <span v-else><span class="mcl-label">进度</span>尚未执行</span>
          <span v-if="item.startsAt || item.endsAt">
            <span class="mcl-label">周期</span>{{ formatDate(item.startsAt) }} ~ {{ formatDate(item.endsAt) }}
          </span>
        </template>

        <template #actions="{ item }">
          <el-button link type="primary" @click="goDetail(item)">详情</el-button>
          <el-button link type="primary" :disabled="item.caseCount === 0"
            @click="handleStartRound(item)">开新一轮</el-button>
          <el-button link type="primary" @click="openEdit(item)">编辑</el-button>
          <el-button link type="primary" @click="handleCopy(item)">复制</el-button>
          <el-button link type="danger" @click="handleDeleteOne(item)">删除</el-button>
        </template>
      </MobileCardList>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize" :total="total"
        :page-sizes="[10, 20, 50, 100]" layout="total, sizes, prev, pager, next" @current-change="load()"
        @size-change="load(1)" />
    </el-card>

    <!-- 新建 / 编辑 -->
    <el-dialog v-model="dialogVisible" :title="editingId ? '编辑计划' : '新建测试计划'" width="80%" top="6vh" @closed="resetForm">
      <el-form :model="form" label-width="104px">
        <!-- 所属项目放在最前：它决定了后面「默认环境」能选什么，先选它顺序才对得上 -->
        <el-form-item label="所属项目" required>
          <el-select v-model="form.projectId" filterable remote reserve-keyword :remote-method="onProjectSearch"
            :loading="projectLoading" :disabled="!!editingId" placeholder="搜索并选择项目" class="w-full"
            @change="onProjectChange">
            <!-- 已选中的项目会被置顶保留（不置顶的话搜索时会从列表里消失，
                 el-select 就只能把 id 当标签显示），并标「当前」避免被误认成搜索结果 -->
            <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id">
              <span>{{ p.name }}</span>
              <span class="option-meta">
                <el-tag v-if="p.id === form.projectId" size="small" effect="plain" type="primary">当前</el-tag>
                {{ p.testCaseCount }} 条用例
              </span>
            </el-option>
          </el-select>
          <div v-if="editingId" class="form-hint">计划创建后不能换项目（已产生的轮次与报告都挂在原项目下）。</div>
        </el-form-item>
        <el-form-item label="计划名称" required>
          <el-input v-model="form.name" placeholder="例如 v2.3.0 发版验收" />
        </el-form-item>
        <!-- 版本标识与负责人同行（均为可留空的弱必填项，各占半行） -->
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="版本标识">
              <el-input v-model="form.releaseName" placeholder="例如 v2.3.0、2026-09 迭代" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="负责人">
              <el-select v-model="form.ownerId" placeholder="默认当前用户" clearable class="w-full">
                <el-option v-for="u in users" :key="u.id" :label="`${u.displayName}（${u.username}）`" :value="u.id" />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="周期">
          <el-date-picker v-model="period" type="daterange" value-format="YYYY-MM-DD" start-placeholder="开始"
            end-placeholder="结束" unlink-panels class="w-full" />
        </el-form-item>
        <el-form-item label="目标通过率">
          <el-slider v-model="targetPercent" :min="50" :max="100" :step="1" show-input class="target-slider" />
        </el-form-item>
        <el-form-item label="达标口径">
          <el-radio-group v-model="form.gateMode">
            <el-radio-button :value="0">最后一轮达标</el-radio-button>
            <el-radio-button :value="1">任意一轮达标</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="质量口径">
          <el-checkbox v-model="form.allowErrors">允许存在 Error 执行（环境异常也算通过）</el-checkbox>
          <el-checkbox v-model="form.excludeFlakyFromFailure">
            排除不稳定用例（flaky）的失败样本
          </el-checkbox>
        </el-form-item>
        <el-form-item label="缺陷门槛">
          <el-checkbox v-model="form.defectGateEnabled">
            项目存在未闭环致命/严重缺陷时不达标（回归全部通过自动闭环 Fixed 缺陷）
          </el-checkbox>
        </el-form-item>
        <!-- 默认环境与浏览器矩阵同行（都是执行参数，语义相邻） -->
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="默认环境">
              <el-select v-model="form.environmentId" clearable :loading="envLoading"
                :placeholder="form.projectId ? '不指定则用用例自身地址' : '请先选择所属项目'" class="w-full">
                <el-option v-for="e in envOptions" :key="e.id" :label="e.name" :value="e.id" />
              </el-select>
              <div v-if="form.projectId && !envLoading && envOptions.length === 0" class="form-hint">
                该项目下还没有环境，可先在「项目详情」里添加。
              </div>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="浏览器矩阵">
              <el-select v-model="form.browsers" multiple clearable placeholder="留空则按用例/环境解析" class="w-full">
                <el-option v-for="b in browserOptions" :key="b.id" :label="b.name" :value="b.id" />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="描述">
          <el-input v-model="form.description" type="textarea" :rows="2" placeholder="可选" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>

    <!-- 计划没配默认环境时开轮先选环境：相对地址 Navigate / 自动登录都依赖环境的 BaseUrl -->
    <el-dialog v-model="envDialogVisible" title="选择执行环境" width="520px">
      <div v-loading="envLoading">
        <el-select v-model="selectedEnvId" style="width: 100%" placeholder="选择环境">
          <el-option label="不使用环境" value="" />
          <el-option v-for="env in envOptions" :key="env.id" :label="env.name" :value="env.id">
            <div class="environment-option">
              <span>{{ env.name }}</span>
              <span class="environment-option-url">{{ env.baseUrl }}</span>
            </div>
          </el-option>
        </el-select>
        <div class="env-hint">不选环境时，UI 用例里的相对地址（如 /login）将无法拼接完整 URL，自动登录也不会执行。</div>
      </div>
      <template #footer>
        <el-button @click="envDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="confirmStartRound">开始新一轮</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Plus, Refresh, Search } from '@element-plus/icons-vue'
import {
  batchDeleteTestPlansApi, copyTestPlanApi, createTestPlanApi, listTestPlansApi,
  startPlanRoundApi, testPlanReleasesApi, testPlanStatusCountsApi, updateTestPlanApi,
} from '@/api/testPlan'
import { getProjects } from '@/api/project'
import { getEnvironments } from '@/api/environment'
import { listUsersApi } from '@/api/auth'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useRowSelection } from '@/composables/useRowSelection'
import { usePagedList } from '@/composables/usePagedList'
import { TestPlanStatus, TEST_PLAN_STATUS_LABELS as STATUS_LABELS, type TestPlanSummary } from '@/types/testPlan'
import type { Project } from '@/types/project'
import type { EnvironmentView } from '@/types/environment'
import type { UserView } from '@/types/auth'
import MobileCardList from '@/components/common/MobileCardList.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'

/** 后端 summary 用枚举名字符串作键，与数字状态码要有一份映射 */
const statusKeys: Record<number, string> = {
  [TestPlanStatus.Draft]: 'Draft',
  [TestPlanStatus.Active]: 'Active',
  [TestPlanStatus.Completed]: 'Completed',
  [TestPlanStatus.Archived]: 'Archived',
}

const statusTagType = (status: number) =>
  status === TestPlanStatus.Active ? 'primary'
    : status === TestPlanStatus.Completed ? 'success'
      : status === TestPlanStatus.Archived ? 'info' : 'warning'

const browserOptions = [
  { id: 'chromium', name: 'Chromium' },
  { id: 'firefox', name: 'Firefox' },
  { id: 'webkit', name: 'WebKit' },
]

const router = useRouter()

/** 窄屏（< 1024px）：表格换成卡片形态，见下方模板 */
const { isMobile } = useBreakpoint()
/** 顶部筛选用的项目列表（不带搜索，仅首屏 100 条） */
const projects = ref<Project[]>([])
/** 表单里的项目下拉：独立一份，走服务端搜索，避免项目一多就搜不到 */
const projectOptions = ref<Project[]>([])
const projectLoading = ref(false)
/** 表单里的环境下拉：随所选项目变化，与筛选区的项目无关 */
const envOptions = ref<EnvironmentView[]>([])
const envLoading = ref(false)
const users = ref<UserView[]>([])
const releases = ref<string[]>([])
const statusCounts = ref<Record<string, number>>({})
const saving = ref(false)
// 开轮前环境选择（计划未配默认环境时弹出）：envOptions/envLoading 复用上方"默认环境"下拉的同一份
const envDialogVisible = ref(false)
const selectedEnvId = ref('')
const envRow = ref<TestPlanSummary | null>(null)
// 不预设项目：进来先看「全部项目」下的进行中计划，而不是替用户挑一个项目
const projectId = ref('')
// 默认只看「进行中」：计划是用来做验收的，草稿和已归档的会淹没真正需要关注的
const statusFilter = ref<number | ''>(TestPlanStatus.Active)
const releaseFilter = ref('')
const search = ref('')
// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<TestPlanSummary>((p, ps) => listTestPlansApi({
  projectId: projectId.value || undefined,
  status: statusFilter.value === '' ? undefined : (statusFilter.value as 0),
  releaseName: releaseFilter.value || undefined,
  search: search.value.trim() || undefined,
  page: p,
  pageSize: ps,
}), { pageSize: 20 })
const { items: plans, total, page, pageSize, loading } = toRefs(list)
const load = (targetPage?: number) => list.load(targetPage)

const dialogVisible = ref(false)
const editingId = ref<string | null>(null)
const period = ref<[string, string] | null>(null)
const targetPercent = ref(95)
const form = reactive({
  projectId: '',
  name: '', description: '', releaseName: '', ownerId: '',
  allowErrors: false, excludeFlakyFromFailure: true, gateMode: 0, defectGateEnabled: false,
  environmentId: '', browsers: [] as string[],
})

const progressPercent = (row: TestPlanSummary) => {
  if (!row.runningCaseCount) return 0
  return Math.round(((row.runningPassedCount ?? 0) / row.runningCaseCount) * 100)
}

const formatDate = (value?: string | null) => (value ? value.slice(0, 10) : '—')

async function loadMeta() {
  const [counts, rel] = await Promise.all([
    testPlanStatusCountsApi(projectId.value || undefined),
    testPlanReleasesApi(projectId.value || undefined),
  ])
  statusCounts.value = counts
  releases.value = rel
}

async function loadOptions() {
  const res = await getProjects({ page: 1, pageSize: 100 })
  projects.value = res.items
  // 刻意**不**自动选中第一个项目：原来那样会让列表只显示某一个项目的计划，
  // 而跨项目验收时用户根本不知道自己被"过滤"了（第一个项目没计划时页面还会空白）。
  // 负责人下拉需要用户列表，但那要求 ManageUsers 权限——普通测试工程师拿不到
  try {
    const page = await listUsersApi({ page: 1, pageSize: 100 })
    users.value = page.items
  } catch {
    users.value = []
  }
}

/**
 * 表单里的项目下拉（服务端搜索）。
 * 方法名与 useBatchDelete 那套无关，纯粹是这个页面内部的选项加载器。
 */
async function loadProjectOptions(searchTerm = '') {
  projectLoading.value = true
  try {
    const res = await getProjects({
      search: searchTerm.trim() || undefined,
      page: 1,
      pageSize: 50,
    })
    // 已选中的项目必须留在列表里，否则 el-select 找不到对应 option 会直接把 id 显示出来
    projectOptions.value = res.items
    if (form.projectId && !res.items.some((p) => p.id === form.projectId)) {
      const current = projects.value.find((p) => p.id === form.projectId)
      if (current) projectOptions.value = [current, ...res.items]
    }
  } catch {
    projectOptions.value = []
  } finally {
    projectLoading.value = false
  }
}

// el-select 不做防抖，逐字符触发会把请求打满
let projectSearchTimer: ReturnType<typeof setTimeout> | undefined
function onProjectSearch(query: string) {
  clearTimeout(projectSearchTimer)
  projectSearchTimer = setTimeout(() => void loadProjectOptions(query), 300)
}

/** 环境必须跟着项目走：A 项目的环境挂到 B 项目上没有任何意义 */
async function loadEnvironmentsFor(target: string) {
  if (!target) {
    envOptions.value = []
    return
  }
  envLoading.value = true
  try {
    envOptions.value = await getEnvironments(target)
  } catch {
    envOptions.value = []
  } finally {
    envLoading.value = false
  }
}

async function onProjectChange(target: string) {
  // 换项目后旧环境下拉里选中的那一条已经不属于当前项目，必须清掉，
  // 否则会把"别的项目下的环境 id"提交到后端，成为一条永远对不上的引用
  form.environmentId = ''
  await loadEnvironmentsFor(target)
}

function resetForm() {
  editingId.value = null
  period.value = null
  targetPercent.value = 95
  Object.assign(form, {
    projectId: '', name: '', description: '', releaseName: '', ownerId: '',
    allowErrors: false, excludeFlakyFromFailure: true, gateMode: 0, defectGateEnabled: false,
    environmentId: '', browsers: [],
  })
  envOptions.value = []
}

function openCreate() {
  resetForm()
  // 筛选区选了项目就沿用（"在哪个项目下点新建"就是那个项目），没选就留空由用户自己挑——
  // 这里不再回落到"第一个项目"，那等于替用户在多个项目里替他做选择
  form.projectId = projectId.value
  projectOptions.value = projects.value.slice()
  void loadEnvironmentsFor(form.projectId)
  dialogVisible.value = true
}

function openEdit(row: TestPlanSummary) {
  editingId.value = row.id
  form.projectId = row.projectId
  // 编辑时项目不可变更，选项里只要有当前这一个就够（避免列表未加载时显示成 id）
  const current = projects.value.find((p) => p.id === row.projectId)
  projectOptions.value = current ? [current] : []
  void loadEnvironmentsFor(row.projectId)
  form.name = row.name
  form.description = row.description ?? ''
  form.releaseName = row.releaseName ?? ''
  form.ownerId = row.ownerId ?? ''
  form.allowErrors = row.allowErrors
  form.excludeFlakyFromFailure = row.excludeFlakyFromFailure
  form.gateMode = row.gateMode
  form.defectGateEnabled = row.defectGateEnabled
  targetPercent.value = Math.round(row.targetPassRate * 100)
  period.value = row.startsAt && row.endsAt ? [row.startsAt.slice(0, 10), row.endsAt.slice(0, 10)] : null
  dialogVisible.value = true
}

async function handleSave() {
  if (!editingId.value && !form.projectId) {
    ElMessage.warning('请选择所属项目')
    return
  }
  if (!form.name.trim()) {
    ElMessage.warning('请填写计划名称')
    return
  }
  const payload = {
    name: form.name.trim(),
    description: form.description || null,
    releaseName: form.releaseName || null,
    startsAt: period.value?.[0] ?? null,
    endsAt: period.value?.[1] ?? null,
    ownerId: form.ownerId || null,
    targetPassRate: targetPercent.value / 100,
    allowErrors: form.allowErrors,
    excludeFlakyFromFailure: form.excludeFlakyFromFailure,
    gateMode: form.gateMode,
    defectGateEnabled: form.defectGateEnabled,
    environmentId: form.environmentId || null,
    browsers: form.browsers.length > 0 ? form.browsers : null,
  }

  saving.value = true
  try {
    if (editingId.value) {
      await updateTestPlanApi(editingId.value, payload)
      ElMessage.success('已保存')
    } else {
      await createTestPlanApi({ ...payload, projectId: form.projectId })
      // 建到了别的项目下时，把筛选切过去——否则新计划根本不在当前列表里，
      // 用户会以为"创建失败"（弹窗已关、列表没变化）
      if (projectId.value !== form.projectId) projectId.value = form.projectId
      ElMessage.success('已创建，接下来为它设置用例范围')
    }
    dialogVisible.value = false
    await load()
    await loadMeta()
  } finally {
    saving.value = false
  }
}

function goDetail(row: TestPlanSummary) {
  router.push(`/test-plans/${row.id}`)
}

async function handleStartRound(row: TestPlanSummary) {
  // 计划没配默认环境 → 先弹环境选择框（项目里有环境可选时才弹）。
  // 相对地址 Navigate 和自动登录都依赖环境的 BaseUrl，不选环境 UI 用例必挂。
  if (!row.environmentId) {
    envRow.value = row
    envLoading.value = true
    try {
      envOptions.value = await getEnvironments(row.projectId)
    } finally {
      envLoading.value = false
    }
    if (envOptions.value.length > 0) {
      selectedEnvId.value = ''
      envDialogVisible.value = true
      return // 确认后走 confirmStartRound
    }
  }

  await confirmAndStart(row, undefined)
}

/** 环境对话框确认：带所选环境开轮 */
async function confirmStartRound() {
  envDialogVisible.value = false
  if (envRow.value) await confirmAndStart(envRow.value, selectedEnvId.value || undefined)
}

async function confirmAndStart(row: TestPlanSummary, environmentId?: string) {
  await ElMessageBox.confirm(
    `将按当前范围（${row.caseCount} 个用例）开启「${row.name}」的新一轮执行。` +
    '同一计划同时只能有一轮在进行中。',
    '开启新一轮',
    { type: 'info' },
  )
  const result = await startPlanRoundApi(row.id, environmentId ? { environmentId } : undefined)
  ElMessage.success(`第 ${result.roundNo} 轮已开始，共创建 ${result.created} 条执行`)
  await load()
  await loadMeta()
}

async function handleCopy(row: TestPlanSummary) {
  // 复制不带轮次：新版本要从干净的历史开始（后端保证）
  await copyTestPlanApi(row.id)
  ElMessage.success('已复制为新计划（不含轮次记录）')
  await load()
}

async function handleDeleteOne(row: TestPlanSummary) {
  try {
    await ElMessageBox.confirm(`确认删除计划「${row.name}」？`, '删除', { type: 'warning' })
  } catch {
    return
  }
  const result = await batchDeleteTestPlansApi([row.id])
  if (result.skipped.length > 0) {
    ElMessage.warning(result.skipped[0].reason)
  } else {
    ElMessage.success('已删除')
  }
  await load()
  await loadMeta()
}

// 点击行直接勾选/取消勾选（复选框列与操作列除外）；进详情走名称链接
const { tableRef, handleRowSelectionClick } = useRowSelection()

const { deleting, selectedRows, onSelectionChange, handleBatchDelete } = useBatchDelete<TestPlanSummary>({
  entity: '测试计划',
  remove: batchDeleteTestPlansApi,
  reload: load,
})

onMounted(async () => {
  await loadOptions()
  await Promise.all([load(), loadMeta()])
})
</script>

<style scoped>
/* 开轮环境选择：名称 + BaseUrl 两段展示，提示语解释"不选环境的后果" */
.environment-option {
  display: flex;
  justify-content: space-between;
  gap: 12px;
}

.environment-option-url {
  color: #9aa2ae;
  font-size: 12px;
}

.env-hint {
  margin-top: 8px;
  font-size: 12px;
  color: #9aa2ae;
  line-height: 1.6;
}

/* 项目下拉里靠右弱化显示用例数，避免和项目名抢注意力 */
.option-meta {
  display: inline-flex;
  margin-left: 16px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  white-space: nowrap;
}

/* 表单项下的补充说明 */
.form-hint {
  margin-top: 4px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.5;
}

.plan-list {
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
  flex-wrap: wrap;
}

.toolbar-right {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.w-140 {
  width: 140px;
}

.w-160 {
  width: 160px;
}

.w-200 {
  width: 200px;
}

.w-full {
  width: 100%;
}

.status-count {
  color: #7a8290;
  font-size: 13px;
  white-space: nowrap;
}

.status-count b {
  color: var(--el-text-color-primary);
}

.boundary-tip {
  margin-bottom: 12px;
}

.table-wrap {
  flex: 1;
  min-height: 0;
}

.name-cell {
  display: flex;
  align-items: center;
  gap: 8px;
}

.progress-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.progress-text {
  color: #9aa2ae;
  font-size: 12px;
}

.period {
  font-size: 13px;
}

.muted {
  color: #9aa2ae;
}

.pagination {
  margin-top: 12px;
  justify-content: flex-end;
}

.target-slider {
  width: 100%;
}

.field-hint {
  color: #9aa2ae;
  font-size: 12px;
  line-height: 1.7;
  margin-top: 2px;
}
</style>
