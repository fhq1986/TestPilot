<template>
  <el-container class="main-layout">
    <!-- 桌面：固定侧栏（可收起）。窄屏下整条侧栏让位给下面的抽屉 -->
    <el-aside v-if="!isNarrow" :width="asideWidth" class="main-aside">
      <AppSidebar />
    </el-aside>

    <!-- 窄屏：菜单收进抽屉。不要 header —— 抽屉里直接就是侧栏本体，它自带 logo -->
    <el-drawer v-else :model-value="drawerOpen" direction="ltr" size="260px" :with-header="false"
      @update:model-value="setDrawerOpen">
      <AppSidebar variant="drawer" />
    </el-drawer>

    <el-container>
      <el-header height="56px" class="main-header">
        <AppHeader />
      </el-header>
      <el-main class="main-content">
        <router-view />
      </el-main>
    </el-container>
  </el-container>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import AppSidebar from '@/components/common/AppSidebar.vue'
import AppHeader from '@/components/common/AppHeader.vue'
import { SIDEBAR_WIDTH, useSidebarCollapse } from '@/composables/useSidebarCollapse'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { useMobileNav } from '@/composables/useMobileNav'
import { useNotificationStore } from '@/stores/notification'

const { collapsed } = useSidebarCollapse()
const { isNarrow } = useBreakpoint()
const { drawerOpen, setDrawerOpen, closeNav } = useMobileNav()
const route = useRoute()
const notificationStore = useNotificationStore()

/**
 * 站内消息的全局连接挂在这里：MainLayout 是「已登录」的唯一外壳，
 * 它一挂载就建立连接（覆盖登录后与刷新页面两种进入方式），
 * 一卸载（跳去登录页 / 登出）就断开，不必在 auth store 里手工配对。
 */
onMounted(() => { void notificationStore.connect() })
onBeforeUnmount(() => { notificationStore.disconnect() })

const asideWidth = computed(() =>
  `${collapsed.value ? SIDEBAR_WIDTH.collapsed : SIDEBAR_WIDTH.expanded}px`,
)

/** 选中菜单项后收起抽屉：不然内容已经跳走了，遮罩还盖在上面 */
watch(() => route.fullPath, closeNav)

/**
 * 从窄屏切回桌面时关掉抽屉。
 * 不关的话 `drawerOpen` 会一直停在 true，下次再变窄时抽屉会自己弹出来，像是误触。
 */
watch(isNarrow, (narrow) => {
  if (!narrow) closeNav()
})
</script>

<style scoped>
.main-layout {
  height: 100%;
}

.main-aside {
  background-color: #001529;
  /* 与 Element Plus 菜单折叠的动画时长对齐（--el-transition-duration = .3s），
     否则一边在动一边不动，看起来像"菜单先跳一下、外框再追上来" */
  transition: width 0.3s;
  overflow: hidden;
}

.main-header {
  background-color: #fff;
  border-bottom: 1px solid #e4e7ed;
  padding: 0 20px;
}

.main-content {
  background-color: #f5f7fa;
  padding: 16px 20px;
}

/* 窄屏收窄留白：桌面那 20px 在 375px 上等于吃掉 11% 的可用宽度 */
@media (max-width: 1023px) {
  .main-header {
    padding: 0 12px;
  }

  .main-content {
    padding: 12px;
  }
}

@media (max-width: 767px) {
  .main-header {
    padding: 0 10px;
  }

  .main-content {
    padding: 10px;
  }
}

/* 这里刻意不做 iPhone 安全区（env(safe-area-inset-*)）留白：
   那要求 index.html 的 viewport 打开 viewport-fit=cover，而开了之后
   横屏时内容会顶进刘海区，需要每一侧都补留白 —— 收益（本页没有贴底的固定栏）
   远小于风险，所以保持浏览器默认的内缩行为。 */
</style>
