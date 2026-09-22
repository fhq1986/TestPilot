<template>
  <div class="notification-page">

    <!-- 未读概览 + 分类快捷筛选：把「还有多少没看」和「哪一类没看」放在一起，
         点分类标签就直接筛，省掉"先想类型名再去下拉里找"的一步 -->
    <el-card shadow="never" class="overview-card">
      <div class="overview">
        <div class="overview-main" :class="{ zero: store.unreadTotal === 0 }">
          <div class="overview-value">{{ store.unreadTotal }}</div>
          <div class="overview-label">未读消息</div>
        </div>

        <div class="overview-chips">
          <button type="button" class="chip" :class="{ active: category === undefined }"
            @click="pickCategory(undefined)">
            全部
          </button>
          <button v-for="c in unreadChips" :key="c.value" type="button" class="chip"
            :class="{ active: category === c.value }" @click="pickCategory(c.value)">
            <el-icon>
              <component :is="categoryIcon(c.value)" />
            </el-icon>
            <span>{{ c.label }}</span>
            <span class="chip-count">{{ c.count }}</span>
          </button>
          <span v-if="unreadChips.length === 0" class="overview-empty">
            暂无未读消息，都处理完了
          </span>
        </div>
      </div>
    </el-card>

    <el-card shadow="never" class="list-card">
      <div class="filter-bar">
        <el-select v-model="category" placeholder="全部类型" clearable class="w-120" @change="reload">
          <el-option v-for="(label, value) in NOTIFICATION_CATEGORY_LABELS" :key="value" :label="label"
            :value="Number(value)" />
        </el-select>
        <el-select v-model="projectId" placeholder="全部项目" clearable class="w-160" @change="reload">
          <el-option v-for="p in projects" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-input v-model="title" placeholder="标题 / 内容搜索" clearable class="w-160" @keyup.enter="reload"
          @clear="reload" />
        <el-date-picker v-model="dateRange" type="daterange" start-placeholder="开始日期" end-placeholder="结束日期"
          value-format="YYYY-MM-DD"  @change="reload" class="w-240" />
        <el-checkbox v-model="unreadOnly" @change="reload">仅看未读</el-checkbox>
        <el-button type="primary" :icon="Search" @click="reload">查询</el-button>
        <div class="toolbar-spacer" />
        <el-button :icon="Select" :disabled="store.unreadTotal === 0" @click="handleReadAll">全部已读</el-button>
        <el-button :icon="Refresh" @click="load()">刷新</el-button>
      </div>

      <div v-if="!isMobile" v-loading="loading" class="msg-list">
        <div v-for="item in items" :key="item.id" class="msg" :class="{ 'is-unread': !item.isRead }">
          <!-- 未读左侧色条：比"整行换背景色"克制，列表滚动时也不会花 -->
          <span class="msg-flag" />

          <div class="msg-icon" :class="`level-${item.level}`">
            <el-icon>
              <component :is="categoryIcon(item.category)" />
            </el-icon>
          </div>

          <div class="msg-body">
            <div class="msg-head">
              <span class="msg-title">{{ item.title }}</span>
              <el-tag v-if="!item.isRead" size="small" type="danger" effect="plain" round>未读</el-tag>
            </div>
            <div v-if="item.body" class="msg-text">{{ item.body }}</div>
            <div class="msg-meta">
              <span v-if="item.projectName" class="msg-project">{{ item.projectName }}</span>
              <span class="msg-cat">{{ categoryLabel(item.category) }}</span>
              <span class="msg-sep">·</span>
              <!-- 发送时间用绝对时间展示（原来只有相对时间，翻历史消息时看不出具体时刻） -->
              <span>发送时间: {{ formatFullDateTime(item.createdAt) }}</span>
              <template v-if="item.userName">
                <span class="msg-sep">·</span>
                <span>接收人: {{ item.userName }}</span>
              </template>
            </div>
          </div>

          <div class="msg-actions">
            <el-button v-if="item.linkUrl" link type="primary" @click="open(item)">
              {{ item.linkLabel || '查看详情' }}
            </el-button>
            <el-button v-if="!item.isRead" link type="info" @click="markRead(item)">标记已读</el-button>
            <el-popconfirm title="删除这条消息？" confirm-button-text="删除" width="220" @confirm="handleDelete(item.id)">
              <template #reference>
                <el-button link type="danger">删除</el-button>
              </template>
            </el-popconfirm>
          </div>
        </div>

        <el-empty v-if="!loading && items.length === 0" :description="emptyText" :image-size="80" />
      </div>

      <!-- 窄屏：三列布局（色条 / 图标 / 正文）在 375px 上会把正文挤成窄条，换成卡片 -->
      <MobileCardList v-else v-loading="loading" :items="items" :row-key="(row) => row.id" :empty-text="emptyText"
        class="msg-list-mobile">
        <template #title="{ item }">
          <span class="msg-title">{{ item.title }}</span>
        </template>
        <template #badge="{ item }">
          <el-tag size="small" effect="plain" type="info">{{ categoryLabel(item.category) }}</el-tag>
          <el-tag v-if="!item.isRead" size="small" type="danger" effect="plain" round>未读</el-tag>
        </template>
        <template #meta="{ item }">
          <!-- 卡片里正文交给 meta 自己的排版（12px 换行），不套列表那套两行截断 -->
          <span v-if="item.body">{{ item.body }}</span>
          <span><span class="mcl-label">发送时间</span>{{ formatFullDateTime(item.createdAt) }}</span>
          <span v-if="item.userName"><span class="mcl-label">接收人</span>{{ item.userName }}</span>
        </template>
        <template #actions="{ item }">
          <el-button v-if="item.linkUrl" link type="primary" @click="open(item)">
            {{ item.linkLabel || '查看详情' }}
          </el-button>
          <el-button v-if="!item.isRead" link type="info" @click="markRead(item)">标记已读</el-button>
          <el-popconfirm title="删除这条消息？" confirm-button-text="删除" width="220" @confirm="handleDelete(item.id)">
            <template #reference>
              <el-button link type="danger">删除</el-button>
            </template>
          </el-popconfirm>
        </template>
      </MobileCardList>

      <el-pagination v-model:current-page="page" :page-size="pageSize" :total="total" layout="total, prev, pager, next"
        class="pager" @current-change="load" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Refresh, Search, Select } from '@element-plus/icons-vue'
