<template>
  <div class="defect-page">

    <!-- 统计卡：存量按严重度、近 7 天进出、修复/验证效率 -->
    <el-row :gutter="12" class="stats-row">
      <el-col :span="5">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">未闭环缺陷</div>
          <div class="stat-value">{{ stats?.openTotal ?? '—' }}</div>
          <div v-if="stats" class="stat-sub">
            致命 {{ stats.openCritical }} · 严重 {{ stats.openMajor }} · 一般 {{ stats.openNormal }} · 建议 {{
              stats.openSuggestion }}
          </div>
        </el-card>
      </el-col>
      <el-col :span="4">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">近 7 天新增</div>
          <div class="stat-value">{{ stats?.createdLast7Days ?? '—' }}</div>
        </el-card>
      </el-col>
      <el-col :span="4">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">近 7 天闭环</div>
          <div class="stat-value success">{{ stats?.closedLast7Days ?? '—' }}</div>
        </el-card>
      </el-col>
      <el-col :span="4">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">平均修复时长</div>
          <div class="stat-value">{{ formatHours(stats?.avgFixHours) }}</div>
        </el-card>
      </el-col>
      <el-col :span="4">
        <el-card shadow="never" class="stat-card">
          <div class="stat-label">平均验证时长</div>
          <div class="stat-value">{{ formatHours(stats?.avgVerifyHours) }}</div>
        </el-card>
      </el-col>
    </el-row>

    <el-card shadow="never" class="list-card">
      <!-- 过滤条 -->
      <div class="filter-bar">
        <el-select v-model="filters.projectId" placeholder="项目" clearable filterable style="width: 200px"
          @change="reload">
          <el-option v-for="p in projects" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-select v-model="filters.status" placeholder="状态" clearable style="width: 130px" @change="reload">
          <el-option v-for="(label, value) in DEFECT_STATUS_LABELS" :key="value" :label="label"
            :value="Number(value)" />
        </el-select>
        <el-select v-model="filters.severity" placeholder="严重度" clearable style="width: 130px" @change="reload">
          <el-option v-for="(label, value) in DEFECT_SEVERITY_LABELS" :key="value" :label="label"
            :value="Number(value)" />
        </el-select>
        <el-input v-model="filters.search" placeholder="搜索标题" clearable style="width: 200px" @keyup.enter="reload"
          @clear="reload" />
        <el-button type="primary" :icon="Search" @click="reload">查询</el-button>
        <el-button v-if="authStore.can(Permission.ManageTestCases)" type="primary" :icon="Plus"
          @click="openCreate">新建缺陷</el-button>
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
          <el-table-column label="标题" min-width="260" fixed="left">
            <template #default="{ row }">
              <el-link type="primary" @click.stop="openDetail(row.id)">{{ row.title }}</el-link>
            </template>
          </el-table-column>
          <el-table-column label="严重度" width="90">
            <template #default="{ row }">
              <el-tag size="small" :type="severityTagType(row.severity)" effect="light">
                {{ DEFECT_SEVERITY_LABELS[row.severity] ?? row.severity }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="90">
            <template #default="{ row }">
              <el-tag size="small" :type="statusTagType(row.status)">{{ DEFECT_STATUS_LABELS[row.status] ?? row.status
                }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="projectName" label="项目" min-width="120" show-overflow-tooltip />
          <el-table-column label="负责人" width="110">
            <template #default="{ row }">{{ row.assignedToName || '—' }}</template>
          </el-table-column>
          <el-table-column label="提交人" width="110">
            <template #default="{ row }">{{ row.createdByName || '—' }}</template>
          </el-table-column>
          <el-table-column label="来源用例" min-width="140" show-overflow-tooltip>
            <template #default="{ row }">
              <!-- 有来源用例时可点进用例详情（.stop 防止同时触发行勾选） -->
              <el-link v-if="row.foundInTestCaseId" type="primary" :underline="false"
                @click.stop="goTestCase(row.foundInTestCaseId)">
                {{ row.foundInTestCaseName || '查看用例' }}
              </el-link>
              <span v-else>{{ row.foundInTestCaseName || '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column label="创建时间" width="160">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="闭环时间" width="160">
            <template #default="{ row }">{{ row.verifiedAt ? formatDateTime(row.verifiedAt) : '—' }}</template>
          </el-table-column>
          <el-table-column label="操作" width="130" fixed="right">
            <template #default="{ row }">
              <!-- 操作列才是大家默认找「编辑/删除」的地方。
                   权限与后端一致（PUT/DELETE 都要求 ManageTestCases），
                   否则只读账号能看到按钮、点了却 403 -->
              <template v-if="authStore.can(Permission.ManageTestCases)">
                <el-button size="small" type="primary" plain text @click="openEdit(row.id)">编辑</el-button>
                <el-popconfirm title="删除该缺陷？关联用例与复现流水会一并清理，且不可恢复" confirm-button-text="删除"
                  @confirm="handleDelete(row.id)">
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

      <!-- 窄屏：表格换成卡片（列宽合计 1240px，是 375px 视口的 3.3 倍） -->
      <MobileCardList v-else v-loading="loading" :items="items" :row-key="(row) => row.id" empty-text="暂无缺陷">
        <template #title="{ item }">
          <el-link type="primary" @click="openDetail(item.id)">{{ item.title }}</el-link>
        </template>

        <template #badge="{ item }">
          <el-tag size="small" :type="severityTagType(item.severity)" effect="light">
            {{ DEFECT_SEVERITY_LABELS[item.severity] ?? item.severity }}
          </el-tag>
          <el-tag size="small" :type="statusTagType(item.status)">
            {{ DEFECT_STATUS_LABELS[item.status] ?? item.status }}
          </el-tag>
        </template>

        <template #meta="{ item }">
          <span><span class="mcl-label">项目</span>{{ item.projectName || '—' }}</span>
          <span><span class="mcl-label">负责人</span>{{ item.assignedToName || '—' }}</span>
          <span><span class="mcl-label">提交人</span>{{ item.createdByName || '—' }}</span>
          <span>
            <span class="mcl-label">来源用例</span>
            <el-link v-if="item.foundInTestCaseId" type="primary" :underline="false"
              @click="goTestCase(item.foundInTestCaseId)">
              {{ item.foundInTestCaseName || '查看用例' }}
            </el-link>
            <template v-else>{{ item.foundInTestCaseName || '—' }}</template>
          </span>
          <span><span class="mcl-label">创建</span>{{ formatDateTime(item.createdAt) }}</span>
          <span v-if="item.verifiedAt"><span class="mcl-label">闭环</span>{{ formatDateTime(item.verifiedAt) }}</span>
        </template>

        <template #actions="{ item }">
          <el-button link type="primary" @click="openDetail(item.id)">查看详情</el-button>
          <template v-if="authStore.can(Permission.ManageTestCases)">
            <el-button link type="primary" @click="openEdit(item.id)">编辑</el-button>
            <el-popconfirm title="删除该缺陷？关联用例与复现流水会一并清理，且不可恢复" confirm-button-text="删除" @confirm="handleDelete(item.id)">
              <template #reference>
                <el-button link type="danger">删除</el-button>
              </template>
            </el-popconfirm>
          </template>
        </template>
      </MobileCardList>

      <el-pagination v-model:current-page="page" :page-size="pageSize" :total="total" layout="total, prev, pager, next"
        class="pager" @current-change="load" />
    </el-card>

    <!-- 新建缺陷 -->
    <el-dialog v-model="createVisible" :title="editingId ? '编辑缺陷' : '新建缺陷'" width="80%" top="3vh">
      <el-form label-width="90px">
        <el-form-item label="项目" :required="!editingId">
          <!-- 编辑时锁住项目：更新接口本来就不接收 projectId，解锁只会让人白改一遍 -->
          <el-select v-model="createForm.projectId" filterable :disabled="!!editingId" style="width: 100%"
            @change="onCreateProjectChange">
            <el-option v-for="p in projects" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="标题" required>
          <el-input v-model="createForm.title" maxlength="200" show-word-limit />
        </el-form-item>
        <el-form-item label="严重度" required>
          <el-select v-model="createForm.severity" style="width: 200px">
            <el-option v-for="(label, value) in DEFECT_SEVERITY_LABELS" :key="value" :label="label"
              :value="Number(value)" />
          </el-select>
        </el-form-item>
        <el-form-item label="修复负责人">
          <el-select v-model="createForm.assignedToId" filterable clearable style="width: 100%">
            <el-option v-for="u in userOptions" :key="u.id" :label="u.name" :value="u.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="关联用例">
          <!-- 必须 remote：用例可能上千条，只用本地 filterable 只能搜到"已加载进来的那些"，
               用户搜一个确实存在的用例却搜不到，比没有搜索更让人困惑 -->
          <el-select v-model="createForm.testCaseIds" class="case-select" multiple filterable remote reserve-keyword
            clearable collapse-tags collapse-tags-tooltip :remote-method="onCaseSearch" :loading="caseLoading"
            :disabled="!createForm.projectId" placeholder="可选，按项目筛选、支持搜索" style="width: 100%">
            <el-option v-for="c in caseSelectOptions" :key="c.id" :label="c.name" :value="c.id">
              <span>{{ c.name }}</span>
              <span class="option-meta">
                <el-tag v-if="c.module" size="small" effect="plain">{{ c.module }}</el-tag>
              </span>
            </el-option>
          </el-select>
          <!-- 这句不是装饰：关联用例会参与「回归通过自动验证」，
               不写清楚的话用户不知道勾一下等于给缺陷开了一个自动闭环开关 -->
          <div class="form-hint">
            关联的用例回归通过时，处于「已修复」的缺陷会自动验证闭环。
          </div>
        </el-form-item>
        <el-form-item label="描述">
          <RichTextEditor v-model="createForm.description" :height="200"
            placeholder="复现步骤、期望/实际结果；从执行记录一键转缺陷时会自动带上错误信息、AI 诊断与截图" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitCreate">{{ editingId ? '保存' : '创建' }}</el-button>
      </template>
    </el-dialog>

    <!-- 缺陷详情抽屉：信息 + 状态流转 + 关联 -->
    <el-drawer v-model="detailVisible" size="50%" :title="detail?.title ?? '缺陷详情'">
      <template v-if="detail">
        <el-descriptions :column="2" size="small" border>
          <el-descriptions-item label="严重度">
            <el-tag size="small" :type="severityTagType(detail.severity)">
              {{ DEFECT_SEVERITY_LABELS[detail.severity] }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="状态">
            <el-tag size="small" :type="statusTagType(detail.status)">
              {{ DEFECT_STATUS_LABELS[detail.status] }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="项目">{{ detail.projectName }}</el-descriptions-item>
          <el-descriptions-item label="负责人">{{ detail.assignedToName || '—' }}</el-descriptions-item>
          <el-descriptions-item label="提交人">{{ detail.createdByName || '—' }}</el-descriptions-item>
          <el-descriptions-item label="创建时间">{{ formatDateTime(detail.createdAt) }}</el-descriptions-item>
          <el-descriptions-item label="修复时间">{{ detail.fixedAt ? formatDateTime(detail.fixedAt) : '—'
            }}</el-descriptions-item>
          <el-descriptions-item label="验证时间">
            {{ detail.verifiedAt ? `${formatDateTime(detail.verifiedAt)}（${detail.verifiedByName ?? '—'}）` : '—' }}
          </el-descriptions-item>
        </el-descriptions>

        <div v-if="detail.foundInTestCaseName || detail.foundInExecutionId" class="detail-section">
          <div class="section-title">来源</div>
          <div class="source-line">
            <span v-if="detail.foundInTestCaseName">用例：{{ detail.foundInTestCaseName }}</span>
            <el-link v-if="detail.foundInExecutionId" type="primary"
              @click="goExecution(detail.foundInExecutionId)">查看来源执行</el-link>
          </div>
        </div>

        <div v-if="externalProviders.length > 0" class="detail-section">
          <div class="section-title">外部缺陷系统</div>
          <div class="source-line">
            <template v-if="detail.externalRef">
              <el-tag size="small" type="success" effect="plain">{{ detail.externalRef }}</el-tag>
              <el-link type="primary" :href="buildExternalUrl(externalProviders, detail.externalRef) ?? undefined"
                target="_blank">在外部系统中查看</el-link>
            </template>
            <template v-else-if="authStore.can(Permission.ManageTestCases)">
              <el-button v-for="p in externalProviders" :key="p.id" size="small" :loading="pushingProvider === p.id"
                @click="handlePushExternal(p)">
                推送到{{ p.name }}
              </el-button>
            </template>
            <span v-else class="muted">尚未推送</span>
          </div>
        </div>

        <div v-if="descriptionParts.body || descriptionParts.screenshot || descriptionParts.trace"
          class="detail-section">
          <div class="section-title">描述与证据</div>
          <RichTextViewer v-if="descriptionParts.body" :value="descriptionParts.body" />
          <!-- 截图与执行回放：后端写进描述的是原始路径（见 DefectService 的证据拼装），
               直接显示既看不懂也用不上。这里把这两行抽出来渲染成缩略图与播放入口 -->
          <div v-if="descriptionParts.screenshot || descriptionParts.trace" class="evidence-row">
            <el-image v-if="descriptionParts.screenshot" :src="descriptionParts.screenshot"
              :preview-src-list="[descriptionParts.screenshot]" fit="cover" class="evidence-thumb"
              preview-teleported />
            <div class="evidence-actions">
              <el-button v-if="detail.foundInExecutionId" size="small" type="primary" plain
                :loading="loadingReplay" @click="openReplay">播放执行回放</el-button>
              <el-link v-if="descriptionParts.trace" type="primary" @click="downloadTrace">
                下载 trace（含每步 DOM 与网络）
              </el-link>
            </div>
          </div>
        </div>

        <div v-if="detail.resolutionNote" class="detail-section">
          <div class="section-title">流转备注</div>
          <div>{{ detail.resolutionNote }}</div>
        </div>

        <div class="detail-section">
          <div class="section-title">关联用例（{{ detail.cases.length }}）</div>
          <el-tag v-for="c in detail.cases" :key="c.testCaseId" size="small" class="case-tag" closable
            :disable-transitions="true" @close="handleUnlinkCase(c.testCaseId)">
            {{ c.testCaseName }}
          </el-tag>
          <span v-if="detail.cases.length === 0" class="muted">暂无</span>
        </div>

        <div class="detail-section">
          <div class="section-title">复现/认领记录（{{ detail.occurrences.length }}）</div>
          <div v-for="o in detail.occurrences" :key="o.id" class="source-line">
            <span>{{ formatDateTime(o.occurredAt) }}</span>
            <el-link v-if="o.executionId" type="primary" @click="goExecution(o.executionId)">步骤 {{ o.stepOrder < 0
              ? '前置' : o.stepOrder + 1 }}</el-link>
          </div>
          <span v-if="detail.occurrences.length === 0" class="muted">暂无</span>
        </div>

        <template v-if="authStore.can(Permission.ManageTestCases)">
          <div class="detail-section">
            <div class="section-title">状态流转</div>
            <div class="action-bar">
              <el-select v-model="assigneeId" filterable clearable placeholder="修复负责人" style="width: 180px"
                size="small">
                <el-option v-for="u in userOptions" :key="u.id" :label="u.name" :value="u.id" />
              </el-select>
              <el-button size="small" @click="doTransition('assign', assigneeId || undefined)">指派</el-button>
              <el-button size="small" type="warning" plain @click="promptNote('fix', '修复说明')">标记修复</el-button>
              <el-button size="small" type="success" plain
                :disabled="detail.status !== DefectStatus.Fixed && detail.status !== DefectStatus.Verified"
                @click="doTransition('verify')">验证通过</el-button>
              <el-button size="small" type="success"
                :disabled="detail.status !== DefectStatus.Verified && detail.status !== DefectStatus.Fixed"
                @click="doTransition('close')">关闭</el-button>
              <el-button size="small" type="danger" plain @click="promptNote('reject', '驳回原因')">驳回</el-button>
              <el-button size="small" plain @click="promptNote('defer', '挂起原因')">挂起</el-button>
              <el-button v-if="detail.status === DefectStatus.Rejected || detail.status === DefectStatus.Deferred
                || detail.status === DefectStatus.Closed" size="small" type="primary" plain
                @click="doTransition('reopen')">重新打开</el-button>
            </div>
          </div>
        </template>
      </template>
    </el-drawer>

    <!-- 执行回放：录像在受权端点上（需要 JWT），不能直接给 <video src>，
         得先取成 blob 再喂给播放器；关闭时释放 objectURL，否则整段录像一直占着内存 -->
    <el-dialog v-model="replayVisible" title="执行回放" width="80%" top="6vh" append-to-body
      @closed="releaseReplay">
      <video v-if="replayUrl" :src="replayUrl" controls autoplay class="replay-video" />
      <div v-else class="muted">录像加载中…</div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { getProjects } from '@/api/project'
import { listUserOptionsApi } from '@/api/auth'
import {
  batchDeleteDefects, createDefect, deleteDefect, getDefect, getDefectStats, getDefects,
  listExternalProviders, pushDefectExternal, transitionDefect, unlinkDefectCase, updateDefect,
} from '@/api/defect'
import { getTestCases } from '@/api/testcase'
import { downloadExecutionTrace, downloadExecutionVideo } from '@/api/execution'
import { saveBlobAsFile } from '@/api/report'
import { useAuthStore } from '@/stores/auth'
import { Permission } from '@/constants/permissions'
import { formatDateTime } from '@/utils/formatter'
import type { Project } from '@/types/project'
import type { UserOption } from '@/types/auth'
import type { TestCaseSummary } from '@/types/testcase'
import {
  DEFECT_SEVERITY_LABELS, DEFECT_STATUS_LABELS, DefectStatus,
  buildExternalUrl,
  type DefectDetail, type DefectListItem, type DefectStats, type ExternalDefectProvider,
} from '@/types/defect'

import MobileCardList from '@/components/common/MobileCardList.vue'
import RichTextEditor from '@/components/common/RichTextEditor.vue'
import RichTextViewer from '@/components/common/RichTextViewer.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { usePagedList } from '@/composables/usePagedList'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useRowSelection } from '@/composables/useRowSelection'

const router = useRouter()

/** 窄屏（< 1024px）：表格换成卡片形态，见下方模板 */
const { isMobile } = useBreakpoint()
const authStore = useAuthStore()

const projects = ref<Project[]>([])
const userOptions = ref<UserOption[]>([])
const stats = ref<DefectStats | null>(null)

const filters = ref<{ projectId?: string; status?: number; severity?: number; search?: string }>({})

// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<DefectListItem>((p, ps) => getDefects({
  projectId: filters.value.projectId,
  status: filters.value.status,
  severity: filters.value.severity,
  search: filters.value.search,
  page: p,
  pageSize: ps,
}), { pageSize: 20 })
const { items, total, page, pageSize, loading } = toRefs(list)
const load = () => list.load()
const reload = () => list.load(1)

// 批量删除（关联用例/复现流水由数据库级联清理，与单删一致）
const { deleting, selectedRows, onSelectionChange, handleBatchDelete } =
  useBatchDelete<DefectListItem>({
    entity: '缺陷',
    remove: batchDeleteDefects,
    reload: load,
  })

// 点击行直接勾选/取消勾选（复选框列与操作列除外）
const { tableRef, handleRowSelectionClick } = useRowSelection()

const loadStats = async () => {
  stats.value = await getDefectStats(filters.value.projectId)
}

const loadProjects = async () => {
  // 下拉只需要选项，拉一页足量数据
  const result = await getProjects({ page: 1, pageSize: 100 })
  projects.value = result.items
}

const loadUsers = async () => {
  if (!authStore.can(Permission.ManageTestCases)) return
  try {
    userOptions.value = await listUserOptionsApi()
  } catch {
    userOptions.value = []
  }
}

// ------------------------------ 新建

const createVisible = ref(false)
const saving = ref(false)
/** 非空 = 编辑模式。复用新建弹窗，少一套几乎相同、却要多维护一遍的表单 */
const editingId = ref<string | null>(null)
const createForm = ref<{
  projectId?: string
  title: string
  description: string
  severity: number
  assignedToId?: string
  externalRef?: string
  /** 关联用例 id（可空 = 不关联） */
  testCaseIds: string[]
}>({ title: '', description: '', severity: 1, testCaseIds: [] })

// ------------------------------ 关联用例下拉（按项目过滤 + 服务端搜索）

/** 下拉选项只需要这三个字段。用 Pick 而不是自定义 interface，
 *  这样后端改了字段名会在这里编译报错，而不是悄悄显示空标签 */
type CaseOption = Pick<TestCaseSummary, 'id' | 'name' | 'module'>

/** 搜索结果。**不含"已选中但当前搜不到"的项**——那些由 selectedCases 兜底 */
const caseOptions = ref<CaseOption[]>([])
const caseLoading = ref(false)
let caseSearchTimer: ReturnType<typeof setTimeout> | undefined

/**
 * 已选中用例的 id → 用例，用来**稳定显示标签**。
 *
 * 为什么不能只靠搜索结果：搜索会把 caseOptions 换成新的结果集，
 * 上一批选中的项不在里面时 el-select 找不到 label，就会**直接把 GUID 显示出来**。
 */
const selectedCases = ref(new Map<string, CaseOption>())

/** 实际渲染的选项 = 已选中的（置顶）+ 本次搜索命中且未被选中的（去重，否则会看到重复条目） */
const caseSelectOptions = computed<CaseOption[]>(() => {
  const picked = createForm.value.testCaseIds
    .map((id) => selectedCases.value.get(id))
    .filter((c): c is CaseOption => Boolean(c))
  const pickedIds = new Set(picked.map((c) => c.id))
  return [...picked, ...caseOptions.value.filter((c) => !pickedIds.has(c.id))]
})

/** 服务端搜索。el-select 不做防抖，这里压 300ms */
function onCaseSearch(query: string) {
  clearTimeout(caseSearchTimer)
  caseSearchTimer = setTimeout(() => void loadCaseOptions(query), 300)
}

async function loadCaseOptions(query: string) {
  const projectId = createForm.value.projectId
  if (!projectId) {
    caseOptions.value = []
    return
  }
  caseLoading.value = true
  try {
    const res = await getTestCases({ projectId, search: query || undefined, page: 1, pageSize: 50 })
    caseOptions.value = res.items
  } catch {
    // 提示由 axios 拦截器统一弹；这里只保证下拉不残留上一批结果
    caseOptions.value = []
  } finally {
    caseLoading.value = false
  }
}

/**
 * 换项目后旧用例已不属于当前项目，必须清空已选。
 * 不清的话会提交一堆跨项目关联，后端直接 400（校验在服务端，前端过滤不算数）。
 */
function onCreateProjectChange() {
  createForm.value.testCaseIds = []
  selectedCases.value = new Map()
  caseOptions.value = []
  void loadCaseOptions('')
}

const openCreate = () => {
  editingId.value = null
  createForm.value = {
    projectId: createForm.value.projectId ?? projects.value[0]?.id,
    title: '',
    description: '',
    severity: 1,
    assignedToId: undefined,
    externalRef: undefined,
    testCaseIds: [],
  }
  selectedCases.value = new Map()
  caseOptions.value = []
  // 先拉一批（不带关键字）让用户能直接浏览，不必先打字才能看到选项
  void loadCaseOptions('')
  createVisible.value = true
}

/**
 * 编辑：列表 DTO 里**没有** description（列表不展示正文，没必要拖着一整段富文本走），
 * 所以必须再拉一次详情，才能把描述填进富文本编辑器。
 */
const openEdit = async (id: string) => {
  const d = await getDefect(id)
  editingId.value = id
  createForm.value = {
    projectId: d.projectId,
    title: d.title,
    description: d.description ?? '',
    severity: d.severity,
    assignedToId: d.assignedToId ?? undefined,
    externalRef: d.externalRef ?? undefined,
    testCaseIds: d.cases.map((c) => c.testCaseId),
  }
  // 详情里已经带了已关联用例的名称，先喂给 selectedCases：
  // 否则打开编辑框时还没搜索过，选项列表里没有它们，el-select 会把 GUID 当标签显示
  selectedCases.value = new Map(
    d.cases.map((c) => [c.testCaseId, { id: c.testCaseId, name: c.testCaseName, module: c.module }]),
  )
  caseOptions.value = []
  void loadCaseOptions('')
  createVisible.value = true
}

const submitCreate = async () => {
  // 编辑模式不校验项目：项目不可改，且更新接口根本不接收 projectId
  if (!editingId.value && !createForm.value.projectId) {
    ElMessage.warning('请选择项目')
    return
  }
  if (!createForm.value.title.trim()) {
    ElMessage.warning('请填写标题')
    return
  }
  saving.value = true
  try {
    const payload = {
      title: createForm.value.title,
      description: createForm.value.description || null,
      severity: createForm.value.severity,
      assignedToId: createForm.value.assignedToId || null,
      externalRef: createForm.value.externalRef || null,
      // 编辑时传的是**目标全集**（后端做差集）；空数组表示解除全部关联
      testCaseIds: createForm.value.testCaseIds,
    }
    if (editingId.value) {
      const updated = await updateDefect(editingId.value, payload)
      ElMessage.success('缺陷已保存')
      createVisible.value = false
      // 详情抽屉可能正开着这一条，顺手刷新，别让人看到改之前的旧内容
      if (detail.value?.id === updated.id) detail.value = updated
    } else {
      await createDefect({ ...payload, projectId: createForm.value.projectId! })
      ElMessage.success('缺陷已创建')
      createVisible.value = false
    }
    reload()
    void loadStats()
  } finally {
    saving.value = false
  }
}

const handleDelete = async (id: string) => {
  try {
    await deleteDefect(id)
  } catch {
    // 失败提示已由 axios 拦截器统一弹出，这里只需终止后续的成功流程
    return
  }
  ElMessage.success('缺陷已删除')
  if (detail.value?.id === id) detailVisible.value = false
  reload()
  void loadStats()
}

// ------------------------------ 详情与流转

const detailVisible = ref(false)
const detail = ref<DefectDetail | null>(null)
const assigneeId = ref<string | undefined>(undefined)

const openDetail = async (id: string) => {
  detail.value = await getDefect(id)
  assigneeId.value = detail.value.assignedToId ?? undefined
  detailVisible.value = true
}

const refreshDetail = async (updated: DefectDetail) => {
  detail.value = updated
  reload()
  void loadStats()
}

const doTransition = async (action: string, assignedToId?: string, note?: string) => {
  if (!detail.value) return
  const updated = await transitionDefect(detail.value.id, {
    action,
    assignedToId: assignedToId ?? null,
    note: note ?? null,
  })
  ElMessage.success('状态已更新')
  void refreshDetail(updated)
}

const promptNote = (action: string, placeholder: string) => {
  void ElMessageBox.prompt(placeholder, placeholder, {
    inputPlaceholder: placeholder + '（必填）',
    confirmButtonText: '确定',
    cancelButtonText: '取消',
  }).then(({ value }) => {
    if (!value || !value.trim()) {
      ElMessage.warning(placeholder + '不能为空')
      return
    }
    void doTransition(action, undefined, value.trim())
  }).catch(() => { })
}

const handleUnlinkCase = async (testCaseId: string) => {
  if (!detail.value) return
  await unlinkDefectCase(detail.value.id, testCaseId)
  ElMessage.success('已取消关联')
  detail.value = await getDefect(detail.value.id)
}

const goExecution = (executionId: string) => {
  detailVisible.value = false
  void router.push(`/executions/${executionId}`)
}

// ------------------------------ 描述里的证据（截图 / 执行回放）

/**
 * 一键转缺陷时后端会把「【截图】路径」「【执行回放】路径」写进描述（见 DefectService.CreateAsync
 * 的证据拼装，每段一个 <p>）。原始路径对用户没有意义，这里把这两行抽出来换成缩略图与播放入口，
 * 正文只留错误信息、AI 诊断这些真正要读的文本。
 *
 * 历史数据里也是同样的两行（格式由后端生成，不会漂），所以新旧缺陷走同一套抽取。
 */
const extractEvidence = (html: string) => {
  const screenshot = html.match(/<p>\s*【截图】\s*([^<]*?)\s*<\/p>/)?.[1]
  const trace = html.match(/<p>\s*【执行回放】\s*([^<]*?)\s*<\/p>/)?.[1]
  const body = html.replace(/<p>\s*【(截图|执行回放)】\s*[^<]*?\s*<\/p>/g, '').trim()
  return { body, screenshot, trace }
}

const descriptionParts = computed(() => extractEvidence(detail.value?.description ?? ''))

const replayVisible = ref(false)
const replayUrl = ref<string | null>(null)
const loadingReplay = ref(false)

/** 录像走受权端点，先取成 blob 再交给 <video>；没有录像（仅失败执行保留）时给出可执行的下一步 */
const openReplay = async () => {
  const executionId = detail.value?.foundInExecutionId
  if (!executionId) return
  replayVisible.value = true
  if (replayUrl.value) return
  loadingReplay.value = true
  try {
    const blob = await downloadExecutionVideo(executionId)
    replayUrl.value = URL.createObjectURL(blob)
  } catch {
    replayVisible.value = false
    ElMessage.warning('该执行没有可播放的录像（仅失败执行保留），可下载 trace 用 Playwright Trace Viewer 回放')
  } finally {
    loadingReplay.value = false
  }
}

/** objectURL 不释放会一直占着那几 MB 内存，直到页面关闭 */
const releaseReplay = () => {
  if (replayUrl.value) {
    URL.revokeObjectURL(replayUrl.value)
    replayUrl.value = null
  }
}

const downloadTrace = async () => {
  const executionId = detail.value?.foundInExecutionId
  if (!executionId) return
  const blob = await downloadExecutionTrace(executionId)
  saveBlobAsFile(blob, `trace-${executionId}.zip`)
}

/** 跳到用例详情（「来源用例」列/卡片） */
const goTestCase = (testCaseId: string) => {
  void router.push(`/testcases/${testCaseId}`)
}

// ------------------------------ 展示辅助

const severityTagType = (severity: number) =>
  severity === 3 ? 'danger' : severity === 2 ? 'warning' : 'info'

const statusTagType = (status: number) => {
  if (status === DefectStatus.Verified || status === DefectStatus.Closed) return 'success'
  if (status === DefectStatus.Rejected || status === DefectStatus.Deferred) return 'info'
  if (status === DefectStatus.Fixed) return 'warning'
  return 'danger'
}

const formatHours = (hours?: number | null) => {
  if (hours == null) return '—'
  if (hours < 1) return `${Math.round(hours * 60)} 分钟`
  return `${hours.toFixed(1)} 小时`
}

onMounted(() => {
  void loadProjects()
  void loadUsers()
  reload()
  void loadStats()
  void loadExternalProviders()
})

// ------------------------------ 外部缺陷系统（配置开关驱动，未启用时数组为空、区块不渲染）
const externalProviders = ref<ExternalDefectProvider[]>([])
const pushingProvider = ref('')

async function loadExternalProviders() {
  try {
    externalProviders.value = await listExternalProviders()
  } catch {
    externalProviders.value = []
  }
}

async function handlePushExternal(p: ExternalDefectProvider) {
  if (!detail.value) return
  pushingProvider.value = p.id
  try {
    const res = await pushDefectExternal(detail.value.id, p.id)
    ElMessage.success(`已推送到${p.name}：${res.externalRef}`)
    // 重新拉详情，让抽屉里的 ExternalRef 与链接即时生效
    detail.value = await getDefect(detail.value.id)
  } finally {
    pushingProvider.value = ''
  }
}
</script>

<style scoped>
/* 下拉里靠右弱化显示模块名，避免和用例名抢注意力
   （与 TestPlanListView 里的同名规则一致——项目里这类小工具类是各文件 scoped 定义） */
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

/* 整页纵向布局：统计卡固定高，列表卡片吃掉剩余高度（56=顶栏，32=main 上下内边距） */
.defect-page {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 56px - 32px);
  overflow: hidden;
}

.stats-row {
  margin-bottom: 12px;
  flex-shrink: 0;
}

/* 列表卡片撑满剩余空间，内部再分：过滤条 / 表格（自适应） / 分页 */
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

.stat-card {
  /* 第一张卡带严重度细分行更高，其余卡撑满列高保持整行等高 */
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

.stat-value.success {
  color: #67c23a;
}

.stat-sub {
  font-size: 12px;
  color: #909399;
  margin-top: 2px;
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

.detail-section {
  margin-top: 16px;
}

.section-title {
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 6px;
}

.source-line {
  display: flex;
  gap: 10px;
  align-items: center;
  font-size: 13px;
  margin-bottom: 4px;
}

/* 证据区：缩略图在左，播放/下载入口在右 */
.evidence-row {
  display: flex;
  gap: 12px;
  align-items: center;
  margin-top: 8px;
}

.evidence-thumb {
  width: 160px;
  height: 90px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
  cursor: zoom-in;
  flex-shrink: 0;
}

.evidence-actions {
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: flex-start;
}

.replay-video {
  width: 100%;
  max-height: 70vh;
  border-radius: 4px;
  background: #000;
}

.case-tag {
  margin-right: 6px;
  margin-bottom: 4px;
}

.muted {
  color: #909399;
  font-size: 12px;
}

.action-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}
</style>
