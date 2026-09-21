import { defineStore } from 'pinia'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import {
  getNotificationUnread, getNotifications, readAllNotifications, readNotification,
} from '@/api/notification'
import type { NotificationItem, NotificationUnread } from '@/types/notification'

/** SignalR 断线时的兜底轮询间隔：与执行详情页同一思路（推送不可用也不能让角标停更） */
const POLL_INTERVAL_MS = 60_000

/**
 * 站内消息状态 + **全局单例** SignalR 连接。
 *
 * 为什么不复用 `useSignalR`：那个 composable 在 `onScopeDispose` 里断开连接，
 * 是「组件在页面时才连」的语义。消息要的是「登录期间一直在连」——
 * 用户可能停在任何页面，铃铛角标都得实时更新，所以连接的生命周期挂在 store 上
 * （登录后 connect、登出时 disconnect），而不是挂在某个组件上。
 */
export const useNotificationStore = defineStore('notification', {
  state: () => ({
    /** 未读分类计数（由后端 /unread-count 给出，实时消息到达时本地自增） */
    unread: { total: 0, byCategory: [] } as NotificationUnread,
    /** 最近一页消息（弹层用；消息中心页面自己分页查询，不共用这份） */
    recent: [] as NotificationItem[],
    loadingRecent: false,
    connection: null as HubConnection | null,
    connected: false,
    pollTimer: 0 as ReturnType<typeof setInterval> | 0,
  }),
  getters: {
    unreadTotal: (state) => state.unread.total,
    /** 分类 → 未读数，弹层页签上显示 */
    unreadByCategory: (state) => {
      const map: Record<number, number> = {}
      for (const item of state.unread.byCategory) map[item.category] = item.count
      return map
    },
  },
  actions: {
    /** 拉一次未读数（SignalR 不可用时的轮询也走它） */
    async refreshUnread() {
      try {
        this.unread = await getNotificationUnread()
      } catch {
        // 未读数拉不到不该弹错：它是辅助信息，页面本身仍可用
      }
    },

    /** 拉最近一页消息（打开铃铛弹层时调用） */
    async refreshRecent(unreadOnly = false) {
      this.loadingRecent = true
      try {
        const res = await getNotifications({ unreadOnly, page: 1, pageSize: 20 })
        this.recent = res.items
      } finally {
        this.loadingRecent = false
      }
    },

    async markRead(id: string) {
      const target = this.recent.find((n) => n.id === id)
      if (target && !target.isRead) {
        target.isRead = true
        // 本地先减，避免等一次往返才更新角标
        this.unread.total = Math.max(0, this.unread.total - 1)
        const bucket = this.unread.byCategory.find((c) => c.category === target.category)
        if (bucket) bucket.count = Math.max(0, bucket.count - 1)
      }
      await readNotification(id)
      // 关键：再拉一次**权威**未读数。消息中心页面的列表与弹层的 recent 不共用，
      // 若被标记的消息不在 recent 里，上面的本地自减就不会发生——
      // 表现为"标了已读但未读统计不动"（真实 bug）。以服务端为准最稳。
      await this.refreshUnread()
    },

    async markAllRead(category?: number) {
      await readAllNotifications(category)
      if (category === undefined) {
        this.unread = { total: 0, byCategory: [] }
        this.recent = this.recent.map((n) => ({ ...n, isRead: true }))
      } else {
        const removed = this.unread.byCategory.find((c) => c.category === category)?.count ?? 0
        this.unread.total = Math.max(0, this.unread.total - removed)
        this.unread.byCategory = this.unread.byCategory.filter((c) => c.category !== category)
        this.recent = this.recent.map((n) => (n.category === category ? { ...n, isRead: true } : n))
      }
      // 同样以服务端为准（本轮 superadmin 可能作用于全部用户，本地推算会不准）
      await this.refreshUnread()
    },

    /**
     * 建立全局连接。登录成功后调用（见 MainLayout 挂载）；
     * 重复调用是幂等的——已有连接直接返回。
     */
    async connect() {
      if (this.connection) return
      await this.refreshUnread()

      try {
        const conn = new HubConnectionBuilder()
          .withUrl('/hubs/notification', {
            accessTokenFactory: () => localStorage.getItem('auth_token') ?? '',
          })
          .withAutomaticReconnect()
          .configureLogging(LogLevel.Warning)
          .build()

        // 新消息：插到列表头部并自增角标，不必再拉一次接口
        conn.on('NotificationReceived', (payload: NotificationItem) => {
          this.recent = [payload, ...this.recent].slice(0, 20)
          this.unread.total += 1
          const bucket = this.unread.byCategory.find((c) => c.category === payload.category)
          if (bucket) bucket.count += 1
          else this.unread.byCategory = [...this.unread.byCategory, { category: payload.category, count: 1 }]
        })

        // 重连后组关系会丢，必须重新加入自己的消息组
        conn.onreconnected(() => {
          this.connected = true
          void conn.invoke('JoinUserGroup').catch(() => { })
          void this.refreshUnread()
        })
        conn.onreconnecting(() => { this.connected = false })

        await conn.start()
        await conn.invoke('JoinUserGroup')
        this.connection = conn
        this.connected = true
        this.stopPolling()
      } catch {
        // 后端没起来 / 代理挡了 WebSocket：退回轮询，功能降级但不断
        this.connection = null
        this.connected = false
        this.startPolling()
      }
    },

    disconnect() {
      this.stopPolling()
      const conn = this.connection
      this.connection = null
      this.connected = false
      this.unread = { total: 0, byCategory: [] }
      this.recent = []
      if (conn) void conn.stop().catch(() => { })
    },

    startPolling() {
      if (this.pollTimer) return
      this.pollTimer = setInterval(() => { void this.refreshUnread() }, POLL_INTERVAL_MS)
    },

    stopPolling() {
      if (this.pollTimer) {
        clearInterval(this.pollTimer)
        this.pollTimer = 0
      }
    },
  },
})
