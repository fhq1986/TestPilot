<template>
  <el-dialog v-model="visible" title="添加用例到范围" width="880px" top="6vh" @closed="resetFilter">
    <div class="picker-filters">
      <el-input v-model="keyword" placeholder="搜索用例名称" clearable class="picker-search" />
      <!-- 模块筛选：选项从候选里就地取（懒加载一次就够），不用额外请求 -->
      <el-select v-model="moduleFilter" placeholder="全部模块" clearable class="picker-module">
        <el-option v-for="m in moduleOptions" :key="m" :label="m" :value="m" />
      </el-select>
    </div>

    <div class="picker-body">
      <!-- 左：候选列表（本地分页；勾选状态自管理，过滤/翻页不丢） -->
      <div class="picker-candidates">
        <el-table :data="pagedCandidates" size="small" max-height="420"
          @row-click="togglePicked">
          <el-table-column width="44" align="center">
            <template #header>
              <!-- 全选只作用于当前页：跨页全选语义含糊，容易一口气勾进几百条 -->
              <el-checkbox :model-value="allPageSelected" :indeterminate="pageIndeterminate"
                title="全选/取消本页" @change="toggleSelectPage" @click.stop />
            </template>
            <template #default="{ row }">
              <el-checkbox :model-value="pickedIds.includes(row.id)" @change="togglePicked(row)" @click.stop />
            </template>
          </el-table-column>
          <el-table-column prop="name" label="用例名称">
            <template #default="{ row }">
              <span :class="{ 'is-picked': pickedIds.includes(row.id) }">{{ row.name }}</span>
            </template>
          </el-table-column>
          <el-table-column prop="module" label="模块" width="150">
            <template #default="{ row }">{{ row.module || '—' }}</template>
          </el-table-column>
          <el-table-column prop="priority" label="优先级" width="70" />
        </el-table>
        <el-pagination v-model:current-page="casePage" v-model:page-size="casePageSize" :total="filteredCount"
          size="small" layout="total, sizes, prev, pager, next" :page-sizes="[10, 20, 50, 100]"
          class="picker-pagination" @size-change="() => { casePage = 1 }" />
      </div>

      <!-- 右：已选列表。翻页/过滤不影响这里，取消即移除 -->
      <div class="picker-selected">
        <div class="selected-header">
          <span>已选用例</span>
          <el-tag size="small" effect="plain" round>{{ pickedCases.length }}</el-tag>
          <el-button v-if="pickedCases.length > 0"  type="danger" size="small"
            class="selected-clear" @click="handleClearPicked">全部删除</el-button>
        </div>
        <div v-if="pickedCases.length === 0" class="picker-empty">
          <div class="picker-empty-icon">☑</div>
          尚未选择用例
        </div>
        <div v-else class="selected-list">
          <div v-for="(item, index) in pickedCases" :key="item.id" class="selected-item">
            <span class="selected-order">{{ index + 1 }}.</span>
            <span class="selected-name" :title="item.name">{{ item.name }}</span>
            <el-button class="selected-remove" link type="danger" :icon="Close" size="small"
              title="取消选择" @click.stop="unpick(item.id)" />
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <span class="picker-count">追加后将按当前顺序排在范围末尾</span>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="primary" :disabled="pickedCases.length === 0" @click="apply">
        追加到范围（{{ pickedCases.length }}）
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import { Close } from '@element-plus/icons-vue'
import { getTestCases } from '@/api/testcase'
import type { TestCaseSummary } from '@/types/testcase'

/** 追加结果由父级合并进范围并落库 */
const emit = defineEmits<{ apply: [pickedIds: string[]] }>()

const visible = ref(false)
const keyword = ref('')
const moduleFilter = ref<string>('')
const candidates = ref<TestCaseSummary[]>([])
/** 已选用例对象：自管理勾选状态，翻页/过滤/右侧取消都只是改这个数组 */
const pickedCases = ref<TestCaseSummary[]>([])
/** 已在范围内的用例不重复出现在候选里 */
const inScope = ref(new Set<string>())

const pickedIds = computed(() => pickedCases.value.map((c) => c.id))

/**
 * 打开弹窗。candidates 按项目懒加载：同一计划的范围内多次添加共享一次请求。
 * （candidates 是组件实例级的，同一个计划详情页内 projectId 不会变）
 */
const open = async (projectId: string | undefined, scopeIds: string[]) => {
  if (candidates.value.length === 0) {
    const res = await getTestCases({ projectId, page: 1, pageSize: 200 })
    candidates.value = res.items
  }
  inScope.value = new Set(scopeIds)
  resetFilter()
  visible.value = true
}

