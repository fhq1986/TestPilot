<template>
  <div class="app-header">
    <div class="app-header-left">
      <!-- 窄屏下侧栏被隐藏，菜单入口收到这里（抽屉由 MainLayout 渲染） -->
      <button v-if="isNarrow" type="button" class="app-header-nav" title="打开菜单" aria-label="打开菜单"
        @click="openNav">
        <el-icon>
          <Menu />
        </el-icon>
      </button>

      <!-- 面包屑：平台名 》 上级模块 》 当前页面 -->
      <nav class="app-header-crumbs">
        <!-- 窄屏只留当前页名。完整面包屑在 375px 上会占满整行还被截断，
             而用户此时最需要知道的只有"我在哪一页" -->
        <template v-if="isNarrow">
          <span class="crumb crumb-current">{{ pageTitle || PLATFORM_NAME }}</span>
        </template>
        <template v-else>
          <router-link to="/dashboard" class="crumb crumb-brand">{{ PLATFORM_NAME }}</router-link>
          <template v-for="item in parents" :key="item.to">
            <span class="crumb-sep">》</span>
            <router-link :to="item.to" class="crumb crumb-link">{{ item.title }}</router-link>
          </template>
          <template v-if="pageTitle">
            <span class="crumb-sep">》</span>
            <span class="crumb crumb-current">{{ pageTitle }}</span>
          </template>
        </template>
      </nav>
    </div>

    <div class="app-header-right">
      <NotificationCenter />
      <el-dropdown @command="handleCommand">
        <span class="app-header-user">
          <!-- 狭窄的头部放不下角色标签，手机上先省掉它：角色在个人中心仍可查 -->
          <el-tag v-if="authStore.roleName && !isNarrow" :type="roleTagType" size="small" effect="light"
            class="role-tag">
            {{ authStore.roleName }}
          </el-tag>
          <span class="user-name">{{ displayName }}</span>
          <el-icon>
            <ArrowDown />
          </el-icon>
        </span>
        <template #dropdown>
          <el-dropdown-menu>
            <el-dropdown-item command="profile">个人中心</el-dropdown-item>
            <el-dropdown-item command="logout" divided>退出登录</el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { ArrowDown, Menu } from '@element-plus/icons-vue'
import NotificationCenter from '@/components/common/NotificationCenter.vue'
import { useAuthStore } from '@/stores/auth'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { useMobileNav } from '@/composables/useMobileNav'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const { isNarrow } = useBreakpoint()
const { openNav } = useMobileNav()

/** 与 router/index.ts 里写进 document.title 的名字保持一致 */
const PLATFORM_NAME = 'AI 自动化测试平台'

/** 详情/编辑类页面补充上级模块，形成层级面包屑 */
const PARENT_MAP: Record<string, { title: string; to: string }[]> = {
  'project-detail': [{ title: '项目管理', to: '/projects' }],
  'testcase-detail': [{ title: '测试用例', to: '/testcases' }],
  'testcase-edit': [{ title: '测试用例', to: '/testcases' }],
  'execution-detail': [{ title: '执行记录', to: '/executions' }],
}

const pageTitle = computed(() => (route.meta?.title as string) ?? '')
const parents = computed(() => PARENT_MAP[String(route.name ?? '')] ?? [])

const displayName = computed(() => authStore.user?.displayName || authStore.user?.username || '')

/** 角色标签配色：管理员红、测试工程师蓝、只读访客灰 */
const roleTagType = computed(() => {
  if (authStore.isAdmin) return 'danger'
  return authStore.role === 1 ? 'primary' : 'info'
})

const handleCommand = (command: string) => {
  if (command === 'logout') {
    authStore.logout()
    ElMessage.success('已退出登录')
    router.push('/login')
    return
  }
  if (command === 'profile') {
    router.push({ name: 'profile' })
  }
}
</script>

<style scoped>
.app-header {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.app-header-left {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

/* 汉堡按钮：与侧栏底部那个「收起」按钮同一套手感（圆角胶囊 + 0.18s 过渡） */
.app-header-nav {
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
  font-size: 18px;
  cursor: pointer;
  transition: background-color 0.18s ease, color 0.18s ease;
}

.app-header-nav:hover {
  background-color: var(--el-fill-color-light);
  color: var(--el-color-primary);
}

.app-header-nav:focus-visible {
  outline: 2px solid var(--el-color-primary);
  outline-offset: -2px;
}

.app-header-crumbs {
  display: flex;
  align-items: center;
  min-width: 0;
  font-size: 15px;
  line-height: 1;
}

.crumb {
  white-space: nowrap;
}

.crumb-brand {
  font-weight: 600;
  color: var(--el-text-color-primary);
  text-decoration: none;
}

.crumb-brand:hover {
  color: var(--el-color-primary);
}

.crumb-sep {
  margin: 0 8px;
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}

.crumb-link {
  color: var(--el-text-color-regular);
  text-decoration: none;
}

.crumb-link:hover {
  color: var(--el-color-primary);
}

.crumb-current {
  font-weight: 600;
  color: var(--el-color-primary);
  overflow: hidden;
  text-overflow: ellipsis;
}

.app-header-right {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}

.app-header-user {
  display: flex;
  align-items: center;
  gap: 4px;
  cursor: pointer;
  outline: none;
  min-width: 0;
}

/* 用户名可能很长（中文姓名 + 英文 ID）：让它自己截断，别把头部撑出横向滚动 */
.user-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.role-tag {
  margin-right: 4px;
  flex-shrink: 0;
}

@media (max-width: 767px) {
  .app-header {
    gap: 8px;
  }

  .app-header-user {
    max-width: 40vw;
  }
}
</style>
