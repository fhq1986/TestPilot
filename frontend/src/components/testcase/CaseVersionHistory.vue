<template>
  <el-drawer :model-value="visible" title="版本历史" size="80%" @update:model-value="emit('update:visible', $event)"
    @open="loadVersions">
    <el-alert type="warning" :closable="false" class="tip">
      <template #title>
        每次**保存且内容真的变了**才会记一版（只开不打字不会多出版本）。
        回滚本身也是一次变更，所以回滚错了还能再回滚回去。
      </template>
    </el-alert>

    <el-empty v-if="!loading && versions.length === 0" description="还没有历史版本——改动内容并保存后才会产生" :image-size="72" />

        <div v-if="!loading && versions.length > 0" class="version-toolbar">
      <el-button type="danger" size="small" :disabled="selectedVersions.length === 0"
        @click="handleBatchDelete">批量删除（{{ selectedVersions.length }}）</el-button>
    </div>
    <el-table v-if="!loading && versions.length > 0" v-loading="loading" :data="versions" size="small" @selection-change="onSelectionChange">
      <el-table-column type="selection" width="40" />
      <el-table-column label="版本" width="70">
        <template #default="{ row }">
          <el-tag size="small" effect="plain">v{{ row.version }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="时间" width="140">
        <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
      </el-table-column>
      <el-table-column label="操作人" width="90" show-overflow-tooltip>
        <template #default="{ row }">{{ row.operatorName || '—' }}</template>
      </el-table-column>
      <el-table-column label="步数" width="64" align="center" prop="stepCount" />
      <el-table-column label="改了什么" min-width="200" show-overflow-tooltip>
        <template #default="{ row }">{{ row.changeSummary || '—' }}</template>
      </el-table-column>
      <el-table-column label="操作" width="110" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openDetail(row.version)">查看</el-button>
          <el-button link type="danger" @click="handleRestore(row)">回滚</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 版本内容 + 与当前对比 -->
    <el-dialog v-model="detailVisible" :title="`第 ${detail?.version ?? ''} 版的内容`" width="780px" append-to-body>
      <div v-if="detail" v-loading="detailLoading">
        <el-descriptions :column="2" border size="small" class="detail-desc">
          <el-descriptions-item label="名称">{{ detail.snapshot.name }}</el-descriptions-item>
          <el-descriptions-item label="类型">{{ typeLabel(detail.snapshot.type) }}</el-descriptions-item>
          <el-descriptions-item label="模块">{{ detail.snapshot.module || '—' }}</el-descriptions-item>
          <el-descriptions-item label="优先级">{{ detail.snapshot.priority || '—' }}</el-descriptions-item>
          <el-descriptions-item label="超时">{{ detail.snapshot.timeout }} ms</el-descriptions-item>
          <el-descriptions-item label="重试">{{ detail.snapshot.retryCount }}</el-descriptions-item>
          <el-descriptions-item label="步骤数">{{ detail.snapshot.steps.length }}</el-descriptions-item>
          <el-descriptions-item label="记录时间">{{ formatDateTime(detail.createdAt) }}</el-descriptions-item>
        </el-descriptions>

        <!-- 差异清单：前端算（快照本身不大，多做一个 diff 接口只会多一套口径） -->
        <div class="diff-block">
          <div class="diff-title">与当前版本相比</div>
          <ul v-if="diffItems.length > 0" class="diff-list">
            <li v-for="(item, i) in diffItems" :key="i">{{ item }}</li>
          </ul>
          <div v-else class="diff-none">内容与当前版本一致</div>
        </div>

        <div class="diff-title">该版本的步骤</div>
        <el-table :data="detail.snapshot.steps" size="small" max-height="260">
          <el-table-column label="#" width="50">
            <template #default="{ row }">{{ row.stepOrder + 1 }}</template>
          </el-table-column>
          <el-table-column label="动作" width="130">
            <template #default="{ row }">{{ actionLabel(row.actionType) }}</template>
          </el-table-column>
          <el-table-column label="共享步骤组" width="130">
            <template #default="{ row }">{{ row.sharedGroupId ? '引用共享步骤' : '—' }}</template>
          </el-table-column>
          <el-table-column label="配置 / 说明" min-width="240" show-overflow-tooltip>
            <template #default="{ row }">{{ row.aiInstruction || row.aiElementDescription || row.config }}</template>
          </el-table-column>
        </el-table>
      </div>

      <template #footer>
        <el-button @click="detailVisible = false">关闭</el-button>
        <el-button type="danger" @click="detail && handleRestore(detail)">回滚到这一版</el-button>
      </template>
    </el-dialog>
  </el-drawer>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { formatDateTime } from '@/utils/formatter'
import {
  getTestCaseVersion,
  listTestCaseVersions,
  restoreTestCaseVersion,
  batchDeleteVersions
} from '@/api/testcase'
import type {
  TestCase,
  TestCaseSnapshot,
  TestCaseVersionDetail,
  TestCaseVersionSummary,
} from '@/types/testcase'

const props = defineProps<{
  visible: boolean
  testCaseId: string
  /** 当前版本的内容，用来算"与当前相比"的差异 */
  current: TestCase | null
}>()

// ------------------------------ 批量删除历史版本
const selectedVersions = ref<number[]>([])
const onSelectionChange = (rows: Array<{ version: number }>) => {
  selectedVersions.value = rows.map((r) => r.version)
}
const handleBatchDelete = async () => {
  await ElMessageBox.confirm(
    `确定删除选中的 ${selectedVersions.value.length} 个历史版本？删除后无法回滚到这些版本，当前用例内容不受影响。`,
    '批量删除历史版本', { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' })
  const r = await batchDeleteVersions(props.testCaseId, selectedVersions.value)
  ElMessage.success(`已删除 ${r.deleted} 个版本`)
  selectedVersions.value = []
  await loadVersions()
}

const emit = defineEmits<{
  (e: 'update:visible', value: boolean): void
  (e: 'restored'): void
}>()

const versions = ref<TestCaseVersionSummary[]>([])
const loading = ref(false)
const detail = ref<TestCaseVersionDetail | null>(null)
const detailVisible = ref(false)
const detailLoading = ref(false)

const loadVersions = async () => {
  loading.value = true
  try {
    versions.value = await listTestCaseVersions(props.testCaseId)
  } finally {
    loading.value = false
  }
}

const openDetail = async (version: number) => {
  detailLoading.value = true
  detailVisible.value = true
  try {
    detail.value = await getTestCaseVersion(props.testCaseId, version)
  } finally {
    detailLoading.value = false
  }
}

const handleRestore = async (row: { version: number }) => {
  await ElMessageBox.confirm(
    `把用例内容回滚到第 ${row.version} 版？当前内容会先被存成新的一版，所以这一步不会丢东西。`,
    '回滚版本',
    { type: 'warning' },
  ).catch(() => Promise.reject())

  await restoreTestCaseVersion(props.testCaseId, row.version)
  ElMessage.success(`已回滚到第 ${row.version} 版`)
  detailVisible.value = false
  emit('restored')
  await loadVersions()
}

// ---------------------------------------------------------------- 与当前对比

/**
 * 稳定序列化：对象键排序、**跳过 null/undefined 的键**，再 stringify。
 *
 * 三个坑都在这一个函数里堵住：
 *  1. 键顺序——直接比字符串会因顺序不同而误报；
 *  2. null 与"缺省"——后端序列化 config 时省略了 null 属性，前端对象里却带着 null；
 *  3. **键名大小写**——快照里的 JSON 曾经是 PascalCase（`Url`），接口返回的是 camelCase（`url`），
 *     于是 14 个步骤全被误判成"有差异"。
 * 所以两边都先归一化成这个形式再比，而不是去模仿后端的序列化选项（那等于把同一份约定抄两遍，
 * 抄漏一次就又全误报——这个坑我真踩了一次）。
 */
const stableJson = (value: unknown): string => {
  if (value === null || value === undefined) return 'null'
  if (typeof value !== 'object') return JSON.stringify(value)
  if (Array.isArray(value)) return `[${value.map(stableJson).join(',')}]`
  const obj = value as Record<string, unknown>
  const keys = Object.keys(obj)
    .filter((k) => obj[k] !== null && obj[k] !== undefined)
    .map((k) => k.toLowerCase())
    .sort()
  // 取值时按小写键回查，兼容 PascalCase / camelCase 两种来源
  const lookup = new Map(Object.keys(obj).map((k) => [k.toLowerCase(), obj[k]]))
  return `{${keys.map((k) => `${JSON.stringify(k)}:${stableJson(lookup.get(k))}`).join(',')}}`
}

/** 快照里的 config 是 JSON 字符串；解析失败就当作空对象，别让对比整体报错 */
const parseConfig = (raw: string): unknown => {
  try {
    return JSON.parse(raw)
  } catch {
    return {}
  }
}

/** 参与对比的标量字段（与后端快照字段同名；状态/更新时间那些不在快照里，天然不参与） */
const COMPARED_FIELDS: { key: keyof TestCaseSnapshot; label: string }[] = [
  { key: 'name', label: '名称' },
  { key: 'description', label: '描述' },
  { key: 'module', label: '模块' },
  { key: 'priority', label: '优先级' },
  { key: 'caseCode', label: '编号' },
  { key: 'browser', label: '浏览器' },
  { key: 'timeout', label: '超时' },
  { key: 'retryCount', label: '重试次数' },
  { key: 'failFast', label: '失败即停' },
  { key: 'baseUrl', label: '基址' },
  { key: 'expectedResult', label: '预期结果' },
  { key: 'visualEnabled', label: '视觉回归' },
  { key: 'visualThreshold', label: '视觉阈值' },
  { key: 'dataSetId', label: '数据集' },
  { key: 'requirementId', label: '关联需求' },
]

const diffItems = computed<string[]>(() => {
  const snap = detail.value?.snapshot
  const now = props.current
  if (!snap || !now) return []

  const out: string[] = []
  const current = now as unknown as Record<string, unknown>

  for (const { key, label } of COMPARED_FIELDS) {
    const a = stableJson(snap[key])
    const b = stableJson(current[key])
    if (a !== b) out.push(`${label}：${display(snap[key])} → ${display(current[key])}`)
  }

  if (snap.steps.length !== now.steps.length) {
    out.push(`步骤数：${snap.steps.length} → ${now.steps.length}`)
  } else {
    const currentSteps = new Map(now.steps.map((s) => [s.stepOrder, s]))
    const changed = snap.steps
      .filter((s) => {
        const nowStep = currentSteps.get(s.stepOrder)
        if (!nowStep) return true
        return (
          s.actionType !== nowStep.actionType ||
          stableJson(parseConfig(s.config)) !== stableJson(nowStep.config) ||
          (s.aiInstruction ?? null) !== (nowStep.aiInstruction ?? null)
        )
      })
      .map((s) => s.stepOrder + 1)
    if (changed.length > 0) out.push(`第 ${changed.join('、')} 步的内容有差异`)
  }
  return out
})

const display = (value: unknown): string => {
  if (value === null || value === undefined || value === '') return '(空)'
  if (typeof value === 'boolean') return value ? '开' : '关'
  return String(value)
}

const typeLabel = (type: number) =>
  ({ 0: 'Web', 1: 'API', 2: '移动端' }[type] ?? `未知(${type})`)

const actionLabel = (action: number) =>
({
  0: '点击', 1: '输入', 2: '跳转', 3: '等待', 4: '截图', 5: '滚动',
  6: '请求', 7: '断言响应', 8: '提取变量', 9: 'AI 动作', 10: 'AI 断言',
  11: '断言可见', 12: '断言文本', 13: '断言 URL', 14: '断言标题', 15: '无障碍扫描',
}[action] ?? `动作 ${action}`)
</script>

<style scoped>
.tip {
  margin-bottom: 12px;
}

.detail-desc {
  margin-bottom: 16px;
}

.diff-block {
  margin-bottom: 16px;
}

.diff-title {
  margin-bottom: 6px;
  font-weight: 600;
  font-size: 13px;
}

.diff-list {
  margin: 0;
  padding-left: 18px;
  font-size: 13px;
  line-height: 1.8;
}

.diff-none {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}
.version-toolbar { display: flex; justify-content: flex-end; margin-bottom: 8px; }
</style>