function resetFilter() {
  keyword.value = ''
  moduleFilter.value = ''
  pickedCases.value = []
  casePage.value = 1
}

/** 模块下拉选项：候选里出现过的模块去重（null 过滤掉） */
const moduleOptions = computed(() =>
  [...new Set(candidates.value.map((c) => c.module).filter((m): m is string => !!m))],
)

const filteredCandidates = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  return candidates.value
    .filter((c) => !inScope.value.has(c.id))
    .filter((c) => !moduleFilter.value || c.module === moduleFilter.value)
    .filter((c) => !kw || c.name.toLowerCase().includes(kw))
})

const filteredCount = computed(() => filteredCandidates.value.length)

// ---------------------------------------------------------------- 本地分页
const casePage = ref(1)
const casePageSize = ref(10)

const pagedCandidates = computed(() => {
  const start = (casePage.value - 1) * casePageSize.value
  return filteredCandidates.value.slice(start, start + casePageSize.value)
})

// ---------------------------------------------------------------- 勾选（自管理，跨过滤/翻页持久）
const togglePicked = (row: TestCaseSummary) => {
  if (pickedIds.value.includes(row.id)) {
    unpick(row.id)
    return
  }
  pickedCases.value.push(row)
}

const unpick = (id: string) => {
  pickedCases.value = pickedCases.value.filter((c) => c.id !== id)
}

/** 全部删除：确认后清空本弹窗内已勾选的用例（不影响已落库的范围） */
async function handleClearPicked() {
  try {
    await ElMessageBox.confirm(
      `将取消本次已勾选的 ${pickedCases.value.length} 个用例（不会影响已保存的范围）。`,
      '清空已选用例',
      { type: 'warning', confirmButtonText: '全部删除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }
  pickedCases.value = []
}

/** 全选只作用于当前页 */
const allPageSelected = computed(() =>
  pagedCandidates.value.length > 0 && pagedCandidates.value.every((c) => pickedIds.value.includes(c.id)))

const pageIndeterminate = computed(() => {
  const picked = pagedCandidates.value.filter((c) => pickedIds.value.includes(c.id)).length
  return picked > 0 && picked < pagedCandidates.value.length
})

const toggleSelectPage = (checked: boolean | string | number) => {
  const want = Boolean(checked)
  for (const c of pagedCandidates.value) {
    const inList = pickedIds.value.includes(c.id)
    if (want && !inList) pickedCases.value.push(c)
    else if (!want && inList) unpick(c.id)
  }
}

function apply() {
  emit('apply', pickedCases.value.map((c) => c.id))
  visible.value = false
}

/** 父级通过 ref 调 open() 打开弹窗 */
defineExpose({ open })
</script>

<style scoped>
.picker-filters { display: flex; gap: 8px; margin-bottom: 12px; }
.picker-search { flex: 1; }
.picker-module { width: 180px; flex-shrink: 0; }

.picker-body { display: flex; gap: 12px; align-items: stretch; }
.picker-candidates { flex: 1; min-width: 0; }
.picker-candidates :deep(.el-table__row) { cursor: pointer; }
.picker-pagination { margin-top: 8px; justify-content: flex-end; }
.is-picked { color: var(--el-color-primary); font-weight: 500; }

/* 右侧已选面板：与候选等高，列表内滚动 */
.picker-selected {
  width: 240px;
  flex-shrink: 0;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  background: #fff;
  padding: 10px;
  display: flex;
  flex-direction: column;
}

.selected-header {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  font-weight: 500;
  color: var(--el-text-color-regular);
  padding: 2px 4px 10px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  margin-bottom: 6px;
}

/* "全部删除"靠右，hover 前弱显避免抢视觉 */
.selected-clear { margin-left: auto; }
.selected-header .selected-clear { opacity: 1; transition: opacity 0.15s; }
.selected-header .selected-clear:hover { opacity: 1; }

.selected-list { flex: 1; overflow-y: auto; max-height: 420px; }

.picker-empty {
  color: #9aa2ae;
  font-size: 13px;
  text-align: center;
  padding: 32px 0;
}
.picker-empty-icon { font-size: 26px; margin-bottom: 6px; opacity: 0.5; }

.selected-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 5px 6px;
  margin-bottom: 2px;
  font-size: 13px;
  background: #fff;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  transition: border-color 0.15s, box-shadow 0.15s;
}

/* hover：边框淡红 + 左侧描边条，与"移除"动作的颜色语义呼应 */
.selected-item:hover {
  border-color: rgba(245, 108, 108, 0.55);
  box-shadow: inset 2px 0 0 var(--el-color-danger);
}

.selected-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}


.picker-count { float: left; color: #7a8290; font-size: 13px; line-height: 32px; }
</style>
