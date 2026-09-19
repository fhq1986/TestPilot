import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { ElMessage } from 'element-plus'
import 'element-plus/dist/index.css'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import * as ElementPlusIconsVue from '@element-plus/icons-vue'
import App from './App.vue'
import router from './router'
import { registerPermissionDirective } from './directives/permission'
import { useAuthStore } from './stores/auth'
import './styles/index.css'
import './styles/table.css'
// 移动端适配层（断点与 Element Plus 组件的窄屏覆盖）。
// 必须排在 table.css 之后：它要用「同特异性、后加载者胜」来覆盖元素自身的样式
import './styles/responsive.css'

const app = createApp(App)

// 全局错误处理：渲染/生命周期异常不再静默白屏
app.config.errorHandler = (err, _instance, info) => {
  console.error('全局错误:', err, '触发点:', info)
  ElMessage.error('页面发生错误，请刷新重试')
}

// 未捕获的 Promise 异常
window.addEventListener('unhandledrejection', (event) => {
  console.error('未处理的 Promise 异常:', event.reason)
})

app.use(createPinia())
app.use(router)
app.use(ElementPlus, { locale: zhCn })

// v-permission：无权限时移除元素（仅界面层过滤，真正拦截在后端）
registerPermissionDirective(app)

for (const [key, component] of Object.entries(ElementPlusIconsVue)) {
  app.component(key, component)
}

app.mount('#app')

/**
 * 启动后向服务端核对一次权限。
 *
 * localStorage 里的 `auth_user` 是**登录当时**的快照。平台新增权限点后，
 * 老快照里没有对应的位，`can()` 会判为无权限 —— 于是新加的菜单对已登录用户直接消失，
 * 而且因为路由守卫只在「本地没有 user」时才拉取，这个状态**永远不会自愈**，
 * 用户只能靠退出重登。
 *
 * 这里刻意放在 mount 之后且不 await：菜单是权限的响应式投影，
 * 刷新回来会自动补上，不必为了它阻塞首屏（本地接口也就几十毫秒）。
 */
void useAuthStore().refreshMe()
