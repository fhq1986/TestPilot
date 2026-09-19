import { ref } from 'vue'
import type { TableInstance } from 'element-plus'

/**
 * 列表「点击行直接勾选」的通用交互：
 * - 点击数据列 → toggleRowSelection 切换勾选；
 * - 复选框列（type=selection）不算行点击——checkbox 自身的 change 已切换，冒泡上来再切会双触发；
 * - 操作列（label=操作）不算行点击——按钮/空白处点击不应改变勾选；
 * - isSelectable 钩子与列的 :selectable 保持一致，不可选的行点击无效。
 *
 * 用法：表格上加 ref="tableRef" 与 @row-click="handleRowSelectionClick"。
 * 表格内嵌套的可点击元素（名称链接、项目链接等）请加 @click.stop，避免跳转的同时误勾选。
 */
export function useRowSelection(isSelectable?: (row: never) => boolean) {
  const tableRef = ref<TableInstance>()

  const handleRowSelectionClick = (row: never, column: { type?: string; label?: string }) => {
    if (column.type === 'selection') return
    if (column.label === '操作') return
    if (isSelectable && !isSelectable(row)) return
    tableRef.value?.toggleRowSelection(row)
  }

  return { tableRef, handleRowSelectionClick }
}
