<template>
  <div class="app-sidebar">
    <div class="app-sidebar-logo">
      <!-- 收起后放不下六个字，换成缩写，避免文字被挤断 -->
      <span v-if="collapsed" class="logo-compact">AI</span>
      <span v-else>AI 测试平台</span>
    </div>

    <!-- 菜单已经 20 项，靠滚动找太慢。收起态放不下输入框，改成一个图标按钮：
         点它先展开侧栏、再把焦点送进输入框，少一步操作 -->
    <div v-if="!collapsed" class="app-sidebar-search">
      <el-input ref="searchInput" v-model="keyword" placeholder="搜索菜单" clearable
        :prefix-icon="Search" @keydown.esc="keyword = ''" @keydown.enter="jumpToFirstMatch" />
      <!-- 提示放在输入框下面而不是列表区：列表空了会是一片空白，看着像菜单坏了 -->
      <div v-if="keyword && filteredMenus.length === 0" class="search-empty">没有匹配的菜单</div>
    </div>
    <button v-else type="button" class="app-sidebar-search-icon" title="搜索菜单"
      aria-label="搜索菜单" @click="openSearch">
      <el-icon><Search /></el-icon>
    </button>

    <el-menu class="app-sidebar-menu" :collapse="collapsed" :default-active="activeMenu" router>
      <el-menu-item v-for="item in filteredMenus" :key="item.path" :index="item.path">
        <el-icon>
          <component :is="item.icon" />
        </el-icon>
        <!-- 菜单标题必须放在 #title 插槽里：折叠态下 Element Plus 靠它把文字藏起来、
             并在悬停时用 tooltip 浮出。用裸 <span> 的话折叠后标题既不隐藏也不提示 -->
        <template #title>{{ item.title }}</template>
      </el-menu-item>
    </el-menu>

    <!-- 收起 / 展开。放在底部而不是顶部：菜单项数量是变化的，底部不会被列表挤走。
         抽屉里没有「收起」这回事（抽屉本身就是要展开全部菜单），所以不渲染它 -->
    <button v-if="!isDrawer" type="button" class="app-sidebar-toggle" :class="{ 'is-collapsed': collapsed }"
      :title="collapsed ? '展开菜单' : '收起菜单'"
      :aria-label="collapsed ? '展开菜单' : '收起菜单'" @click="toggle">
      <el-icon>
        <component :is="collapsed ? Expand : Fold" />
      </el-icon>
      <span v-if="!collapsed">收起菜单</span>
    </button>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { InputInstance } from 'element-plus'
import {
  DataBoard, Folder, Document, VideoPlay, Timer, Collection, Grid, Picture,
  MagicStick, ChatDotRound, Connection, Setting, User, Lock, Memo, Calendar, WarningFilled,
  Flag, Monitor, Expand, Fold, Search,
} from '@element-plus/icons-vue'
import { useAuthStore } from '@/stores/auth'
import { Permission } from '@/constants/permissions'
import { useSidebarCollapse } from '@/composables/useSidebarCollapse'

/**
 * aside = 桌面固定侧栏（可收起）；drawer = 移动端抽屉里（永远展开）。
 *
 * 用一个 prop 而不是复制一份组件：菜单列表、权限过滤、菜单搜索加起来一百多行，
 * 复制两份迟早漏改（新增一个菜单只改其中一处）。
 */
const props = withDefaults(
  defineProps<{
    variant?: 'aside' | 'drawer'
  }>(),
  { variant: 'aside' },
)

const isDrawer = computed(() => props.variant === 'drawer')

const { collapsed: asideCollapsed, toggle } = useSidebarCollapse()

/**
 * 抽屉里永远按「展开」渲染：抽屉的存在就是为了看全菜单，
 * 再叠一层折叠态只会让 260px 宽的抽屉里出现一排光秃秃的图标。
 */
const collapsed = computed(() => !isDrawer.value && asideCollapsed.value)

interface MenuItem {
  path: string
  title: string
  icon: unknown
  /** 缺少该权限点的角色看不到这个入口 */
  permission: number
}

/**
 * 菜单即权限表的投影。
 *
 * 每个入口只声明「最少需要哪个权限点」，是否可见完全由 `auth.can()` 决定——
 * 这样新增角色时不需要回来改这个文件，只要在 PermissionCatalog 里配好矩阵即可。
 * 前端过滤只是「不给点不通的入口」，真正的拦截在后端。
 */
