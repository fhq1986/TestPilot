<template>
  <div class="step-list">
    <div class="step-toolbar">
      <el-button type="primary" :icon="Plus" @click="$emit('add')">添加步骤</el-button>
      <el-button :icon="Memo" @click="$emit('addShared')">插入共享步骤</el-button>
      <span class="toolbar-hint">
        共享步骤在运行时展开，组里的内容改了，本用例下次执行即生效
      </span>
    </div>

    <el-table v-if="!isMobile" ref="tableRef" :data="steps" row-key="localId" size="small">
      <el-table-column label="" width="45" class-name="drag-col">
        <template #default>
          <span class="drag-handle" title="拖动排序"><el-icon><Rank /></el-icon></span>
        </template>
      </el-table-column>
      <el-table-column label="序号" width="56">
        <template #default="{ $index }">{{ $index + 1 }}</template>
      </el-table-column>
      <el-table-column label="动作" width="130">
        <template #default="{ row }">
          <el-tag v-if="row.sharedGroupId" type="warning" size="small" effect="light">
            共享步骤
          </el-tag>
          <el-tag v-else size="small">{{ actionLabel(row.actionType) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="配置摘要" min-width="260">
        <template #default="{ row }">
          <template v-if="row.sharedGroupId">
            <span class="shared-name">
              <el-icon><Memo /></el-icon>
              {{ row.sharedGroupName || '（共享步骤组已删除）' }}
            </span>
            <span v-if="row.sharedVariables && row.sharedVariables.length > 0" class="shared-vars">
              覆盖变量：{{ sharedVarSummary(row) }}
            </span>
          </template>
          <span v-else class="step-summary">{{ configSummary(row) }}</span>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="220" align="right">
        <template #default="{ row, $index }">
          <el-button link type="primary" size="small" :disabled="$index === 0" @click="$emit('move', row, -1)">上移</el-button>
          <el-button link type="primary" size="small" :disabled="$index === steps.length - 1" @click="$emit('move', row, 1)">下移</el-button>
          <el-button link type="primary" size="small" @click="$emit('edit', row)">
            {{ row.sharedGroupId ? '设置变量' : '编辑' }}
          </el-button>
          <el-button link type="danger" size="small" @click="$emit('remove', row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>

    <!-- 窄屏：换成卡片。这张表列宽合计 680px，操作按钮（220px）在最右，
         在 375px 上横滚意味着「上移/编辑/删除」全都点不到 —— 步骤编辑是核心操作，不能丢。 -->
    <div v-else ref="cardsRef" class="step-cards">
      <el-empty v-if="steps.length === 0" description="还没有步骤，点「添加步骤」开始" :image-size="60" />
      <div v-for="(row, index) in steps" :key="row.localId" class="step-card">
        <div class="step-card__head">
          <span class="drag-handle-card" title="拖动排序"><el-icon><Rank /></el-icon></span>
          <span class="step-index">{{ index + 1 }}</span>
          <el-tag v-if="row.sharedGroupId" type="warning" size="small" effect="light">共享步骤</el-tag>
          <el-tag v-else size="small">{{ actionLabel(row.actionType) }}</el-tag>
        </div>

        <div class="step-card__body">
          <template v-if="row.sharedGroupId">
            <span class="shared-name">
              <el-icon><Memo /></el-icon>
              {{ row.sharedGroupName || '（共享步骤组已删除）' }}
            </span>
            <span v-if="row.sharedVariables && row.sharedVariables.length > 0" class="shared-vars">
              覆盖变量：{{ sharedVarSummary(row) }}
            </span>
          </template>
          <span v-else class="step-summary">{{ configSummary(row) }}</span>
        </div>

        <div class="step-card__actions">
          <el-button link type="primary" size="small" :disabled="index === 0" @click="$emit('move', row, -1)">上移</el-button>
          <el-button link type="primary" size="small" :disabled="index === steps.length - 1" @click="$emit('move', row, 1)">下移</el-button>
          <el-button link type="primary" size="small" @click="$emit('edit', row)">
            {{ row.sharedGroupId ? '设置变量' : '编辑' }}
          </el-button>
          <el-button link type="danger" size="small" @click="$emit('remove', row)">删除</el-button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { Memo, Plus, Rank } from '@element-plus/icons-vue'
import Sortable from 'sortablejs'
import { ActionType, type TestStep } from '@/types/testcase'
import { useBreakpoint } from '@/composables/useBreakpoint'

export interface EditableStep extends TestStep {
  localId: number
}

const props = defineProps<{ steps: EditableStep[] }>()

const emit = defineEmits<{
  add: []
  addShared: []
  edit: [row: EditableStep]
  remove: [row: EditableStep]
  move: [row: EditableStep, offset: number]
  reorder: [steps: EditableStep[]]
}>()

/** 窄屏（< 768px）：表格换成卡片，操作按钮不再被横滚推到屏幕外 */
const { isMobile } = useBreakpoint()

// ===== SortableJS 拖拽排序 =====
const tableRef = ref<any>(null)
const cardsRef = ref<HTMLElement | null>(null)
let tableSortable: Sortable | null = null
let cardsSortable: Sortable | null = null

function initTableSortable() {
  if (!tableRef.value) return
  const tbody = tableRef.value.$el?.querySelector('.el-table__body-wrapper tbody')
  if (!tbody) return
  tableSortable?.destroy()
  tableSortable = Sortable.create(tbody as HTMLElement, {
    animation: 180,
    handle: '.drag-handle',
    ghostClass: 'drag-ghost',
    onEnd: (evt) => {
      if (evt.oldIndex == null || evt.newIndex == null || evt.oldIndex === evt.newIndex) return
      const arr = [...props.steps]
      const [moved] = arr.splice(evt.oldIndex, 1)
      arr.splice(evt.newIndex, 0, moved)
      emit('reorder', arr)
    },
  })
}

function initCardsSortable() {
  if (!cardsRef.value) return
  cardsSortable?.destroy()
  cardsSortable = Sortable.create(cardsRef.value, {
    animation: 180,
    handle: '.drag-handle-card',
    ghostClass: 'drag-ghost',
    onEnd: (evt) => {
      if (evt.oldIndex == null || evt.newIndex == null || evt.oldIndex === evt.newIndex) return
      const arr = [...props.steps]
      const [moved] = arr.splice(evt.oldIndex, 1)
      arr.splice(evt.newIndex, 0, moved)
      emit('reorder', arr)
    },
  })
}

function destroyAllSortable() {
  tableSortable?.destroy(); tableSortable = null
  cardsSortable?.destroy(); cardsSortable = null
}

onMounted(async () => {
  await nextTick()
  if (!isMobile.value) initTableSortable()
  else initCardsSortable()
})

// 切换移动 / 桌面时重建 sortable
watch(isMobile, async (val) => {
  destroyAllSortable()
  await nextTick()
  if (!val) initTableSortable()
  else initCardsSortable()
})

watch(props.steps, async () => {
  // 数据变化时重建 sortable（增删步骤后 DOM 更新）
  await nextTick()
  if (!isMobile.value && tableSortable === null) initTableSortable()
  else if (isMobile.value && cardsSortable === null) initCardsSortable()
})

onUnmounted(destroyAllSortable)

const actionLabels: Record<number, string> = {
  [ActionType.Click]: '点击',
  [ActionType.Fill]: '输入',
  [ActionType.Navigate]: '打开',
  [ActionType.Wait]: '等待',
  [ActionType.Screenshot]: '截图',
  [ActionType.Scroll]: '滚动',
  [ActionType.Request]: '接口请求',
  [ActionType.AssertResponse]: '响应断言',
  [ActionType.ExtractVariable]: '变量提取',
  [ActionType.AIAction]: 'AI 动作',
  [ActionType.AIAssert]: 'AI 断言',
  [ActionType.AssertVisible]: '可见性断言',
  [ActionType.AssertText]: '文本断言',
  [ActionType.AssertUrl]: '地址断言',
  [ActionType.AssertTitle]: '标题断言',
  [ActionType.AssertA11y]: '无障碍扫描',
}

const actionLabel = (type: ActionType) => actionLabels[type] ?? '未知'

/** 共享步骤的变量覆盖摘要。抽成函数是因为 el-table 作用域插槽的 row 是 any，
 *  在模板里直接 map 会触发 noImplicitAny。 */
const sharedVarSummary = (step: EditableStep) =>
  (step.sharedVariables ?? []).map((v) => `${v.name}=${v.value}`).join('、')

const configSummary = (step: EditableStep) => {
  const cfg = step.config ?? {}
  const parts: string[] = []
  if (cfg.url) parts.push(cfg.url)
  if (cfg.selector?.value) parts.push(`选择器: ${cfg.selector.value}`)
  if (cfg.selector?.type === 'ai') parts.push('AI 定位')
  if (cfg.value) parts.push(`值: ${cfg.value}`)
  if (cfg.method && cfg.endpoint) parts.push(`${cfg.method} ${cfg.endpoint}`)
  // 无障碍扫描没有选择器也没有 URL，只按级别描述，否则会显示成一个孤零零的「—」
  if (step.actionType === ActionType.AssertA11y) {
    return cfg.value ? `拦截 ${cfg.value} 及以上违规` : '拦截严重及以上违规（默认）'
  }
  return parts.join(' · ') || '—'
}
</script>

<style scoped>
.step-toolbar {
  margin-bottom: 12px;
  display: flex;
  align-items: center;
  gap: 10px;
}

/* 拖拽把手 —— 表格列 */
.drag-col { cursor: grab; }
.drag-handle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  color: #c0c4cc;
  cursor: grab;
  font-size: 16px;
  border-radius: 4px;
  transition: color 0.15s, background-color 0.15s;
}
.drag-handle:hover { color: #1f3b73; background-color: #f0f4fb; }
.drag-handle:active { cursor: grabbing; }

/* 拖拽 ghost —— 拖过的目标位置高亮 */
:deep(.drag-ghost) {
  opacity: 0.4;
  background-color: #e4ecfb !important;
  border-radius: 4px;
}
:deep(.el-table__body tr.drag-ghost td) { background-color: #e4ecfb !important; }

/* 拖拽把手 —— 卡片 */
.drag-handle-card {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  color: #c0c4cc;
  cursor: grab;
  font-size: 16px;
  border-radius: 4px;
  margin-right: 4px;
  transition: color 0.15s, background-color 0.15s;
}
.drag-handle-card:hover { color: #1f3b73; background-color: #f0f4fb; }
.drag-handle-card:active { cursor: grabbing; }

.toolbar-hint {
  color: #9aa2ae;
  font-size: 12px;
}

.step-summary {
  color: #606266;
  font-size: 13px;
}

.shared-name {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: var(--el-color-warning);
  font-size: 13px;
}

.shared-vars {
  display: block;
  margin-top: 2px;
  color: #9aa2ae;
  font-size: 12px;
}

/* ---------------------------------------------------------------- 卡片（窄屏） */
.step-cards {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.step-card {
  padding: 10px 12px;
  background-color: #fff;
  border: 1px solid #d5e0ee;
  border-radius: 8px;
}

.step-card__head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.step-index {
  min-width: 22px;
  height: 22px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  background-color: #f4f8fd;
  color: #1f3b73;
  font-size: 12px;
  font-weight: 600;
}

.step-card__body {
  margin-top: 8px;
  min-width: 0;
}

.step-card__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px dashed #e4e7ed;
}

@media (max-width: 767px) {
  /* 两个按钮 + 一句说明在 375px 上排不下，让说明换行到下一行 */
  .step-toolbar {
    flex-wrap: wrap;
  }

  .toolbar-hint {
    flex-basis: 100%;
  }
}
</style>