import { deleteNotification, getNotifications } from '@/api/notification'
import { getProjects } from '@/api/project'
import { useNotificationStore } from '@/stores/notification'
import { usePagedList } from '@/composables/usePagedList'
import { useBreakpoint } from '@/composables/useBreakpoint'
import MobileCardList from '@/components/common/MobileCardList.vue'
import { formatFullDateTime } from '@/utils/formatter'
import {
  NOTIFICATION_CATEGORY_LABELS, categoryIcon, categoryLabel, type NotificationItem,
} from '@/types/notification'

const router = useRouter()
const store = useNotificationStore()
const { isMobile } = useBreakpoint()

const category = ref<number | undefined>(undefined)
const projectId = ref<string | undefined>(undefined)
const title = ref<string | undefined>(undefined)
const dateRange = ref<string[] | null>(null)
const unreadOnly = ref(false)

/** 缓存的项目列表 — 筛选下拉 & 消息 meta 展示都用它 */
const projects = ref<{ id: string; name: string }[]>([])

const list = usePagedList<NotificationItem>((p, ps) => getNotifications({
  category: category.value,
  projectId: projectId.value,
  title: title.value?.trim() || undefined,
  dateFrom: dateRange.value?.[0],
  dateTo: dateRange.value?.[1],
  unreadOnly: unreadOnly.value || undefined,
  page: p,
  pageSize: ps,
}), { pageSize: 20 })
const { items, total, page, pageSize, loading } = toRefs(list)
const load = () => list.load()
const reload = () => list.load(1)

/** 有未读的分类，按未读数倒序——最该处理的排最前 */
const unreadChips = computed(() =>
  store.unread.byCategory
    .filter((c) => c.count > 0)
    .sort((a, b) => b.count - a.count)
    .map((c) => ({ value: c.category, count: c.count, label: categoryLabel(c.category) })),
)

/** 概览标签与工具栏下拉是同一个筛选项，点哪边都行 */
const pickCategory = (value: number | undefined) => {
  category.value = value
  reload()
}

const emptyText = computed(() => {
  if (unreadOnly.value) return '没有未读消息'
  if (category.value !== undefined) return `「${categoryLabel(category.value)}」下暂无消息`
  return '暂无消息'
})

const open = async (item: NotificationItem) => {
  if (!item.isRead) await markRead(item, false)
  if (item.linkUrl) void router.push(item.linkUrl)
}

/** 标记已读：列表里的对象就地更新，避免整页重拉（重拉会打断滚动位置） */
const markRead = async (item: NotificationItem, notify = true) => {
  if (item.isRead) return
  await store.markRead(item.id)
  item.isRead = true
  if (notify) ElMessage.success('已标记为已读')
  // 「仅看未读」模式下，这条已不再是未读，重载一次才与筛选语义一致
  if (unreadOnly.value) await reload()
}

const handleReadAll = async () => {
  await store.markAllRead(category.value)
  ElMessage.success('已全部标记为已读')
  reload()
}

const handleDelete = async (id: string) => {
  await deleteNotification(id)
  ElMessage.success('已删除')
  load()
  void store.refreshUnread()
}

onMounted(async () => {
  // 先拉项目列表（后续筛选和 meta 展示都依赖），再拉消息
  try {
    const res = await getProjects({ page: 1, pageSize: 100 })
    projects.value = (res.items ?? []).map((p: { id: string; name: string }) => ({ id: p.id, name: p.name }))
  }
  catch { /* 拉不到就空着，不阻塞消息列表 */ }
  load()
  void store.refreshUnread()
})
</script>

<style scoped>
/* 与缺陷/需求页同一套整页布局：概览卡固定，列表自适应撑满 */
.notification-page {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 56px - 32px);
  overflow: hidden;
}

