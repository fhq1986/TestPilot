import { createRouter, createWebHistory } from 'vue-router'
import MainLayout from '@/layouts/MainLayout.vue'
import { useAuthStore } from '@/stores/auth'
import { Permission } from '@/constants/permissions'

/**
 * 路由权限：`meta.permission` 声明进入该页面所需的权限点（缺省表示「登录即可」）。
 *
 * 守卫为什么异步：刷新页面后 Pinia 里没有 user（只有 localStorage 的 token），
 * 需要先 `refreshMe()` 拿到服务端权限再判断，否则会误判成无权限。
 */
const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      // 免登录只读报告：分享链接即凭证，故标记 public
      path: '/share/:token',
      name: 'share-report',
      component: () => import('@/views/share/ShareReportView.vue'),
      meta: { title: '测试报告', public: true },
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/login/LoginView.vue'),
      meta: { title: '登录', public: true },
    },
    {
      // SSO 企业授权回调页：public（登录发生前），无需权限
      path: '/login/sso',
      name: 'sso-callback',
      component: () => import('@/views/login/SsoCallbackView.vue'),
      meta: { title: 'SSO 登录', public: true },
    },
    {
      // SSO 绑定回调页：已登录用户从个人中心发起「绑定企业身份」后落这里。
      // 需要登录态（bind 接口带 JWT），所以不标 public——未登录会被守卫先送去登录页
      path: '/login/sso/bind',
      name: 'sso-bind-callback',
      component: () => import('@/views/login/SsoCallbackView.vue'),
      meta: { title: '绑定企业身份' },
    },
    {
      path: '/403',
      name: 'forbidden',
      component: () => import('@/views/common/ForbiddenView.vue'),
      meta: { title: '无权访问' },
    },
    {
      path: '/',
      component: MainLayout,
      redirect: '/dashboard',
      children: [
        {
          path: 'dashboard',
          name: 'dashboard',
          component: () => import('@/views/dashboard/DashboardView.vue'),
          meta: { title: '仪表盘' },
        },
        {
          path: 'projects',
          name: 'projects',
          component: () => import('@/views/project/ProjectListView.vue'),
          meta: { title: '项目管理', permission: Permission.ViewProjects },
        },
        {
          path: 'projects/:id',
          name: 'project-detail',
          component: () => import('@/views/project/ProjectDetailView.vue'),
          meta: { title: '项目详情', permission: Permission.ViewProjects },
        },
        {
          path: 'testcases/:id',
          name: 'testcase-detail',
          component: () => import('@/views/testcase/TestCaseDetailView.vue'),
          meta: { title: '用例详情', permission: Permission.ViewTestCases },
        },
        {
          path: 'testcases/:id/edit',
          name: 'testcase-edit',
          component: () => import('@/views/testcase/TestCaseEditView.vue'),
          meta: { title: '用例编辑', permission: Permission.ManageTestCases },
        },
        {
          path: 'testcases',
          name: 'testcases',
          component: () => import('@/views/testcase/TestCaseListView.vue'),
          meta: { title: '测试用例', permission: Permission.ViewTestCases },
        },
        {
          path: 'executions',
          name: 'executions',
          component: () => import('@/views/execution/ExecutionListView.vue'),
          meta: { title: '执行记录', permission: Permission.ViewExecutions },
        },
        {
          path: 'nodes',
          name: 'nodes',
          component: () => import('@/views/node/NodeListView.vue'),
          meta: { title: '执行节点', permission: Permission.ViewExecutions },
        },
        {
          path: 'executions/:id',
          name: 'execution-detail',
          component: () => import('@/views/execution/ExecutionDetailView.vue'),
          meta: { title: '执行详情', permission: Permission.ViewExecutions },
        },
        {
          path: 'schedules',
          name: 'schedules',
          component: () => import('@/views/schedule/ScheduleListView.vue'),
          meta: { title: '定时任务', permission: Permission.ManageSchedules },
        },
        {
          path: 'suites',
          name: 'suites',
          component: () => import('@/views/suite/SuiteListView.vue'),
          meta: { title: '测试套件', permission: Permission.ViewTestCases },
        },
        {
          path: 'test-plans',
          name: 'test-plans',
          component: () => import('@/views/testplan/TestPlanListView.vue'),
          meta: { title: '测试计划', permission: Permission.ViewTestPlans },
        },
        {
          path: 'defects',
          name: 'defects',
          component: () => import('@/views/defect/DefectListView.vue'),
          meta: { title: '缺陷管理', permission: Permission.ViewTestCases },
        },
        {
          path: 'requirements',
          name: 'requirements',
          component: () => import('@/views/requirement/RequirementListView.vue'),
          meta: { title: '需求覆盖', permission: Permission.ViewTestCases },
        },
        {
          path: 'test-plans/:id',
          name: 'test-plan-detail',
          component: () => import('@/views/testplan/TestPlanDetailView.vue'),
          meta: { title: '计划详情', permission: Permission.ViewTestPlans },
        },
        {
          path: 'datasets',
          name: 'datasets',
          component: () => import('@/views/dataset/DataSetListView.vue'),
          meta: { title: '数据集', permission: Permission.ManageDataSets },
        },
        {
          path: 'visual',
          name: 'visual',
          component: () => import('@/views/visual/VisualBaselineView.vue'),
          meta: { title: '视觉基线', permission: Permission.ManageBaselines },
        },
        {
          path: 'shared-steps',
          name: 'shared-steps',
          component: () => import('@/views/sharedstep/SharedStepListView.vue'),
          meta: { title: '共享步骤', permission: Permission.ManageSharedSteps },
        },
        {
          path: 'recorder',
          name: 'recorder',
          component: () => import('@/views/recorder/RecorderView.vue'),
          meta: { title: '脚本录制', permission: Permission.ManageTestCases },
        },
        {
          path: 'ai-generate',
          name: 'ai-generate',
          component: () => import('@/views/ai-center/AIGenerateView.vue'),
          meta: { title: 'AI 生成用例', permission: Permission.ManageTestCases },
        },
        {
          path: 'chat',
          name: 'chat',
          component: () => import('@/views/chat/ChatView.vue'),
          meta: { title: 'AI 聊天', permission: Permission.ViewTestCases },
        },
        {
          path: 'mocks',
          name: 'mocks',
          component: () => import('@/views/mock/MockListView.vue'),
          meta: { title: 'Mock 管理', permission: Permission.ManageSettings },
        },
        {
          path: 'audit',
          name: 'audit',
          component: () => import('@/views/audit/AuditLogView.vue'),
          meta: { title: '审计日志', permission: Permission.ViewAuditLog },
        },
        {
          path: 'users',
          name: 'users',
          component: () => import('@/views/user/UserListView.vue'),
          meta: { title: '用户管理', permission: Permission.ManageUsers },
        },
        {
          path: 'profile',
          name: 'profile',
          component: () => import('@/views/user/ProfileView.vue'),
          meta: { title: '个人中心' },
        },
        {
          // 消息中心：入口是顶栏铃铛，任何登录用户都能看自己的消息，不挂权限
          path: 'notifications',
          name: 'notifications',
          component: () => import('@/views/notification/NotificationListView.vue'),
          meta: { title: '消息中心' },
        },
        {
          path: 'settings',
          name: 'settings',
          component: () => import('@/views/settings/SettingsView.vue'),
          meta: { title: '系统配置', permission: Permission.ManageSettings },
        },
      ],
    },
    { path: '/:pathMatch(.*)*', redirect: '/dashboard' },
  ],
})

router.beforeEach(async (to) => {
  const token = localStorage.getItem('auth_token')
  if (!to.meta.public && !token) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  if (to.name === 'login' && token) {
    return { path: '/' }
  }

  const required = to.meta.permission as number | undefined
  if (!required) return

  const auth = useAuthStore()
  // 刷新页面后 store 里还没有用户信息 → 先补齐再判权限，避免把有权限的人挡在门外
  if (!auth.user) {
    await auth.refreshMe()
    if (!auth.token) {
      return { name: 'login', query: { redirect: to.fullPath } }
    }
  }

  if (!auth.can(required)) {
    return { name: 'forbidden', query: { from: to.fullPath } }
  }
})

// 浏览器标签标题：「当前页面 - AI 自动化测试平台」
router.afterEach((to) => {
  const title = to.meta?.title as string | undefined
  document.title = title ? `${title} - AI 自动化测试平台` : 'AI 自动化测试平台'
})

export default router
