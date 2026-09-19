<template>
  <div class="chat-page">
    <el-card class="chat-card">
      <template #header>
        <div class="chat-header">
          <span class="chat-title">
            <el-icon><ChatDotRound /></el-icon>
            AI 助手
          </span>
          <span class="chat-sub">基于平台已配置的模型；可上传截图提问、可用语音输入</span>
          <el-button link type="danger" :disabled="messages.length === 0" :icon="Delete"
            @click="handleClear">清空对话</el-button>
        </div>
      </template>

      <!-- 消息区 -->
      <div ref="scrollRef" v-loading="loadingHistory" class="chat-body">
        <el-empty v-if="messages.length === 0" description="开始提问吧，例如：帮我设计登录功能的测试用例" />

        <div v-for="(msg, index) in messages" :key="index" class="msg" :class="`msg-${msg.role}`">
          <div class="msg-avatar">{{ msg.role === 'user' ? '我' : 'AI' }}</div>
          <div class="msg-main">
            <div v-if="msg.images?.length" class="msg-images">
              <el-image v-for="(img, i) in msg.images" :key="i" :src="img" fit="cover"
                :preview-src-list="msg.images" :initial-index="i" class="msg-image" />
            </div>
            <div class="msg-bubble">
              <span class="msg-text">{{ msg.content || (msg.streaming ? '思考中…' : '') }}</span>
              <span v-if="msg.streaming" class="cursor">▋</span>
            </div>
            <div v-if="msg.notice" class="msg-notice">{{ msg.notice }}</div>
          </div>
        </div>
      </div>

      <!-- 待发送的截图 -->
      <div v-if="pendingImages.length" class="pending-images">
        <div v-for="(img, i) in pendingImages" :key="i" class="pending-item">
          <img :src="img" alt="截图" />
          <el-icon class="pending-remove" @click="pendingImages.splice(i, 1)"><Close /></el-icon>
        </div>
      </div>

      <!-- 输入区 -->
      <div class="chat-input" @paste="handlePaste" @drop.prevent="handleDrop" @dragover.prevent>
        <el-input v-model="input" type="textarea" :rows="3" resize="none"
          placeholder="输入问题，Enter 发送 / Shift+Enter 换行；截图可直接 Ctrl+V 粘贴或拖拽到此处"
          @keydown.enter.exact.prevent="handleSend" />
        <div class="chat-actions">
          <input ref="fileRef" type="file" accept="image/*" multiple hidden @change="onPickImages" />
          <el-button :icon="Picture" :disabled="pendingImages.length >= 6" @click="fileRef?.click()">
            截图（{{ pendingImages.length }}/6）
          </el-button>
          <span class="paste-hint">支持 Ctrl+V 粘贴、拖拽图片</span>
          <el-tooltip :content="speechTip" placement="top">
            <el-button :type="recording ? 'danger' : 'default'" :icon="Microphone"
              :disabled="!speechSupported" @click="toggleRecord">
              {{ recording ? '停止录音' : '语音输入' }}
            </el-button>
          </el-tooltip>
          <div class="chat-spacer" />
          <el-button v-if="streaming" type="danger" :icon="VideoPause" @click="handleStop">停止</el-button>
          <el-button v-else type="primary" :icon="Promotion" :disabled="!canSend" @click="handleSend">
            发送
          </el-button>
        </div>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ChatDotRound, Close, Delete, Microphone, Picture, Promotion, VideoPause,
} from '@element-plus/icons-vue'
import { streamChat, type ChatMessagePayload } from '@/api/chat'

interface ChatItem {
  role: 'user' | 'assistant'
  content: string
  images?: string[]
  streaming?: boolean
  notice?: string
}

const STORAGE_KEY = 'ai_chat_history'
const MAX_CONTEXT = 20

const messages = ref<ChatItem[]>([])
const input = ref('')
const pendingImages = ref<string[]>([])
const streaming = ref(false)
const loadingHistory = ref(false)
const scrollRef = ref<HTMLElement>()
const fileRef = ref<HTMLInputElement>()
const controller = ref<AbortController | null>(null)

const canSend = computed(() => !streaming.value && (input.value.trim().length > 0 || pendingImages.value.length > 0))

// ---------------------------------------------------------------- 历史持久化
const saveHistory = () => {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(messages.value.slice(-50)))
  } catch {
    // 超出配额时忽略
  }
}

const loadHistory = () => {
  loadingHistory.value = true
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    messages.value = raw ? (JSON.parse(raw) as ChatItem[]) : []
  } catch {
    messages.value = []
  } finally {
    loadingHistory.value = false
  }
}