.overview-card {
  flex-shrink: 0;
  margin-bottom: 12px;
}

.overview-card :deep(.el-card__body) {
  padding: 16px 20px;
}

.overview {
  display: flex;
  align-items: center;
  gap: 24px;
  flex-wrap: wrap;
}

.overview-main {
  flex-shrink: 0;
  min-width: 96px;
  padding-right: 24px;
  border-right: 1px solid var(--el-border-color-lighter);
}

.overview-value {
  font-size: 30px;
  font-weight: 600;
  line-height: 1.1;
  color: var(--el-color-danger);
}

/* 未读清零时把红色收掉：0 是个好消息，不该继续报警 */
.overview-main.zero .overview-value {
  color: var(--el-color-success);
}

.overview-label {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.overview-chips {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
}

.chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 5px 12px;
  border: 1px solid var(--el-border-color);
  border-radius: 999px;
  background-color: var(--el-fill-color-blank);
  color: var(--el-text-color-regular);
  font-size: 13px;
  line-height: 1.4;
  cursor: pointer;
  transition: all 0.18s ease;
}

.chip:hover {
  border-color: var(--el-color-primary-light-5);
  color: var(--el-color-primary);
}

.chip.active {
  border-color: var(--el-color-primary);
  background-color: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
  font-weight: 500;
}

.chip-count {
  padding: 0 6px;
  border-radius: 999px;
  background-color: var(--el-color-danger);
  color: #fff;
  font-size: 11px;
  line-height: 16px;
}

.overview-empty {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

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

.filter-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
  flex-shrink: 0;
  flex-wrap: wrap;
}
.w-120 {
  width: 120px;
}
.w-160 {
  width: 160px;
}

.w-220 {
  width: 220px;
}

/* el-date-picker 内部套了一层 el-input，
   在 flex 工具栏里只写 width 会被内部的 width:100% 撑开 → 三重锁死 */
.w-240 {
  width: 240px;
  max-width: 240px;
  flex: 0 0 240px;
}

.w-180 {
  width: 180px;
  max-width: 180px;
  flex: 0 0 180px;
}


/* 消息 meta 里的项目名 — 用 tag 风格高亮一下，让"这条消息属于哪个项目"一眼能扫到 */
.msg-project {
  padding: 1px 8px;
  border-radius: 4px;
  background-color: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
  font-size: 11px;
  line-height: 16px;
}

/* 刷新按钮推到工具栏最右 */
.toolbar-spacer {
  flex: 1;
}

.msg-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

.msg-list-mobile {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

.msg {
  position: relative;
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 14px 12px 14px 16px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  transition: background-color 0.18s ease;
}

.msg:hover {
  background-color: var(--el-fill-color-light);
}

.msg:last-child {
  border-bottom: none;
}

/* 未读色条：默认透明占位，未读时才着色——否则已读行会左右错位 */
.msg-flag {
  position: absolute;
  left: 0;
  top: 12px;
  bottom: 12px;
  width: 3px;
  border-radius: 0 2px 2px 0;
  background-color: transparent;
}

.msg.is-unread .msg-flag {
  background-color: var(--el-color-danger);
}

.msg-icon {
  flex-shrink: 0;
  width: 34px;
  height: 34px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  font-size: 17px;
  background-color: var(--el-fill-color-light);
  color: var(--el-text-color-secondary);
}

/* 级别只改图标配色，不改卡片底色 */
.msg-icon.level-0 {
  color: var(--el-text-color-secondary);
}

.msg-icon.level-1 {
  color: var(--el-color-success);
  background-color: var(--el-color-success-light-9);
}

.msg-icon.level-2 {
  color: var(--el-color-warning);
  background-color: var(--el-color-warning-light-9);
}

.msg-icon.level-3 {
  color: var(--el-color-danger);
  background-color: var(--el-color-danger-light-9);
}

.msg-body {
  flex: 1;
  min-width: 0;
}

.msg-head {
  display: flex;
  align-items: center;
  gap: 8px;
}

.msg-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--el-text-color-primary);
  word-break: break-word;
}

/* 已读的标题弱一档，让未读自然浮出来 */
.msg:not(.is-unread) .msg-title {
  font-weight: 400;
  color: var(--el-text-color-regular);
}

.msg-text {
  margin-top: 4px;
  font-size: 13px;
  line-height: 1.6;
  color: var(--el-text-color-secondary);
  /* 最多两行：一条消息占太高会让列表失去"扫一眼"的价值 */
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  word-break: break-word;
}

.msg-meta {
  margin-top: 6px;
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}

.msg-cat {
  color: var(--el-text-color-secondary);
}

.msg-actions {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 4px;
  padding-left: 8px;
}

.pager {
  margin-top: 12px;
  justify-content: flex-end;
  flex-shrink: 0;
}

@media (max-width: 1023px) {
  .notification-page {
    height: auto;
    overflow: visible;
  }

  .overview-main {
    padding-right: 16px;
  }

  .msg-actions {
    flex-direction: column;
    align-items: flex-end;
  }
}
</style>
