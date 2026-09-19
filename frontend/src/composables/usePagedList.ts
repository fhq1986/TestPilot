import { reactive, ref, shallowRef } from 'vue'

/** 后端分页响应的最小形状（与 PagedResult<T> 同构，避免循环依赖具体 API 类型） */
interface PagedResult<T> {
  items: T[]
  total: number
}

export interface PagedList<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
  loading: boolean
  /** 拉取列表；传 targetPage 时先切页码（筛选条件变更后回到第一页的场景） */
  load: (targetPage?: number) => Promise<void>
}

/**
 * 分页列表的通用状态机：items / total / page / pageSize / loading + load()。
 *
 * 各列表页与项目详情的四个 tab 全在复制这套样板（页码/页大小/总数/loading/
 * 「切筛选回第一页」的联动），逐页手写迟早分叉——调用方只保留
 * 「组装查询参数 + 调 API」的 fetcher，筛选条件通过闭包读取。
 *
 * items 用 shallowRef：列表整组替换、不做深层响应式，既省开销也避开
 * reactive 深度解包对泛型 T 的类型侵蚀。
 * 返回 reactive 包装：模板里 list.items / list.loading 直接可用（ref 自动解包），
 * v-model:current-page="list.page" 按普通属性绑定。
 */
export function usePagedList<T>(
  fetcher: (page: number, pageSize: number) => Promise<PagedResult<T>>,
  options: { pageSize?: number } = {},
): PagedList<T> {
  const items = shallowRef<T[]>([])
  const total = ref(0)
  const page = ref(1)
  const pageSize = ref(options.pageSize ?? 10)
  const loading = ref(false)

  async function load(targetPage?: number) {
    if (targetPage) page.value = targetPage
    loading.value = true
    try {
      const res = await fetcher(page.value, pageSize.value)
      items.value = res.items
      total.value = res.total
    } finally {
      loading.value = false
    }
  }

  return reactive({ items, total, page, pageSize, loading, load })
}