const scrollToBottom = async () => {
  await nextTick()
  if (scrollRef.value) scrollRef.value.scrollTop = scrollRef.value.scrollHeight
}

// ---------------------------------------------------------------- 截图处理
/** 压缩为最长边 1280 的 JPEG dataURL，避免请求体过大 */
const toCompressedDataUrl = async (file: File): Promise<string> => {
  const dataUrl = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = () => reject(new Error('读取图片失败'))
    reader.readAsDataURL(file)
  })

  try {
    const image = await new Promise<HTMLImageElement>((resolve, reject) => {
      const el = new Image()
      el.onload = () => resolve(el)
      el.onerror = () => reject(new Error('图片解析失败'))
      el.src = dataUrl
    })
    const maxSide = 1280
    const scale = Math.min(1, maxSide / Math.max(image.width, image.height))
    const canvas = document.createElement('canvas')
    canvas.width = Math.round(image.width * scale)
    canvas.height = Math.round(image.height * scale)
    const ctx = canvas.getContext('2d')
    if (!ctx)
      return dataUrl
    ctx.drawImage(image, 0, 0, canvas.width, canvas.height)
    return canvas.toDataURL('image/jpeg', 0.82)
  } catch {
    return dataUrl
  }
}

const MAX_IMAGES = 6

/** 统一的图片收集入口：文件选择 / 粘贴 / 拖拽 共用，超额时给出提示 */
const addImages = async (files: File[]) => {
  const images = files.filter((f) => f.type.startsWith('image/'))
  if (images.length === 0)
    return

  const room = MAX_IMAGES - pendingImages.value.length
  if (room <= 0) {
    ElMessage.warning(`最多上传 ${MAX_IMAGES} 张截图`)
    return
  }
  if (images.length > room)
    ElMessage.warning(`单次最多 ${MAX_IMAGES} 张，已保留前 ${room} 张`)

  for (const file of images.slice(0, room))
    pendingImages.value.push(await toCompressedDataUrl(file))
  ElMessage.success(`已添加 ${Math.min(images.length, room)} 张截图`)
}

const onPickImages = async (event: Event) => {
  const target = event.target as HTMLInputElement
  await addImages(Array.from(target.files ?? []))
  target.value = ''
}

/** 直接粘贴截图（Ctrl+V） */
const handlePaste = async (event: ClipboardEvent) => {
  const items = Array.from(event.clipboardData?.items ?? [])
  const files = items
    .filter((item) => item.kind === 'file' && item.type.startsWith('image/'))
    .map((item) => item.getAsFile())
    .filter((file): file is File => file !== null)

  if (files.length === 0)
    return   // 非图片内容：保持默认的文本粘贴行为
  event.preventDefault()
  await addImages(files)
}

/** 拖拽图片到输入区 */
const handleDrop = async (event: DragEvent) => {
  const files = Array.from(event.dataTransfer?.files ?? [])
  if (files.some((f) => f.type.startsWith('image/')))
    await addImages(files)
}

// ---------------------------------------------------------------- 语音输入
const speechSupported = ref(false)
const recording = ref(false)
const speechTip = ref('使用浏览器语音识别（Chrome/Edge），识别结果会追加到输入框')
// eslint-disable-next-line @typescript-eslint/no-explicit-any
let recognition: any = null
let baseText = ''

const initSpeech = () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const ctor = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition
  if (!ctor) {
    speechTip.value = '当前浏览器不支持语音识别，请使用 Chrome/Edge'
    return
  }
  speechSupported.value = true
  recognition = new ctor()
  recognition.lang = 'zh-CN'
  recognition.continuous = true
  recognition.interimResults = true
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  recognition.onresult = (event: any) => {
    let text = ''
    for (let i = event.resultIndex; i < event.results.length; i++)
      text += event.results[i][0].transcript
    input.value = baseText + text
  }
  recognition.onerror = () => {
    ElMessage.warning('语音识别失败，请检查麦克风权限')
    recording.value = false
  }
  recognition.onend = () => {
    recording.value = false
  }
}

const toggleRecord = () => {
  if (!recognition) return
  if (recording.value) {
    recognition.stop()
    recording.value = false
    return
  }
  baseText = input.value
  recognition.start()
  recording.value = true
  ElMessage.info('开始录音，说完点击「停止录音」')
}

