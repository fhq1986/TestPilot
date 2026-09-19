<template>
  <div class="audit-log">
    <el-card class="list-card">
      <div class="toolbar">
        <div class="toolbar-left">
          <el-input v-model="filters.username" placeholder="用户名" clearable class="w-160"
            @keyup.enter="load(1)" @clear="load(1)" />
          <el-select v-model="filters.action" placeholder="全部动作" clearable class="w-150">
            <el-option v-for="a in facets.actions" :key="a" :label="actionLabel(a)" :value="a" />
          </el-select>
          <el-select v-model="filters.resourceType" placeholder="全部资源" clearable class="w-150">
            <el-option v-for="r in facets.resourceTypes" :key="r" :label="resourceLabel(r)" :value="r" />
          </el-select>
          <el-select v-model="filters.result" placeholder="全部结果" clearable class="w-120">
            <el-option label="成功" :value="'true'" />
            <el-option label="失败" :value="'false'" />
          </el-select>
          <el-date-picker v-model="range" type="datetimerange" value-format="YYYY-MM-DDTHH:mm:ss"
            start-placeholder="开始" end-placeholder="结束" class="w-360" unlink-panels />
        </div>
        <div class="toolbar-right">
          <el-button type="primary" :icon="Search" @click="load(1)">查询</el-button>
          <el-button :icon="RefreshLeft" @click="resetFilters">重置</el-button>
          <el-button :icon="Download" :loading="exporting" @click="handleExport">
            导出 Excel
          </el-button>
          <el-button :icon="Refresh" @click="load()">刷新</el-button>
        </div>
      </div>

      <div class="table-wrap">
        <el-table v-loading="loading" :data="logs" row-key="id" height="100%" size="small"
          @row-click="openDetail">
          <el-table-column label="时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作人" width="130">
            <template #default="{ row }">
              <div class="actor">
                <span>{{ row.username || '-' }}</span>
                <span v-if="row.userRole" class="actor-role">{{ roleLabel(row.userRole) }}</span>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="动作" width="130">
            <template #default="{ row }">
              <el-tag :type="actionTagType(row.action)" size="small" effect="plain">
                {{ actionLabel(row.action) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="资源" min-width="170">
            <template #default="{ row }">
              <span class="res-type">{{ resourceLabel(row.resourceType) }}</span>
              <span v-if="row.resourceName" class="res-name">{{ row.resourceName }}</span>
            </template>
          </el-table-column>
          <el-table-column label="请求" min-width="240">
            <template #default="{ row }">
              <span class="method" :class="`method-${row.method.toLowerCase()}`">{{ row.method }}</span>
              <span class="path">{{ row.path }}</span>
            </template>
          </el-table-column>
          <el-table-column label="结果" width="120">
            <template #default="{ row }">
              <el-tag :type="row.succeeded ? 'success' : 'danger'" size="small" effect="light">
                {{ row.statusCode }}
              </el-tag>
              <span class="duration">{{ row.durationMs }}ms</span>
            </template>
          </el-table-column>
          <el-table-column label="IP" width="130">
            <template #default="{ row }">
              <span class="mono">{{ row.ipAddress || '-' }}</span>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize"
        :total="total" :page-sizes="[20, 50, 100, 200]" layout="total, sizes, prev, pager, next"
        @current-change="load()" @size-change="load(1)" />
    </el-card>

    <el-drawer v-model="detailVisible" title="审计详情" size="70%">
      <el-descriptions v-if="current" :column="1" border>
        <el-descriptions-item label="时间">{{ formatDateTime(current.createdAt) }}</el-descriptions-item>
        <el-descriptions-item label="操作人">
          {{ current.username || '-' }}
          <span v-if="current.userRole" class="actor-role">{{ roleLabel(current.userRole) }}</span>
        </el-descriptions-item>
        <el-descriptions-item label="动作">
          {{ actionLabel(current.action) }}（{{ current.action }}）
        </el-descriptions-item>
        <el-descriptions-item label="资源">
          {{ resourceLabel(current.resourceType) }}（{{ current.resourceType }}）
          <span v-if="current.resourceName">· {{ current.resourceName }}</span>
        </el-descriptions-item>
        <el-descriptions-item label="资源 ID">
          <span class="mono">{{ current.resourceId || '-' }}</span>
        </el-descriptions-item>
        <el-descriptions-item label="请求">
          <span class="method" :class="`method-${current.method.toLowerCase()}`">{{ current.method }}</span>
          {{ current.path }}
        </el-descriptions-item>
        <el-descriptions-item label="状态码">
          <el-tag :type="current.succeeded ? 'success' : 'danger'" size="small">
            {{ current.statusCode }}
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="耗时">{{ current.durationMs }} ms</el-descriptions-item>
        <el-descriptions-item label="来源 IP">
          <span class="mono">{{ current.ipAddress || '-' }}</span>
        </el-descriptions-item>
      </el-descriptions>

      <div class="detail-section">
        <div class="detail-title">
          请求内容
          <span class="detail-hint">密码、令牌等敏感字段已在入库前脱敏为 ***</span>
        </div>
        <pre class="detail-json">{{ prettyDetail }}</pre>
      </div>

      <!-- 与「请求内容」相对：请求是"传进去什么"，这里是"返回了什么"。
           排查"用户说保存失败、日志却显示 200"这类问题，只有请求内容是不够的 -->
      <div class="detail-section">
        <div class="detail-title">
          响应结果
          <span class="detail-hint">文件下载、批量导出等非 JSON 响应不采集；超长会截断</span>
        </div>
        <pre class="detail-json">{{ prettyResponse }}</pre>
      </div>
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, toRefs } from 'vue'
import { Download, Refresh, RefreshLeft, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { auditFacetsApi, exportAuditLogsApi, listAuditLogsApi } from '@/api/auth'
import { formatDateTime } from '@/utils/formatter'
import type { AuditLogView } from '@/types/auth'
import { usePagedList } from '@/composables/usePagedList'

// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const range = ref<[string, string] | null>(null)
const list = usePagedList<AuditLogView>((p, ps) => listAuditLogsApi({
  username: filters.username || undefined,
  action: filters.action || undefined,
  resourceType: filters.resourceType || undefined,
  succeeded: filters.result === '' ? undefined : filters.result === 'true',
  from: range.value?.[0],
  to: range.value?.[1],
  page: p,
  pageSize: ps,
}), { pageSize: 50 })
const load = (targetPage?: number) => list.load(targetPage)
const { items: logs, total, page, pageSize, loading } = toRefs(list)

const facets = reactive({ actions: [] as string[], resourceTypes: [] as string[], usernames: [] as string[] })

const filters = reactive({
  username: '',
  action: '',
  resourceType: '',
  result: '' as '' | 'true' | 'false',
})

const detailVisible = ref(false)
const current = ref<AuditLogView | null>(null)

/** 动作 → 中文（后端写入的是稳定英文标识，展示层做映射） */
const ACTION_LABELS: Record<string, string> = {
  Create: '新建',
  Update: '更新',
  Delete: '删除',
  BatchDelete: '批量删除',
  Run: '执行',
  Stop: '停止',
  Import: '导入',
  Export: '导出',
  ResetPassword: '重置密码',
  ChangePassword: '修改密码',
  UpdateCamera: '更新',
  Save: '保存',
  Trigger: '手动触发',
  Toggle: '启停',
  Adopt: '采纳',
  Ignore: '忽略',
  Apply: '应用',
  Generate: '生成',
  TestSend: '测试发送',
}

const RESOURCE_LABELS: Record<string, string> = {
  Project: '项目',
  TestCase: '测试用例',
  Execution: '执行',
  DataSet: '数据集',
  Schedule: '定时任务',
  Suite: '测试套件',
  Baseline: '视觉基线',
  User: '用户',
  Setting: '系统设置',
  Mock: 'Mock',
  Environment: '环境',
  SharedStep: '共享步骤',
  Recorder: '录制会话',
}

const ROLE_LABELS: Record<string, string> = {
  Admin: '管理员',
  Tester: '测试工程师',
  Viewer: '只读访客',
}

const actionLabel = (a: string) => ACTION_LABELS[a] ?? a
const resourceLabel = (r: string) => RESOURCE_LABELS[r] ?? r
const roleLabel = (r: string) => ROLE_LABELS[r] ?? r

const actionTagType = (action: string) => {
  if (action === 'Create') return 'success'
  if (action === 'Delete' || action === 'BatchDelete') return 'danger'
  if (action === 'Update' || action === 'ResetPassword') return 'warning'
  if (action === 'Run' || action === 'Trigger') return 'primary'
  return 'info'
}

/** 详情 JSON 美化；解析失败就原样显示（脱敏后的非 JSON 文本、或截断过的内容） */
function prettyJson(raw: string | null | undefined, emptyText: string) {
  if (!raw) return emptyText
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
}

const prettyDetail = computed(() => prettyJson(current.value?.detail, '（无请求内容）'))
const prettyResponse = computed(() => prettyJson(current.value?.responseBody, '（无响应内容）'))

function openDetail(row: AuditLogView) {
  current.value = row
  detailVisible.value = true
}

function resetFilters() {
  filters.username = ''
  filters.action = ''
  filters.resourceType = ''
  filters.result = ''
  range.value = null
  load(1)
}

const exporting = ref(false)

/** 导出遵循当前筛选条件——审计表可能很大，全量导出既慢也没有使用场景 */
async function handleExport() {
  exporting.value = true
  try {
    await exportAuditLogsApi({
      username: filters.username || undefined,
      action: filters.action || undefined,
      resourceType: filters.resourceType || undefined,
      succeeded: filters.result === '' ? undefined : filters.result === 'true',
      from: range.value?.[0],
      to: range.value?.[1],
    })
    ElMessage.success('已开始下载导出文件')
  } finally {
    exporting.value = false
  }
}

async function loadFacets() {
  const res = await auditFacetsApi()
  facets.actions = res.actions
  facets.resourceTypes = res.resourceTypes
  facets.usernames = res.usernames
}

onMounted(() => {
  load()
  loadFacets()
})
</script>

<style scoped>
.audit-log {
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
  align-items: center;
  flex-wrap: wrap;
}

.w-120 { width: 120px; }
.w-150 { width: 150px; }
.w-160 { width: 160px; }
.w-360 { width: 360px; }

.table-wrap {
  flex: 1;
  min-height: 0;
}

.actor {
  display: flex;
  flex-direction: column;
  line-height: 1.4;
}

.actor-role {
  color: #9aa2ae;
  font-size: 12px;
}

.res-type {
  color: var(--el-text-color-primary);
}

.res-name {
  margin-left: 8px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.method {
  display: inline-block;
  min-width: 46px;
  margin-right: 8px;
  font-weight: 600;
  font-size: 12px;
}

.method-get { color: #2563eb; }
.method-post { color: #16a34a; }
.method-put { color: #d97706; }
.method-delete { color: #dc2626; }

.path {
  color: var(--el-text-color-secondary);
  font-size: 13px;
  word-break: break-all;
}

.duration {
  margin-left: 8px;
  color: #9aa2ae;
  font-size: 12px;
}

.mono {
  font-family: "Consolas", "Menlo", monospace;
  font-size: 13px;
}

.detail-section {
  margin-top: 20px;
}

.detail-title {
  font-weight: 600;
  margin-bottom: 8px;
}

.detail-hint {
  margin-left: 8px;
  font-weight: 400;
  color: #9aa2ae;
  font-size: 12px;
}

.detail-json {
  background: #f6f8fa;
  border: 1px solid #e6e9ee;
  border-radius: 4px;
  padding: 12px;
  max-height: 420px;
  overflow: auto;
  font-family: "Consolas", "Menlo", monospace;
  font-size: 12.5px;
  line-height: 1.6;
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
}
</style>
