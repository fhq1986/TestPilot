import { onScopeDispose, ref } from 'vue'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'

export function useSignalR(hubUrl: string) {
  const connection = ref<HubConnection | null>(null)
  const isConnected = ref(false)

  const start = async () => {
    const builder = new HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: () => localStorage.getItem('auth_token') ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)

    connection.value = builder.build()

    connection.value.onreconnecting(() => {
      isConnected.value = false
    })

    connection.value.onreconnected(() => {
      isConnected.value = true
    })

    await connection.value.start()
    isConnected.value = true
  }

  const stop = async () => {
    if (connection.value) {
      await connection.value.stop()
      isConnected.value = false
      connection.value = null
    }
  }

  // 作用域销毁（组件卸载）时自动断开，避免连接泄漏
  onScopeDispose(() => {
    void stop()
  })

  return { connection, isConnected, start, stop }
}
