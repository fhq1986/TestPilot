import axios, { AxiosError } from 'axios'
import { ElMessage } from 'element-plus'

export function getAuthToken(): string {
  return localStorage.getItem('auth_token') ?? ''
}

const request = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || '/api',
  timeout: 30000,
})

request.interceptors.request.use((config) => {
  const token = getAuthToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

interface ApiErrorBody {
  message?: string
  title?: string
  /** 403 由 PermissionFilter 返回，标明缺少哪个权限点 */
  required?: string
  /** 400 校验错误（ASP.NET ValidationProblem 的形状） */
  errors?: Record<string, string[]>
}

/**
 * 把响应体翻译成一句用户能懂的话。
 *
 * 后端只有在业务主动构造响应时才会带 message，框架自动产生的响应（429 限流、
 * 400 校验、500 未处理异常）没有这个字段——直接用 axios 的 error.message 会显示
 * 「Request failed with status code 429」这种给不了任何行动指引的英文。
 */
function describe(error: AxiosError<ApiErrorBody>): string {
  const status = error.response?.status
  const body = error.response?.data

  if (body?.message) return body.message

  // 模型校验错误：把字段级提示拼出来，比「One or more validation errors occurred」有用得多
  if (body?.errors) {
    const details = Object.values(body.errors).flat().filter(Boolean)
    if (details.length > 0) return details.join('；')
  }
  if (body?.title && body.title !== 'One or more validation errors occurred.') return body.title

  switch (status) {
    case 400:
      return '请求参数有误，请检查后重试'
    case 401:
      return '登录状态已失效，请重新登录'
    case 403:
      return body?.required
        ? `当前账号缺少「${body.required}」权限`
        : '当前账号无权执行此操作'
    case 404:
      return '请求的资源不存在，可能已被删除'
    case 408:
      return '请求超时，请稍后重试'
    case 409:
      return '操作冲突，请刷新后重试'
    case 413:
      return '提交内容过大，请减少数据量后重试'
    case 429:
      // 全局限流是按「用户 / IP」分区的，共享出口 IP 的办公网会撞到别人的额度，
      // 说清楚这一点能避免用户误以为是系统坏了
      return '操作过于频繁，已触发限流，请稍候几秒再试'
    case 500:
    case 502:
    case 503:
    case 504:
      return '服务暂时不可用，请稍后重试（若持续出现请联系管理员）'
    default:
      if (!error.response) return '网络连接失败，请检查网络或服务是否已启动'
      return '请求失败，请稍后重试'
  }
}

/**
 * 401 跳转的单飞保护：并发请求同时拿到 401 时，只做一次「清 token + 跳登录 + 弹提示」。
 * 跳转会整页刷新，模块状态自然复位，无需手动还原。
 */
let redirecting = false

request.interceptors.response.use(
  (response) => response.data,
  (error: AxiosError<ApiErrorBody>) => {
    const status = error.response?.status
    const url = error.config?.url ?? ''

    // 登录接口的 401 是「用户名或密码错误」的正常业务响应（登录前也不存在可失效的会话）：
    // 不弹全局 toast、不跳转，由 LoginView 自己提示。之前这里会先弹一条误导性的
    // 「登录状态已失效」，再叠加登录页自己的「用户名或密码错误」（双重 toast，审查发现）
    const isLoginRequest = url.includes('/auth/login')

    if (status === 401 && !isLoginRequest) {
      // 并发 401 只处理一次；redirect 让登录成功后回到原页面（LoginView 已消费该参数）
      if (!redirecting) {
        redirecting = true
        localStorage.removeItem('auth_token')
        localStorage.removeItem('auth_user')
        // 之前用裸 window.location.href = '/login' 会丢失当前页面上下文（审查发现）
        const target = encodeURIComponent(window.location.pathname + window.location.search)
        ElMessage.error(describe(error))
        window.location.href = `/login?redirect=${target}`
      }
      return Promise.reject(error)
    }

    // 限流不是错误操作，用 warning 呈现，避免用户以为出了问题
    const text = describe(error)
    if (status === 429) ElMessage.warning(text)
    else ElMessage.error(text)
    return Promise.reject(error)
  },
)

export default request
