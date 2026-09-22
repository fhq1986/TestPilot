<template>
  <div class="loadtest-page">
    <el-card class="list-card">
      <div class="toolbar">
        <!-- 项目默认为空 = 看全部项目的场景；clearable 后 @change 会传 null，
             所以统一走箭头函数回到第一页，不能直接绑 load（load 的首参是页码） -->
        <el-select v-model="projectId" placeholder="选择项目" clearable filterable class="project-select"
          @change="() => load(1)">
          <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-input v-model="keyword" placeholder="搜索场景名称" clearable class="keyword-input" :prefix-icon="Search"
          @keyup.enter="load(1)" @clear="load(1)" />
        <el-select v-model="source" placeholder="来源" clearable class="source-select" @change="() => load(1)">
          <el-option v-for="(label, value) in LOAD_TEST_SOURCE_LABELS" :key="value" :label="label"
            :value="Number(value)" />
        </el-select>
        <el-button type="primary" :icon="Search" @click="load(1)">查询</el-button>
        <el-button type="primary" :icon="Plus" plain @click="openCreate">新建</el-button>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" @click="load()">刷新</el-button>
      </div>

      <div v-if="!isMobile" class="table-wrap">
        <el-table v-loading="loading" :data="scenarios" row-key="id" height="100%">
          <el-table-column label="名称" min-width="200" show-overflow-tooltip fixed="left">
            <template #default="{ row }">
              <el-link type="primary" :underline="false" @click="openDetail(row)">{{ row.name }}</el-link>
            </template>
          </el-table-column>
          <!-- 跨项目看场景时靠它区分归属 -->
          <el-table-column label="所属项目" min-width="140" show-overflow-tooltip>
            <template #default="{ row }">
              <el-button v-if="row.projectName" link type="primary" @click="router.push(`/projects/${row.projectId}`)">
                {{ row.projectName }}
              </el-button>
              <span v-else class="muted">—</span>
            </template>
          </el-table-column>
          <el-table-column label="来源" width="110">
            <template #default="{ row }">
              <el-tag :type="loadTestSourceTagType(row.source)" size="small">
                {{ LOAD_TEST_SOURCE_LABELS[row.source] ?? '未知' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="用例数" width="90">
            <template #default="{ row }">
              <el-tag size="small" type="info">{{ row.caseCount }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="负载" width="150">
            <template #default="{ row }">{{ formatLoadText(row.virtualUsers, row.durationSeconds) }}</template>
          </el-table-column>
          <!-- 最近运行：状态 + 时间一起看，才知道"这个数字是什么时候的" -->
          <el-table-column label="最近运行" width="200">
            <template #default="{ row }">
              <template v-if="row.lastRunStatus !== null && row.lastRunStatus !== undefined">
                <el-tag :type="executionStatusTagType(row.lastRunStatus)" size="small">
                  {{ EXECUTION_STATUS_LABELS[row.lastRunStatus] }}
                </el-tag>
                <div class="sub-text">{{ formatFullDateTime(row.lastRunAt) }}</div>
              </template>
              <span v-else class="muted">未运行</span>
            </template>
          </el-table-column>
          <el-table-column label="最近 p95" width="110">
            <template #default="{ row }">
              <span v-if="row.lastP95Ms !== null && row.lastP95Ms !== undefined">{{ Math.round(row.lastP95Ms) }}ms</span>
              <span v-else class="muted">—</span>
            </template>
          </el-table-column>
          <el-table-column label="最近错误率" width="110">
            <template #default="{ row }">
              <span v-if="row.lastErrorRate !== null && row.lastErrorRate !== undefined">
                {{ formatRate(row.lastErrorRate) }}
              </span>
              <span v-else class="muted">—</span>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="180" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click="openDetail(row)">详情</el-button>
              <el-button link type="primary" :loading="runningId === row.id" @click="handleRun(row)">运行</el-button>
              <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- 窄屏：表格换成卡片（列宽合计约 1190px，是 375px 视口的 3 倍多） -->
      <MobileCardList v-else v-loading="loading" :items="scenarios" :row-key="(row) => row.id" empty-text="暂无压测场景">
        <template #title="{ item }">
          <el-link type="primary" :underline="false" @click="openDetail(item)">{{ item.name }}</el-link>
        </template>

        <template #badge="{ item }">
          <el-tag :type="loadTestSourceTagType(item.source)" size="small">
            {{ LOAD_TEST_SOURCE_LABELS[item.source] ?? '未知' }}
          </el-tag>
          <el-tag size="small" type="info">{{ item.caseCount }} 条用例</el-tag>
        </template>

        <template #meta="{ item }">
          <span><span class="mcl-label">项目</span>{{ item.projectName || '—' }}</span>
          <span><span class="mcl-label">负载</span>{{ formatLoadText(item.virtualUsers, item.durationSeconds) }}</span>
          <span>
            <span class="mcl-label">最近运行</span>
            <template v-if="item.lastRunStatus !== null && item.lastRunStatus !== undefined">
              {{ EXECUTION_STATUS_LABELS[item.lastRunStatus] }} · {{ formatFullDateTime(item.lastRunAt) }}
            </template>
            <template v-else>未运行</template>
          </span>
          <span>
            <span class="mcl-label">最近 p95</span>
            {{ item.lastP95Ms !== null && item.lastP95Ms !== undefined ? `${Math.round(item.lastP95Ms)}ms` : '—' }}
          </span>
          <span>
            <span class="mcl-label">最近错误率</span>
            {{ item.lastErrorRate !== null && item.lastErrorRate !== undefined ? formatRate(item.lastErrorRate) : '—' }}
          </span>
        </template>

        <template #actions="{ item }">
          <el-button link type="primary" @click="openDetail(item)">详情</el-button>
          <el-button link type="primary" :loading="runningId === item.id" @click="handleRun(item)">运行</el-button>
          <el-button link type="danger" @click="handleDelete(item)">删除</el-button>
        </template>
      </MobileCardList>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize" :total="total"
        :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" @current-change="load()"
        @size-change="load(1)" />
    </el-card>

    <!-- 新建：只收集创建所需的最少信息，细节（用例/负载/阈值）留到详情页配置 -->
    <el-dialog v-model="dialogVisible" title="新建压测场景" width="620px" destroy-on-close>
      <el-form ref="formRef" :model="form" :rules="rules" label-width="100px">
        <el-form-item label="名称" prop="name">
          <el-input v-model="form.name" placeholder="如：登录接口 100 并发压测" maxlength="200" />
        </el-form-item>
        <el-form-item label="所属项目" prop="projectId">
          <el-select v-model="form.projectId" placeholder="选择项目" filterable class="full-width"
            @change="onProjectChange">
            <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="来源" prop="source">
          <el-radio-group v-model="form.source">
            <el-radio-button :value="LoadTestSource.Cases">接口用例</el-radio-button>
            <el-radio-button :value="LoadTestSource.OpenApi">导入 OpenAPI</el-radio-button>
          </el-radio-group>
          <div class="form-hint">
            {{ form.source === LoadTestSource.Cases
              ? '从项目内已有的接口用例拼装压测脚本'
              : '粘贴 OpenAPI 规范，按接口生成压测脚本' }}
          </div>
        </el-form-item>
        <el-form-item label="目标地址">
          <el-input v-model="form.targetBaseUrl" placeholder="https://api.example.com（可留空，用环境地址）" />
        </el-form-item>
        <el-form-item label="执行环境">
          <el-select v-model="form.environmentId" placeholder="未指定" clearable class="full-width"
            :disabled="!form.projectId">
            <el-option v-for="e in environmentOptions" :key="e.id" :label="e.name" :value="e.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="说明">
          <el-input v-model="form.description" type="textarea" :rows="2" placeholder="场景用途（可选）" maxlength="1000" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleCreate">创建并配置</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { getProjects } from '@/api/project'
import { getEnvironments } from '@/api/environment'
import { createLoadTest, deleteLoadTest, getLoadTests, runLoadTest } from '@/api/loadtest'
import { formatFullDateTime } from '@/utils/formatter'
import { EXECUTION_STATUS_LABELS, executionStatusTagType } from '@/types/execution'
import {
  formatLoadText, formatRate, loadTestSourceTagType, LoadTestSource, LOAD_TEST_SOURCE_LABELS,
  type LoadTestScenarioSummary,
} from '@/types/loadtest'
import type { Project } from '@/types/project'
import type { EnvironmentView } from '@/types/environment'
import MobileCardList from '@/components/common/MobileCardList.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { usePagedList } from '@/composables/usePagedList'

const router = useRouter()
/** 窄屏（< 1024px）：表格换成卡片形态，见下方模板 */
const { isMobile } = useBreakpoint()

const projectOptions = ref<Project[]>([])
const environmentOptions = ref<EnvironmentView[]>([])
const projectId = ref('')
const keyword = ref('')
const source = ref<number | undefined>(undefined)
const runningId = ref('')

// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<LoadTestScenarioSummary>((p, ps) => getLoadTests({
  projectId: projectId.value || undefined,
  keyword: keyword.value || undefined,
  source: source.value,
  page: p,
  pageSize: ps,
}))
const load = (targetPage?: number) => list.load(targetPage)
const { items: scenarios, total, page, pageSize, loading } = toRefs(list)

