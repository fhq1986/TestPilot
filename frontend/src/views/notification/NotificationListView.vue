<template>
  <div class="notification-page">
    <el-card shadow="never" class="list-card">
      <div class="filter-bar">
        <el-select v-model="category" placeholder="全部类型" clearable style="width: 160px" @change="reload">
          <el-option v-for="(label, value) in NOTIFICATION_CATEGORY_LABELS" :key="value" :label="label"
            :value="Number(value)" />
        </el-select>
        <el-checkbox v-model="unreadOnly" @change="reload">仅看未读</el-checkbox>
        <el-button type="primary" :icon="Search" @click="reload">查询</el-button>
        <el-button :disabled="store.unreadTotal === 0" @click="handleReadAll">全部已读</el-button>
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" @click="load()">刷新</el-button>
      </div>

      <div v-loading="loading" class="msg-list">
        <div v-for="item in items" :key="item.id" class="msg" :class="{ unread: !item.isRead }">
          <el-icon class="msg-icon" :class="`level-${item.level}`">
            <component :is="iconOf(item.level)" />
          </el-icon>
          <div class="msg-body">
            <div class="msg-title">{{ item.title }}</div>
            <div v-if="item.body" class="msg-text">{{ item.body }}</div>
            <div class="msg-meta">
              <el-tag size="small" effect="plain" type="info">
                {{ NOTIFICATION_CATEGORY_LABELS[item.category] ?? '消息' }}
              </el-tag>
              <span>{{ formatDateTime(item.createdAt) }}</span>
              <el-link v-if="item.linkUrl" type="primary" :underline="false" @click="open(item)">
                {{ item.linkLabel || '查看详情' }}
              </el-link>
              <el-link v-if="!item.isRead" type="info" :underline="false" @click="markRead(item)">
                标记已读
              </el-link>
            </div>
          </div>
          <el-popconfirm title="删除这条消息？" confirm-button-text="删除" @confirm="handleDelete(item.id)">
            <template #reference>
              <el-button size="small" text type="danger" class="msg-delete">删除</el-button>
            </template>
          </el-popconfirm>
        </div>
        <el-empty v-if="!loading && items.length === 0" description="暂无消息" :image-size="70" />
      </div>

      <el-pagination v-model:current-page="page" :page-size="pageSize" :total="total"
        layout="total, prev, pager, next" class="pager" @current-change="load" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  CircleCheck, CircleClose, InfoFilled, Refresh, Search, WarningFilled,
} from '@element-plus/icons-vue'
import { deleteNotification, getNotifications } from '@/api/notification'
import { useNotificationStore } from '@/stores/notification'
import { usePagedList } from '@/composables/usePagedList'
import { formatDateTime } from '@/utils/formatter'
import {
  NOTIFICATION_CATEGORY_LABELS, NotificationLevel, type NotificationItem,
} from '@/types/notification'

const router = useRouter()
const store = useNotificationStore()

const category = ref<number | undefined>(undefined)
const unreadOnly = ref(false)

const list = usePagedList<NotificationItem>((p, ps) => getNotifications({
  category: category.value,
  unreadOnly: unreadOnly.value || undefined,
  page: p,
  pageSize: ps,
}), { pageSize: 20 })
const { items, total, page, pageSize, loading } = toRefs(list)
const load = () => list.load()
const reload = () => list.load(1)

const iconOf = (level: number) => {
  switch (level) {
    case NotificationLevel.Success: return CircleCheck
    case NotificationLevel.Warning: return WarningFilled
    case NotificationLevel.Error: return CircleClose
    default: return InfoFilled
  }
}

const open = async (item: NotificationItem) => {
  if (!item.isRead) await markRead(item, false)
  if (item.linkUrl) void router.push(item.linkUrl)
}

/** 标记已读：列表里的对象同时更新，避免整页重拉 */
const markRead = async (item: NotificationItem, notify = true) => {
  if (item.isRead) return
  await store.markRead(item.id)
  item.isRead = true
  if (notify) ElMessage.success('已标记为已读')
}

const handleReadAll = async () => {
  await store.markAllRead()
  ElMessage.success('已全部标记为已读')
  reload()
}

const handleDelete = async (id: string) => {
  await deleteNotification(id)
  ElMessage.success('已删除')
  load()
  void store.refreshUnread()
}

onMounted(() => {
  load()
  void store.refreshUnread()
})
</script>

<style scoped>
.notification-page {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.list-card {
  flex: 1;
  min-height: 0;
}

.list-card :deep(.el-card__body) {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.filter-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}

.toolbar-spacer {
  flex: 1;
}

.msg-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

.msg {
  display: flex;
  gap: 10px;
  padding: 12px 10px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.msg.unread {
  background-color: var(--el-color-primary-light-9);
}

.msg-icon {
  flex-shrink: 0;
  margin-top: 2px;
  font-size: 16px;
}

.msg-icon.level-0 { color: var(--el-text-color-secondary); }
.msg-icon.level-1 { color: var(--el-color-success); }
.msg-icon.level-2 { color: var(--el-color-warning); }
.msg-icon.level-3 { color: var(--el-color-danger); }

.msg-body {
  flex: 1;
  min-width: 0;
}

.msg-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--el-text-color-primary);
  word-break: break-word;
}

.msg-text {
  margin-top: 4px;
  font-size: 13px;
  color: var(--el-text-color-regular);
  word-break: break-word;
}

.msg-meta {
  margin-top: 8px;
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.msg-delete {
  flex-shrink: 0;
  align-self: flex-start;
}

.pager {
  margin-top: 12px;
  justify-content: flex-end;
}
</style>
