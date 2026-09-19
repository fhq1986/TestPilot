<template>
  <div>
    <div class="scope-toolbar">
      <el-button :icon="Plus" @click="emit('add-cases')">添加用例</el-button>
      <el-button :icon="Download" :disabled="!plan" @click="emit('import-suite')">从套件导入</el-button>
      <el-button type="danger" plain :icon="Delete" :disabled="items.length === 0"
        @click="handleClearScope">全部删除</el-button>
      <span class="scope-hint">
        顺序即执行顺序：按住左侧 ⠿ 拖动排序，也可用 ↑ ↓ 精确微调（改动立即保存）
      </span>
    </div>

    <el-alert v-if="plan && plan.scopeIssues.length > 0" type="warning" :closable="false"
      class="scope-issues">
      <template #title>范围体检发现 {{ plan.scopeIssues.length }} 个问题（执行时会被跳过或浪费轮次）</template>
      <div v-for="(issue, i) in plan.scopeIssues" :key="i" class="issue-line">
        <el-tag :type="issue.level === 'Error' ? 'danger' : 'warning'" size="small" effect="plain">
          {{ issue.kind }}
        </el-tag>
        <span class="issue-name">{{ issue.name }}</span>
        <span class="issue-msg">{{ issue.message }}</span>
        <el-button link type="primary" size="small" @click="emit('remove', issue.testCaseId)">
          移除
        </el-button>
      </div>
    </el-alert>
    <el-alert v-else-if="plan && plan.caseCount > 0" type="success" :closable="false"
      class="scope-issues" title="范围体检通过：所有用例都可执行" />

    <el-alert v-if="runningRound" type="info" :closable="false" class="scope-issues">
      <template #title>
        当前有进行中的第 {{ runningRound.roundNo }} 轮，范围调整（含排序）将在<b>下一轮</b>生效——
        已开的轮次用的是它自己的范围快照。
      </template>
    </el-alert>

    <!-- 拖拽排序：用原生 HTML5 DnD，不引入 sortablejs。
         拖拽手柄放在我自己控制的模板里（而不是给 el-table 的 <tr> 打 draggable 属性），
         这样重渲染不会把属性冲掉；落点判定用事件委托 + closest('tr') 算行号。 -->
    <div class="scope-drag-area" :class="{ 'is-dragging': dragIndex !== null }"
      @dragover.prevent="onDragOver" @drop.prevent="onDrop" @dragleave="onDragLeave">
      <el-table :data="items" size="small" max-height="520" class="scope-table"
        :row-class-name="scopeRowClass">
        <el-table-column label="" width="66" align="center">
          <template #default="{ $index }">
            <span class="drag-cell">
              <span class="drag-handle" draggable="true" title="按住拖拽调整执行顺序"
                @dragstart="onDragStart($index, $event)" @dragend="onDragEnd">⠿</span>
              <span class="row-no">{{ $index + 1 }}</span>
            </span>
          </template>
        </el-table-column>
        <el-table-column label="用例名称" min-width="240">
          <template #default="{ row }">
            <span :class="{ 'deleted-row': row.deleted }">{{ row.name }}</span>
            <el-tag v-if="row.deleted" type="danger" size="small" effect="plain" class="ml-6">已删除</el-tag>
            <el-tag v-if="row.isFlaky" type="warning" size="small" effect="plain" class="ml-6">flaky</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="模块" width="140">
          <template #default="{ row }">{{ row.module || '—' }}</template>
        </el-table-column>
        <el-table-column label="优先级" width="90">
          <template #default="{ row }">{{ row.priority || '—' }}</template>
        </el-table-column>
        <el-table-column label="操作" width="140">
          <template #default="{ row, $index }">
            <!-- 保留上移/下移：拖拽之外还能精确微调，也是无鼠标场景的兜底 -->
            <el-tooltip content="上移" placement="top">
              <el-button link type="primary" size="small" :disabled="$index === 0"
                @click="moveItem(row, -1)">上移</el-button>
            </el-tooltip>
            <el-tooltip content="下移" placement="top">
              <el-button link type="primary" size="small" :disabled="$index === items.length - 1"
                @click="moveItem(row, 1)">下移</el-button>
            </el-tooltip>
            <el-button link type="danger" size="small" @click="emit('remove', row.testCaseId)">移除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>
    <el-empty v-if="items.length === 0" description="范围内还没有用例，先添加或从套件导入" />
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import { Delete, Download, Plus } from '@element-plus/icons-vue'
import { isAfterRow, moveInList } from '@/utils/reorder'
import type { PlanRoundSummary, TestPlanDetail, TestPlanItem } from '@/types/testPlan'

const props = defineProps<{
  plan: TestPlanDetail | null
  items: TestPlanItem[]
  runningRound: PlanRoundSummary | null
}>()

/** 排序/移除的实际落库由父级统一做（需要 planId 与整页刷新） */
const emit = defineEmits<{
  'add-cases': []
  'import-suite': []
  remove: [testCaseId: string | null | undefined]
  reorder: [ids: string[]]
  clear: []
}>()

