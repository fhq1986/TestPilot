<template>
  <el-popover v-model:visible="visible" placement="bottom-end" :width="380" trigger="click"
    popper-class="notification-popper" @show="onShow">
    <template #reference>
      <button type="button" class="bell" :title="bellTitle" :aria-label="bellTitle">
        <el-badge :value="store.unreadTotal" :max="99" :hidden="store.unreadTotal === 0">
          <el-icon :size="18">
            <Bell />
          </el-icon>
        </el-badge>
      </button>
    </template>

    <div class="nc">
      <div class="nc-head">
        <span class="nc-title">消息</span>
        <div class="nc-head-actions">
          <el-button v-if="store.unreadTotal > 0" link type="primary" size="small" @click="handleReadAll">
            全部已读
          </el-button>
          <el-button link type="primary" size="small" @click="goAll">查看全部</el-button>
        </div>
      </div>

      <el-radio-group v-model="filter" size="small" class="nc-filter" @change="onShow">
        <el-radio-button :value="false">全部</el-radio-button>
        <el-radio-button :value="true">未读{{ store.unreadTotal > 0 ? `（${store.unreadTotal}）` : '' }}</el-radio-button>
      </el-radio-group>

      <div v-loading="store.loadingRecent" class="nc-list">
        <div v-for="item in store.recent" :key="item.id" class="nc-item" :class="{ unread: !item.isRead }"
          @click="open(item)">
          <el-icon class="nc-icon" :class="`level-${item.level}`">
            <component :is="iconOf(item.level)" />
          </el-icon>
          <div class="nc-body">
            <div class="nc-item-title">{{ item.title }}</div>
            <div v-if="item.body" class="nc-item-text">{{ item.body }}</div>
            <div class="nc-item-meta">
              <el-tag size="small" effect="plain" type="info">{{ categoryLabel(item.category) }}</el-tag>
              <span>{{ formatDateTime(item.createdAt) }}</span>
            </div>
          </div>
        </div>
        <el-empty v-if="!store.loadingRecent && store.recent.length === 0" :image-size="50"
          description="暂无消息" />
      </div>
    </div>
  </el-popover>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  Bell, CircleCheck, CircleClose, InfoFilled, WarningFilled,
} from '@element-plus/icons-vue'
import { useNotificationStore } from '@/stores/notification'
import { formatDateTime } from '@/utils/formatter'
import { NOTIFICATION_CATEGORY_LABELS, NotificationLevel, type NotificationItem } from '@/types/notification'

const router = useRouter()
const store = useNotificationStore()

const visible = ref(false)
const filter = ref(false)

const bellTitle = computed(() =>
  store.unreadTotal > 0 ? `${store.unreadTotal} 条未读消息` : '消息')

const categoryLabel = (category: number) => NOTIFICATION_CATEGORY_LABELS[category] ?? '消息'

/** 级别 → 图标：颜色交给 CSS 的 level-* 类，图标只表达语义 */
const iconOf = (level: number) => {
  switch (level) {
    case NotificationLevel.Success: return CircleCheck
    case NotificationLevel.Warning: return WarningFilled
    case NotificationLevel.Error: return CircleClose
    default: return InfoFilled
  }
}

const onShow = () => { void store.refreshRecent(filter.value) }

const open = async (item: NotificationItem) => {
  if (!item.isRead) {
    // 标记已读失败不该挡住跳转（角标下次轮询会自愈）
    void store.markRead(item.id).catch(() => { })
  }
  visible.value = false
  if (item.linkUrl) router.push(item.linkUrl)
}

const handleReadAll = async () => {
  await store.markAllRead()
  ElMessage.success('已全部标记为已读')
}

const goAll = () => {
  visible.value = false
  router.push('/notifications')
}
</script>

<style scoped>
/* 顶栏按钮：与汉堡按钮同一套手感（透明底 + hover 浅底） */
.bell {
  flex-shrink: 0;
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: var(--el-text-color-regular);
  cursor: pointer;
  transition: background-color 0.18s ease, color 0.18s ease;
}

.bell:hover {
  background-color: var(--el-fill-color-light);
  color: var(--el-color-primary);
}

.bell:focus-visible {
  outline: 2px solid var(--el-color-primary);
  outline-offset: -2px;
}

.nc-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.nc-title {
  font-size: 15px;
  font-weight: 600;
}

.nc-head-actions {
  display: flex;
  align-items: center;
  gap: 4px;
}

.nc-filter {
  margin: 8px 0;
}

/* 固定高度 + 内部滚动：消息条数不定，弹层不能跟着无限变高 */
.nc-list {
  max-height: 380px;
  overflow-y: auto;
  margin: 0 -4px;
}

.nc-item {
  display: flex;
  gap: 10px;
  padding: 10px 8px;
  border-radius: 6px;
  cursor: pointer;
  transition: background-color 0.18s ease;
}

.nc-item:hover {
  background-color: var(--el-fill-color-light);
}

.nc-item.unread {
  background-color: var(--el-color-primary-light-9);
}

.nc-icon {
  flex-shrink: 0;
  margin-top: 2px;
  font-size: 16px;
}

.nc-icon.level-0 { color: var(--el-text-color-secondary); }
.nc-icon.level-1 { color: var(--el-color-success); }
.nc-icon.level-2 { color: var(--el-color-warning); }
.nc-icon.level-3 { color: var(--el-color-danger); }

.nc-body {
  min-width: 0;
  flex: 1;
}

.nc-item-title {
  font-size: 13px;
  font-weight: 500;
  color: var(--el-text-color-primary);
  word-break: break-word;
}

.nc-item-text {
  margin-top: 2px;
  font-size: 12px;
  color: var(--el-text-color-regular);
  /* 正文最多两行：一条消息占太高的位置会让列表失去"扫一眼"的价值 */
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.nc-item-meta {
  margin-top: 6px;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
</style>