// ------------------------------------------------------------ 新建

const dialogVisible = ref(false)
const saving = ref(false)
const formRef = ref<FormInstance>()
const form = reactive({
  projectId: '',
  name: '',
  description: '',
  source: LoadTestSource.Cases as number,
  targetBaseUrl: '',
  environmentId: '',
})

const rules: FormRules = {
  name: [{ required: true, message: '请输入场景名称', trigger: 'blur' }],
  projectId: [{ required: true, message: '请选择所属项目', trigger: 'change' }],
}

const openCreate = async () => {
  form.projectId = projectId.value || projectOptions.value[0]?.id || ''
  form.name = ''
  form.description = ''
  form.source = LoadTestSource.Cases
  form.targetBaseUrl = ''
  form.environmentId = ''
  environmentOptions.value = []
  dialogVisible.value = true
  if (form.projectId) environmentOptions.value = await getEnvironments(form.projectId)
}

/** 换项目要清掉环境（环境是项目级的，留着旧项目的会串项目） */
const onProjectChange = async () => {
  form.environmentId = ''
  environmentOptions.value = form.projectId ? await getEnvironments(form.projectId) : []
}

const handleCreate = async () => {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return

  saving.value = true
  try {
    const detail = await createLoadTest({
      projectId: form.projectId,
      name: form.name.trim(),
      description: form.description.trim() || null,
      source: form.source,
      targetBaseUrl: form.targetBaseUrl.trim() || null,
      environmentId: form.environmentId || null,
    })
    dialogVisible.value = false
    ElMessage.success('已创建，请继续配置用例与负载')
    // 创建接口只收最少信息，用例/负载/阈值都在详情页配置，所以直接跳过去
    router.push(`/loadtests/${detail.id}`)
  } finally {
    saving.value = false
  }
}

// ------------------------------------------------------------ 行操作

const openDetail = (row: LoadTestScenarioSummary) => router.push(`/loadtests/${row.id}`)

const handleRun = async (row: LoadTestScenarioSummary) => {
  runningId.value = row.id
  try {
    const result = await runLoadTest(row.id)
    ElMessage.success('已开始运行')
    router.push(`/loadtests/runs/${result.runId}`)
  } catch {
    // 409（项目内已有运行中任务）/ 400（脚本未生成等）由请求拦截器统一提示服务端消息
  } finally {
    runningId.value = ''
  }
}

const handleDelete = async (row: LoadTestScenarioSummary) => {
  try {
    await ElMessageBox.confirm(`确认删除压测场景「${row.name}」？运行历史会一并删除。`, '删除压测场景',
      { type: 'warning' })
  } catch {
    return
  }
  await deleteLoadTest(row.id)
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
.loadtest-page {
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

.source-select {
  width: 140px;
}

.sub-text {
  margin-top: 2px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.muted {
  color: var(--el-text-color-placeholder);
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}

.full-width {
  width: 100%;
}

.form-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.6;
}
</style>