const menus: MenuItem[] = [
  { path: '/dashboard', title: '仪表盘', icon: DataBoard, permission: 0 },
  { path: '/projects', title: '项目管理', icon: Folder, permission: Permission.ViewProjects },
  { path: '/testcases', title: '测试用例', icon: Document, permission: Permission.ViewTestCases },
  { path: '/executions', title: '执行记录', icon: VideoPlay, permission: Permission.ViewExecutions },
  { path: '/nodes', title: '执行节点', icon: Monitor, permission: Permission.ViewExecutions },
  { path: '/schedules', title: '定时任务', icon: Timer, permission: Permission.ManageSchedules },
  { path: '/suites', title: '测试套件', icon: Collection, permission: Permission.ViewTestCases },
  { path: '/test-plans', title: '测试计划', icon: Calendar, permission: Permission.ViewTestPlans },
  { path: '/defects', title: '缺陷管理', icon: WarningFilled, permission: Permission.ViewTestCases },
  { path: '/requirements', title: '需求覆盖', icon: Flag, permission: Permission.ViewTestCases },
  { path: '/datasets', title: '数据集', icon: Grid, permission: Permission.ManageDataSets },
  { path: '/visual', title: '视觉基线', icon: Picture, permission: Permission.ManageBaselines },
  { path: '/shared-steps', title: '共享步骤', icon: Memo, permission: Permission.ManageSharedSteps },
  { path: '/recorder', title: '脚本录制', icon: VideoPlay, permission: Permission.ManageTestCases },
  { path: '/ai-generate', title: 'AI 生成用例', icon: MagicStick, permission: Permission.ManageTestCases },
  { path: '/chat', title: 'AI 聊天', icon: ChatDotRound, permission: Permission.ViewTestCases },
  { path: '/mocks', title: 'Mock 管理', icon: Connection, permission: Permission.ManageSettings },
  { path: '/audit', title: '审计日志', icon: Memo, permission: Permission.ViewAuditLog },
  { path: '/users', title: '用户管理', icon: User, permission: Permission.ManageUsers },
  { path: '/settings', title: '系统配置', icon: Setting, permission: Permission.ManageSettings },
]

const route = useRoute()
const auth = useAuthStore()

const visibleMenus = computed(() => menus.filter((item) => auth.can(item.permission)))

// ------------------------------------------------------------------ 菜单搜索

const router = useRouter()
const searchInput = ref<InputInstance | null>(null)
const keyword = ref('')

/**
 * 过滤**已按权限过滤过**的列表——不能拿全量 menus 去搜，
 * 否则只读账号会搜到点不通（或压根看不到）的入口。
 *
 * 标题和路径都参与匹配：这样 `缺陷` 和 `/defects`（或 defects）都能命中——
 * 路径是英文，习惯敲英文/记路径的人也能用。
 */
const filteredMenus = computed(() => {
  const k = keyword.value.trim().toLowerCase()
  if (!k) return visibleMenus.value
  return visibleMenus.value.filter(
    (item) => item.title.toLowerCase().includes(k) || item.path.toLowerCase().includes(k),
  )
})

/** 回车直接跳到第一个命中项（纯键盘操作，不用再去点） */
const jumpToFirstMatch = () => {
  const first = filteredMenus.value[0]
  if (first) router.push(first.path)
}

/** 收起态点搜索图标：先展开，等输入框渲染出来再聚焦 */
const openSearch = async () => {
  asideCollapsed.value = false
  await nextTick()
  searchInput.value?.focus()
}

/**
 * 高亮当前菜单。最长前缀优先——否则 `/testcases/xxx/edit` 会同时命中
 * `/testcases` 与将来可能的 `/testcases-archive` 之类的兄弟路径。
 */
const activeMenu = computed(() => {
  let best = ''
  for (const item of menus) {
    if (route.path.startsWith(item.path) && item.path.length > best.length) {
      best = item.path
    }
  }
  return best || route.path
})
</script>

<style scoped>
.app-sidebar {
  height: 100%;
  display: flex;
  flex-direction: column;
  /* 深色底由侧栏自己画，而不是交给外层容器。
     移动端的抽屉会被 teleport 到 body 外，MainLayout 的 scoped 样式够不到它，
     把背景放在这里，侧栏在「固定栏」和「抽屉」两种容器里观感一致 */
  background-color: #001529;
}

.app-sidebar-logo {
  flex-shrink: 0;
  height: 56px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-size: 18px;
  font-weight: 600;
  letter-spacing: 1px;
  white-space: nowrap;
  overflow: hidden;
}