/** 全部删除：二次确认（不可恢复的批量操作，确认文案里带数量让用户知道影响面） */
async function handleClearScope() {
  try {
    await ElMessageBox.confirm(
      `将把范围内的 ${props.items.length} 个用例全部移除，执行计划将不再包含它们。确认清空？`,
      '清空用例范围',
      { type: 'warning', confirmButtonText: '全部删除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }
  emit('clear')
}

// ---------------------------------------------------------------- 拖拽排序
const dragIndex = ref<number | null>(null)
/** 落点所在行 */
const dragOverIndex = ref<number | null>(null)
/** 落点在目标行的下半部分（插到它后面）还是上半部分（插到它前面） */
const dragOverAfter = ref(false)

function onDragStart(index: number, event: DragEvent) {
  dragIndex.value = index
  dragOverIndex.value = index
  dragOverAfter.value = false
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move'
    // Firefox 不设 data 就不触发 drop，给个占位值
    event.dataTransfer.setData('text/plain', String(index))
  }
}

function onDragEnd() {
  dragIndex.value = null
  dragOverIndex.value = null
  dragOverAfter.value = false
}

function onDragLeave(event: DragEvent) {
  // 只有真正离开整个拖拽区域才清掉落点，否则在行间移动会不断闪
  const area = event.currentTarget as HTMLElement | null
  if (area && event.relatedTarget instanceof Node && area.contains(event.relatedTarget)) return
  dragOverIndex.value = null
}

function onDragOver(event: DragEvent) {
  if (dragIndex.value === null) return
  const hit = resolveDropTarget(event)
  if (!hit) return
  dragOverIndex.value = hit.index
  dragOverAfter.value = hit.after
}

async function onDrop(event: DragEvent) {
  const from = dragIndex.value
  const hit = resolveDropTarget(event)
  const overIndex = hit?.index ?? dragOverIndex.value
  const after = hit?.after ?? dragOverAfter.value
  onDragEnd()
  if (from === null || overIndex === null) return

  const ids = props.items.map((i) => i.testCaseId)
  // 下标运算走单测覆盖过的纯函数：落点差一位这种 bug 很难靠肉眼发现
  const next = moveInList(ids, from, overIndex, after)
  if (next === null) return // 拖回原位，不产生无意义的保存
  emit('reorder', next)
}

/**
 * 从拖拽事件算出落点。
 *
 * 不去给 el-table 的 <tr> 打 draggable 属性（重渲染会冲掉），
 * 而是从事件目标往上找 <tr>，再用它在 tbody 里的位置换成数据下标
 * （tbody 的 tr 顺序与 items 一致）。上下半部分按行中线区分。
 */
function resolveDropTarget(event: DragEvent): { index: number; after: boolean } | null {
  const target = event.target as HTMLElement | null
  const row = target?.closest('tr')
  if (!row) return null
  const tbody = row.parentElement
  if (!tbody) return null
  const index = Array.prototype.indexOf.call(tbody.children, row)
  if (index < 0 || index >= props.items.length) return null

  const rect = row.getBoundingClientRect()
  return { index, after: isAfterRow(event.clientY, rect.top, rect.height) }
}

/** 行样式：拖拽源半透明，落点在目标行上/下边缘画插入线 */
function scopeRowClass({ rowIndex }: { rowIndex: number }) {
  const classes: string[] = []
  if (dragIndex.value === rowIndex) classes.push('drag-source')
  if (dragIndex.value !== null && dragOverIndex.value === rowIndex && dragIndex.value !== rowIndex) {
    classes.push(dragOverAfter.value ? 'drag-over-after' : 'drag-over-before')
  }
  return classes.join(' ')
}

async function moveItem(row: TestPlanItem, offset: number) {
  const ids = props.items.map((i) => i.testCaseId)
  const index = ids.indexOf(row.testCaseId)
  const target = index + offset
  if (index < 0 || target < 0 || target >= ids.length) return
  const arr = [...ids]
  ;[arr[index], arr[target]] = [arr[target], arr[index]]
  emit('reorder', arr)
}
</script>

<style scoped>
.scope-toolbar { display: flex; align-items: center; gap: 10px; margin-bottom: 12px; }
.scope-hint { color: #9aa2ae; font-size: 12px; }
.scope-issues { margin-bottom: 12px; }
.issue-line { display: flex; align-items: center; gap: 8px; padding: 3px 0; font-size: 13px; }
.issue-name { font-weight: 500; }
.issue-msg { color: #7a8290; }
.scope-table { width: 100%; }
.deleted-row { text-decoration: line-through; color: #9aa2ae; }
.ml-6 { margin-left: 6px; }

/* ---------- 拖拽排序 ---------- */
.scope-drag-area { position: relative; }

.drag-cell {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.drag-handle {
  cursor: grab;
  color: #b4b2a9;
  font-size: 14px;
  line-height: 1;
  padding: 2px 4px;
  border-radius: 3px;
  user-select: none;
  transition: color 0.15s, background 0.15s;
}

.drag-handle:hover {
  color: var(--el-color-primary);
  background: var(--el-fill-color-light);
}

.drag-handle:active {
  cursor: grabbing;
}

.row-no {
  color: #9aa2ae;
  font-size: 12px;
  min-width: 14px;
}

/* 拖拽源：淡出，让落点更醒目 */
.scope-drag-area :deep(.drag-source) {
  opacity: 0.4;
}

/* 插入线用 inset box-shadow 而不是 border：加边框会让行高跳动，
   而拖拽过程中行高一抖，落点就会在两个位置之间来回跳 */
.scope-drag-area :deep(.drag-over-before) > td {
  box-shadow: inset 0 2px 0 0 var(--el-color-primary);
}

.scope-drag-area :deep(.drag-over-after) > td {
  box-shadow: inset 0 -2px 0 0 var(--el-color-primary);
}

/* 拖拽进行中：整个区域提示可放置 */
.scope-drag-area.is-dragging :deep(.el-table__row) {
  cursor: grabbing;
}
</style>