// ---------------------------------------------------------------- 发送
const handleSend = async () => {
  if (!canSend.value) return
  if (recording.value && recognition) {
    recognition.stop()
    recording.value = false
  }

  const images = [...pendingImages.value]
  const content = input.value.trim() || '（请描述这张截图）'
  messages.value.push({ role: 'user', content, images })
  input.value = ''
  pendingImages.value = []

  const assistant: ChatItem = { role: 'assistant', content: '', streaming: true }
  messages.value.push(assistant)
  streaming.value = true
  await scrollToBottom()

  // 只取最近的上下文，避免超出模型上限
  const context: ChatMessagePayload[] = messages.value
    .filter((m) => !m.streaming)
    .slice(-MAX_CONTEXT)
    .map((m) => ({ role: m.role, content: m.content }))

  controller.value = new AbortController()
  let streamed = ''
  try {
    await streamChat(
      { messages: context, images },
      {
        onDelta: (text) => {
          streamed += text
          assistant.content = streamed
          void scrollToBottom()
        },
        onNotice: (message) => {
          assistant.notice = message
        },
        onError: (message) => {
          assistant.content = assistant.content || message
          ElMessage.error(message)
        },
        onDone: () => {
          assistant.streaming = false
        },
      },
      controller.value.signal,
    )
  } catch (error) {
    // 用户主动停止
    if ((error as Error)?.name !== 'AbortError')
      ElMessage.error((error as Error)?.message ?? '对话失败')
  } finally {
    assistant.streaming = false
    streaming.value = false
    controller.value = null
    if (!assistant.content) assistant.content = '（未返回内容）'
    saveHistory()
    await scrollToBottom()
  }
}

const handleStop = () => {
  controller.value?.abort()
  streaming.value = false
}

const handleClear = async () => {
  try {
    await ElMessageBox.confirm('确认清空当前对话记录？', '提示', { type: 'warning' })
  } catch {
    return
  }
  messages.value = []
  pendingImages.value = []
  localStorage.removeItem(STORAGE_KEY)
  ElMessage.success('已清空对话')
}

onMounted(() => {
  loadHistory()
  initSpeech()
  void scrollToBottom()
})
</script>

<style scoped>
/* 页面撑满视口：卡片自适应高度，消息区内部滚动，输入区固定在底部 */
.chat-page {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.chat-card {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.chat-card :deep(.el-card__body) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  padding-bottom: 12px;
}

.chat-header {
  display: flex;
  align-items: center;
  gap: 10px;
}

.chat-title {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-weight: 600;
}

.chat-sub {
  flex: 1;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.chat-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding-right: 6px;
}

.msg {
  display: flex;
  gap: 10px;
  margin-bottom: 16px;
}

.msg-user {
  flex-direction: row-reverse;
}

.msg-avatar {
  flex-shrink: 0;
  width: 32px;
  height: 32px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  font-weight: 600;
  background: var(--el-color-primary-light-8);
  color: var(--el-color-primary);
}

.msg-user .msg-avatar {
  background: var(--el-color-success-light-8);
  color: var(--el-color-success);
}

.msg-main {
  max-width: 78%;
}

.msg-user .msg-main {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
}

.msg-bubble {
  padding: 9px 12px;
  border-radius: 8px;
  background: var(--el-fill-color-light);
  line-height: 1.7;
  font-size: 14px;
  white-space: pre-wrap;
  word-break: break-word;
}

.msg-user .msg-bubble {
  background: var(--el-color-primary-light-9);
}

.cursor {
  animation: blink 1s steps(2, start) infinite;
  color: var(--el-color-primary);
}

@keyframes blink {
  to { visibility: hidden; }
}

.msg-images {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 6px;
}

.msg-image {
  width: 120px;
  height: 78px;
  border-radius: 6px;
  border: 1px solid var(--el-border-color-lighter);
}

.msg-notice {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-color-warning);
}

.pending-images {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 8px;
}

.pending-item {
  position: relative;
}

.pending-item img {
  width: 72px;
  height: 48px;
  object-fit: cover;
  border-radius: 4px;
  border: 1px solid var(--el-border-color);
}

.pending-remove {
  position: absolute;
  top: -6px;
  right: -6px;
  cursor: pointer;
  background: #fff;
  border-radius: 50%;
  color: var(--el-color-danger);
}

.chat-input {
  flex-shrink: 0;
  border-top: 1px solid var(--el-border-color-lighter);
  padding-top: 10px;
}

.chat-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 8px;
}

.paste-hint {
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}

.chat-spacer {
  flex: 1;
}
</style>