.logo-compact {
  font-size: 20px;
  letter-spacing: 0;
}

/* ------------------------------------------------------------------ 菜单搜索 */
.app-sidebar-search {
  flex-shrink: 0;
  padding: 0 10px 8px;
}

/* 深色底上的输入框要单独配：默认那套白底在 #001529 上会亮得刺眼。
   用半透明白区分常态 / 悬停 / 聚焦三档，聚焦时加一圈主色描边 */
.app-sidebar-search :deep(.el-input__wrapper) {
  background-color: rgba(255, 255, 255, 0.08);
  box-shadow: none;
  border-radius: 8px;
}

.app-sidebar-search :deep(.el-input__wrapper:hover) {
  background-color: rgba(255, 255, 255, 0.12);
}

.app-sidebar-search :deep(.el-input__wrapper.is-focus) {
  background-color: rgba(255, 255, 255, 0.14);
  box-shadow: inset 0 0 0 1px #409eff;
}

.app-sidebar-search :deep(.el-input__inner) {
  color: #e6ebf2;
}

.app-sidebar-search :deep(.el-input__inner::placeholder) {
  color: #7b8695;
}

.app-sidebar-search :deep(.el-input__prefix),
.app-sidebar-search :deep(.el-input__suffix) {
  color: #8b94a0;
}

.search-empty {
  margin-top: 6px;
  padding-left: 2px;
  font-size: 12px;
  color: #8b94a0;
}

/* 收起态只有一个搜索图标，做成和底部收起按钮同样的内缩胶囊 */
.app-sidebar-search-icon {
  flex-shrink: 0;
  height: 36px;
  margin: 0 8px 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: #8b94a0;
  cursor: pointer;
  transition: background-color 0.18s ease, color 0.18s ease;
}

.app-sidebar-search-icon:hover {
  background-color: rgba(255, 255, 255, 0.08);
  color: #fff;
}

.app-sidebar-search-icon:focus-visible {
  outline: 2px solid #409eff;
  outline-offset: -2px;
}

/* 菜单占满剩余高度并自己滚动：菜单项在增加（现在已经 19 个），
   小屏上如果整体撑开，底部的收起按钮会被挤出可视区 */
/* 配色与尺寸统一走 Element Plus 的菜单变量，而不是组件的 props：
   props 会把值当成**内联自定义属性**写到根节点上，内联优先级高于样式表，
   想改悬停色就只能写 !important，越改越脏；集中到这里以后每个状态都能直接调。

   ⚠ 别动 `--el-menu-icon-width` 和 `--el-menu-base-level-padding`：
   `el-menu--collapse` 的自动宽度 = 图标宽 + 内边距 × 2（默认 24 + 20×2 = 64px），
   而 MainLayout 里收起宽度常量也写着 64 —— 改了这两项，菜单宽度和外框就对不上了。 */
.app-sidebar-menu {
  --el-menu-bg-color: #001529;
  --el-menu-text-color: #a6adb4;
  --el-menu-active-color: #ffffff;
  --el-menu-hover-bg-color: rgba(255, 255, 255, 0.08);
  --el-menu-hover-text-color: #ffffff;
  --el-menu-item-height: 42px;
  flex: 1;
  min-height: 0;
  overflow-x: hidden;
  overflow-y: auto;
  border-right: none;
}

/* 菜单项：圆角内缩，替代默认那种通栏灰块——通栏的看起来像表格行，不像菜单 */
.app-sidebar-menu :deep(.el-menu-item) {
  margin: 3px 8px;
  border-radius: 8px;
  /* 纵向菜单不需要横向菜单那条下边框 */
  border-bottom: none;
  transition: background-color 0.18s ease, color 0.18s ease;
}

/* 左侧强调条：悬停淡入一小段、选中实心。
   比"整块变色"克制，也不会跟文字抢视线 */
.app-sidebar-menu :deep(.el-menu-item)::before {
  content: '';
  position: absolute;
  left: 0;
  top: 50%;
  width: 3px;
  height: 0;
  opacity: 0;
  border-radius: 0 2px 2px 0;
  background-color: #409eff;
  transform: translateY(-50%);
  transition: height 0.18s ease, opacity 0.18s ease;
}

.app-sidebar-menu :deep(.el-menu-item:hover)::before {
  height: 14px;
  opacity: 0.55;
}

