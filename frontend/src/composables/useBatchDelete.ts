import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { BatchDeleteResult } from '@/types/common'

/**
 * 列表页批量删除的通用交互：
 * 选中行 → 二次确认 → 调用删除接口 → 汇总结果（含被跳过原因）→ 刷新列表。
 */
export function useBatchDelete<T extends { id: string }>(options: {
  /** 实体名称，用于提示文案，如「测试用例」 */
  entity: string
  remove: (ids: string[]) => Promise<BatchDeleteResult>
  reload: () => Promise<unknown> | unknown
}) {
  const deleting = ref(false)
  const selectedRows = ref<T[]>([])

  const onSelectionChange = (rows: T[]) => {
    selectedRows.value = rows
  }

  const handleBatchDelete = async () => {
    const rows = selectedRows.value
    if (rows.length === 0) {
      ElMessage.warning(`请先选择要删除的${options.entity}`)
      return
    }

    try {
      await ElMessageBox.confirm(
        `确认删除选中的 ${rows.length} 个${options.entity}？此操作不可撤销。`,
        `批量删除${options.entity}`,
        { type: 'warning', confirmButtonText: '确认删除', cancelButtonText: '取消' },
      )
    } catch {
      return
    }

    deleting.value = true
    try {
      const result = await options.remove(rows.map((row) => row.id))
      const skipped = result.skipped ?? []
      if (skipped.length > 0) {
        const shown = skipped
          .slice(0, 8)
          .map((item) => `${item.name || item.id.slice(0, 8)}：${item.reason}`)
          .join('\n')
        const more = skipped.length > 8 ? `\n…以及另外 ${skipped.length - 8} 条` : ''
        await ElMessageBox.alert(
          `${shown}${more}`,
          `已删除 ${result.deleted} 条，${skipped.length} 条被跳过`,
          { type: 'warning', confirmButtonText: '知道了' },
        )
      } else {
        ElMessage.success(`已删除 ${result.deleted} 个${options.entity}`)
      }
      selectedRows.value = []
      await options.reload()
    } finally {
      deleting.value = false
    }
  }

  return { deleting, selectedRows, onSelectionChange, handleBatchDelete }
}
