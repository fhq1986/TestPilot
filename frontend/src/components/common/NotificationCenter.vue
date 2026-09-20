<template>
  <el-popover v-model:visible="visible" placement="bottom-end" :width="400" trigger="click"
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
        <span class="nc-title">
          消息
          <span v-if="store.unreadTotal > 0" class="nc-unread">{{ store.unreadTotal }} 条未读</span>
        </span>
        <el-button v-if="store.unreadTotal > 0" link type="primary" size="small" @click="handleReadAll">
          全部已读
        </el-button>
      </div>

      <el-radio-group v-model="filter" size="small" class="nc-filter" @change="onShow">
        <el-radio-button :value="false">全部</el-radio-button>
        <el-radio-button :value="true">未读</el-radio-button>
      </el-radio-group>

      <div v-loading="store.loadingRecent" class="nc-list">
        <div v-for="item in store.recent" :key="item.id" class="nc-item" :class="{ 'is-unread': !item.isRead }"
          @click="open(item)">
          <span class="nc-flag" />
          <div class="nc-icon" :class="`level-${item.level}`">
            <el-icon>
              <component :is="categoryIcon(item.category)" />
            </el-icon>
          </div>
          <div class="nc-body">
            <div class="nc-item-title">{{ item.title }}</div>
            <div v-if="item.body" class="nc-item-text">{{ item.body }}</div>
            <div class="nc-item-meta">
              <span>{{ categoryLabel(item.category) }}</span>
              <span class="nc-sep">·</span>
              <span>{{ formatRelativeTime(item.createdAt) }}</span>
            </div>
          </div>
        </div>

        <el-empty v-if="!store.loadingRecent && store.recent.length === 0" :image-size="56"
          :description="filter ? '没有未读消息' : '暂无消息'" />
      </div>

      <div class="nc-foot">
        <el-button link type="primary" @click="goAll">查看全部消息</el-button>
      </div>
    </div>
  </el-popover>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Bell } from '@element-plus/icons-vue'
import { useNotificationStore } from '@/stores/notification'
import { formatRelativeTime } from '@/utils/formatter'
import { categoryIcon, categoryLabel, type NotificationItem } from '@/types/notification'

const router = useRouter()
const store = useNotificationStore()

const visible = ref(false)
const filter = ref(false)

const bellTitle = computed(() =>
  store.unreadTotal > 0 ? `${store.unreadTotal} 条未读消息` : '消息')

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
  void store.refreshRecent(filter.value)
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
  display: inline-flex;
  align-items: baseline;
  gap: 8px;
  font-size: 15px;
  font-weight: 600;
}

.nc-unread {
  font-size: 12px;
  font-weight: 400;
  color: var(--el-color-danger);
}

.nc-filter {
  margin: 10px 0 8px;
}

/* 固定高度 + 内部滚动：消息条数不定，弹层不能跟着无限变高 */
.nc-list {
  max-height: 400px;
  overflow-y: auto;
  margin: 0 -6px;
}

.nc-item {
  position: relative;
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 10px 10px 10px 14px;
  border-radius: 8px;
  cursor: pointer;
  transition: background-color 0.18s ease;
}

.nc-item:hover {
  background-color: var(--el-fill-color-light);
}

/* 未读色条：与消息中心页同一套语言，跨页面认得出是同一件事 */
.nc-flag {
  position: absolute;
  left: 4px;
  top: 12px;
  bottom: 12px;
  width: 3px;
  border-radius: 2px;
  background-color: transparent;
}

.nc-item.is-unread .nc-flag {
  background-color: var(--el-color-danger);
}

.nc-icon {
  flex-shrink: 0;
  width: 30px;
  height: 30px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  font-size: 15px;
  background-color: var(--el-fill-color-light);
  color: var(--el-text-color-secondary);
}

.nc-icon.level-0 { color: var(--el-text-color-secondary); }
.nc-icon.level-1 { color: var(--el-color-success); background-color: var(--el-color-success-light-9); }
.nc-icon.level-2 { color: var(--el-color-warning); background-color: var(--el-color-warning-light-9); }
.nc-icon.level-3 { color: var(--el-color-danger); background-color: var(--el-color-danger-light-9); }

.nc-body {
  min-width: 0;
  flex: 1;
}

.nc-item-title {
  font-size: 13px;
  font-weight: 500;
  line-height: 1.5;
  color: var(--el-text-color-primary);
  word-break: break-word;
}

/* 已读的标题弱一档，未读自然浮出来 */
.nc-item:not(.is-unread) .nc-item-title {
  font-weight: 400;
  color: var(--el-text-color-regular);
}

.nc-item-text {
  margin-top: 2px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--el-text-color-secondary);
  /* 正文最多两行：弹层里一条占太高会挤掉后面几条 */
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  word-break: break-word;
}

.nc-item-meta {
  margin-top: 6px;
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}

.nc-sep {
  color: var(--el-border-color);
}

.nc-foot {
  margin-top: 6px;
  padding-top: 8px;
  border-top: 1px solid var(--el-border-color-lighter);
  text-align: center;
}
</style>