.app-sidebar-menu :deep(.el-menu-item.is-active)::before {
  height: 18px;
  opacity: 1;
}

/* 选中项底色比悬停再重一档，一眼能分清"鼠标在这"和"当前在这" */
.app-sidebar-menu :deep(.el-menu-item.is-active) {
  background-color: rgba(255, 255, 255, 0.1);
}

/* 折叠态：Element Plus 是**靠左内边距**把图标摆到正中的
   （左内边距 20 + 图标 24 = 64 的菜单宽），我加的内缩 margin 会让它整体偏右，
   所以这里显式改成 flex 居中。

   注意还有个坑：折叠态下 EP 会把内容包进一层 `.el-menu-tooltip__trigger`（做悬停提示用），
   **那层壳上带着 20px 内边距**——只在菜单项上清 padding 是不够的（实测图标仍偏右 6px）。
   展开态没有这层壳，所以下面的规则不会影响展开布局。 */
.app-sidebar-menu.el-menu--collapse :deep(.el-menu-item) {
  margin: 3px 6px;
  padding: 0;
  justify-content: center;
}

.app-sidebar-menu.el-menu--collapse :deep(.el-menu-tooltip__trigger) {
  padding: 0;
  justify-content: center;
}

/* 滚动条：深色底上不能用 table.css 那套浅色滚动条（#c9d8ea 压在 #001529 上太扎眼），
   改成半透明白、细一档，平时低调、悬停才明显。

   注意**不能直接写 `scrollbar-width`**：Chromium 121+ 一旦识别到它（值非 auto），
   就会改用标准属性并**忽略 `::-webkit-scrollbar`**，下面那套圆角/hover 会整体失效。
   所以标准属性只在"不支持 webkit 伪元素"的浏览器（Firefox）里生效。 */
@supports not selector(::-webkit-scrollbar) {
  .app-sidebar-menu {
    scrollbar-width: thin;
    scrollbar-color: rgba(255, 255, 255, 0.22) transparent;
  }
}

.app-sidebar-menu::-webkit-scrollbar {
  width: 6px;
}

/* 透明度是实测定下来的：0.14 在 #001529 上只有 Δ+36/通道，太容易看不见；
   0.22 是"平时能看见、但不抢注意力"的档位，悬停再提亮到 0.4。 */
.app-sidebar-menu::-webkit-scrollbar-thumb {
  background-color: rgba(255, 255, 255, 0.22);
  border-radius: 3px;
}

.app-sidebar-menu::-webkit-scrollbar-thumb:hover {
  background-color: rgba(255, 255, 255, 0.4);
}

.app-sidebar-menu::-webkit-scrollbar-track {
  background-color: transparent;
}

/* 菜单项没有滚动条时也不要留占位的白块 */
.app-sidebar-menu::-webkit-scrollbar-corner {
  background-color: transparent;
}

/* 收起 / 展开：内缩的胶囊按钮。
   原来是「通栏 + 一条硬分割线」，读起来像表格的表尾而不是一个可点的控件；
   内缩 + 圆角之后它才像个按钮，收起态下也自然变成一个居中的方钮。 */
.app-sidebar-toggle {
  position: relative;
  flex-shrink: 0;
  height: 36px;
  margin: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: #8b94a0;
  font-size: 13px;
  cursor: pointer;
  transition: background-color 0.18s ease, color 0.18s ease;
}

/* 分割线两端渐隐，比一条通栏实线轻——目的是"分组"而不是"切一刀" */
.app-sidebar-toggle::before {
  content: '';
  position: absolute;
  top: -8px;
  left: 4px;
  right: 4px;
  height: 1px;
  background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.16), transparent);
}

.app-sidebar-toggle:hover {
  background-color: rgba(255, 255, 255, 0.08);
  color: #fff;
}

.app-sidebar-toggle:active {
  background-color: rgba(255, 255, 255, 0.13);
}

/* 图标朝"将要发生的事"方向轻微位移：展开时向左收，收起时向右展。
   和 PageHeaderBar 返回按钮用的是同一套手感（0.18s + 位移 2px） */
.app-sidebar-toggle .el-icon {
  transition: transform 0.18s ease;
}

.app-sidebar-toggle:hover .el-icon {
  transform: translateX(-2px);
}

.app-sidebar-toggle.is-collapsed:hover .el-icon {
  transform: translateX(2px);
}

.app-sidebar-toggle:focus-visible {
  outline: 2px solid #409eff;
  outline-offset: -2px;
}
</style>
