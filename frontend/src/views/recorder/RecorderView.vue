<template>
  <div class="recorder">
    <el-alert v-if="!capabilities.available" type="error" :closable="false" class="mb-12">
      <template #title>当前服务端不具备录制能力：{{ capabilities.reason }}</template>
    </el-alert>

    <!-- 录制不是「挤一挤还能用」，而是物理上做不到：
         浏览器窗口开在运行录制服务的机器上，人在手机上够不着它。
         不把桌面布局硬塞进 375px，直接说清楚，并把启动入口收起来。 -->
    <DesktopOnlyNotice title="脚本录制需要桌面端"
      reason="点击「开始录制」会在运行录制服务的机器上弹出浏览器窗口，录制过程需要在那里人工操作，手机上无法完成。这里可以查看已有的录制历史。" />

    <div class="recorder-layout">
      <!-- 左：会话配置与历史 -->
      <el-card class="panel panel-left">
        <template #header>
          <div class="panel-header">
            <span class="panel-title">{{ isNarrow ? '录制历史' : '新建录制' }}</span>
          </div>
        </template>

        <template v-if="!isNarrow">
        <el-form :model="form" label-width="86px" class="start-form">
          <el-form-item label="项目">
            <el-select v-model="form.projectId" placeholder="选择项目" class="w-full">
              <el-option v-for="p in projects" :key="p.id" :label="p.name" :value="p.id" />
            </el-select>
          </el-form-item>
          <el-form-item label="会话名称">
            <el-input v-model="form.name" placeholder="留空则用「未命名录制」" />
          </el-form-item>
          <el-form-item label="起始地址">
            <el-input v-model="form.baseUrl" placeholder="https://你的站点（留空打开空白页）" />
          </el-form-item>
          <el-form-item label="浏览器">
            <el-select v-model="form.browser" class="w-full">
              <el-option v-for="b in capabilities.browsers" :key="b.id" :label="b.name" :value="b.id">
                <span>{{ b.name }}</span>
                <span class="option-note">{{ b.note }}</span>
              </el-option>
            </el-select>
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :icon="VideoPlay" :loading="starting"
              :disabled="!capabilities.available || !form.projectId" @click="handleStart">
              开始录制
            </el-button>
          </el-form-item>
        </el-form>

        <el-alert type="info" :closable="false" class="tip">
          <template #title>
            点击「开始录制」后会在<b>本机桌面弹出一个浏览器窗口</b>，你在窗口里的点击、输入会被自动写成 Playwright 脚本。
            录完点「停止并保存」，脚本会解析成平台步骤存为新用例。
          </template>
        </el-alert>
        </template>

        <div class="history">
          <div class="history-title">历史会话</div>
          <!-- 固定高度的独立滚动区：录制会话只会越攒越多，不封顶的话左栏会被一直拉长，
               把上面的「新建录制」表单挤出视野，整页也跟着变高 -->
          <div class="history-list" :class="{ 'is-empty': sessions.length === 0 }">
            <el-empty v-if="sessions.length === 0" description="暂无录制记录" :image-size="60" />
            <div v-for="s in sessions" :key="s.id" class="history-item" :class="{ active: s.id === activeId }"
              @click="openSession(s)">
              <div class="hi-main">
                <span class="hi-name">{{ s.name }}</span>
                <span class="hi-right">
                  <el-tag :type="statusTagType(s.status)" size="small" effect="plain">
                    {{ statusLabel(s.status) }}
                  </el-tag>
                  <el-icon v-if="s.status !== RecorderStatus.Recording" class="hi-del" title="删除会话"
                    @click.stop="handleDelete(s)">
                    <Delete />
                  </el-icon>
                </span>
              </div>
              <div class="hi-meta">
                {{ s.stepCount }} 步 · {{ formatDateTime(s.createdAt) }}
                <span v-if="s.savedTestCaseName" class="hi-saved">已存为「{{ s.savedTestCaseName }}」</span>
              </div>
            </div>
          </div>
        </div>
      </el-card>

      <!-- 右：实时步骤 -->
      <el-card class="panel panel-right">
        <template #header>
          <div class="panel-header">
            <span class="panel-title">
              {{ active ? `录制中：${active.name}` : '实时步骤' }}
            </span>
            <div class="panel-actions">
              <el-tag v-if="active" :type="statusTagType(active.status)" size="small">
                {{ statusLabel(active.status) }}
              </el-tag>
              <el-button v-if="isRecording" type="warning" :icon="VideoPause" :loading="stopping"
                @click="handleStop">停止录制</el-button>
              <el-button v-if="active && !isRecording" type="primary" :icon="Check"
                :disabled="stepCount === 0 || !!active.savedTestCaseId" @click="openSave">
                {{ active.savedTestCaseId ? '已保存' : '保存为用例' }}
              </el-button>
            </div>
          </div>
        </template>

        <el-empty v-if="!active" description="在左侧开始一次录制，步骤会实时出现在这里" />

        <template v-else>
          <el-alert v-if="active.lastError" type="warning" :closable="false" class="mb-12">
            <template #title>{{ active.lastError }}</template>
          </el-alert>

          <div class="steps-header">
            <span>已捕获 <b>{{ stepCount }}</b> 个步骤</span>
            <span v-if="isRecording" class="polling-hint">
              <el-icon class="spin">
                <Loading />
              </el-icon>
              正在监听浏览器操作…
            </span>
          </div>

          <el-alert v-for="(w, i) in warnings" :key="i" type="warning" :closable="false" class="mb-8">
            <template #title>{{ w }}</template>
          </el-alert>

          <el-table :data="steps" size="small" height="320" class="steps-table">
            <el-table-column type="index" label="#" width="46" />
            <el-table-column label="动作" width="110">
              <template #default="{ row }">
                <el-tag size="small" effect="plain">{{ actionLabel(row.actionType) }}</el-tag>
              </template>
            </el-table-column>
            <el-table-column label="目标 / 内容" min-width="220">
              <template #default="{ row }">
                <span class="mono">{{ describeStep(row) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="来源行" min-width="240" show-overflow-tooltip>
              <template #default="{ row }">
                <code class="src">{{ row.sourceLine }}</code>
              </template>
            </el-table-column>
          </el-table>

          <el-collapse class="script-collapse">
            <el-collapse-item>
              <template #title>
                <el-button type="primary">查看生成的 Playwright 脚本</el-button>
              </template>
              <pre class="script">{{ script || '（暂无脚本）' }}</pre>
            </el-collapse-item>
          </el-collapse>
        </template>
      </el-card>
    </div>

    <el-dialog v-model="saveVisible" title="保存为测试用例" width="560px">
      <el-form :model="saveForm" label-width="90px">
        <el-form-item label="用例名称">
          <el-input v-model="saveForm.name" placeholder="用例名称" />
        </el-form-item>
        <el-form-item label="模块">
          <el-input v-model="saveForm.module" placeholder="可选，如「登录模块」" />
        </el-form-item>
        <el-form-item label="优先级">
          <el-select v-model="saveForm.priority" clearable placeholder="可选" class="w-full">
            <el-option label="P0" value="P0" />
            <el-option label="P1" value="P1" />
            <el-option label="P2" value="P2" />
            <el-option label="P3" value="P3" />
          </el-select>
        </el-form-item>
        <el-form-item label="起始地址">
          <el-input v-model="saveForm.baseUrl" placeholder="用例运行时的基础地址" />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="saveForm.description" type="textarea" :rows="2" placeholder="可选" />
        </el-form-item>
      </el-form>
      <p class="save-hint">将保存 {{ stepCount }} 个步骤，用例状态为「草稿」，可在用例编辑页继续调整。</p>
      <template #footer>
        <el-button @click="saveVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Check, Delete, Loading, VideoPause, VideoPlay } from '@element-plus/icons-vue'
import {
  RecorderStatus, deleteRecorderSessionApi, listRecorderSessionsApi, pollRecorderStepsApi,
  recorderCapabilitiesApi, saveRecorderSessionApi, startRecorderSessionApi, stopRecorderSessionApi,
  type RecorderCapabilities, type RecorderSession, type RecorderSnapshot,
} from '@/api/recorder'
import type { ParsedScriptStep } from '@/api/script'
import { ACTION_TYPE_LABELS } from '@/types/testcase'
import { getProjects } from '@/api/project'
import { formatDateTime } from '@/utils/formatter'
import type { Project } from '@/types/project'
import DesktopOnlyNotice from '@/components/common/DesktopOnlyNotice.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'

const POLL_INTERVAL_MS = 2000

const router = useRouter()

/** 窄屏（< 1024px）：录制流程做不到，降级为「只看历史」；见模板顶部的提示 */
const { isNarrow } = useBreakpoint()
const projects = ref<Project[]>([])
const sessions = ref<RecorderSession[]>([])
const capabilities = ref<RecorderCapabilities>({
  available: false, reason: '正在检测…',
  browsers: [],
})

const form = reactive({ projectId: '', name: '', baseUrl: '', browser: 'chromium' })
const starting = ref(false)
const stopping = ref(false)
const saving = ref(false)

const active = ref<RecorderSession | null>(null)
const activeId = ref('')
const snapshot = ref<RecorderSnapshot | null>(null)

const saveVisible = ref(false)
const saveForm = reactive({
  name: '', module: '', priority: '', baseUrl: '', description: '',
})

let timer: ReturnType<typeof setInterval> | null = null

const isRecording = computed(
  () => active.value?.status === RecorderStatus.Recording || active.value?.status === RecorderStatus.Idle,
)
const steps = computed<ParsedScriptStep[]>(() => snapshot.value?.steps ?? [])
const warnings = computed(() => snapshot.value?.warnings ?? [])
const stepCount = computed(() => snapshot.value?.stepCount ?? active.value?.stepCount ?? 0)
const script = computed(() => snapshot.value?.script ?? '')

const actionLabel = (t: number) => ACTION_TYPE_LABELS[t] ?? `动作 ${t}`

/** 把步骤摘要成人能读的一行：优先显示选择器 / 地址 / 输入值 */
function describeStep(step: ParsedScriptStep): string {
  const cfg = step.config ?? {}
  const selector = (cfg as { selector?: { value?: string; description?: string } }).selector
  const parts: string[] = []
  if ((cfg as { url?: string }).url) parts.push((cfg as { url: string }).url)
  if ((cfg as { value?: string }).value) parts.push(`「${(cfg as { value: string }).value}」`)
  if (selector?.value) parts.push(selector.value)
  else if (selector?.description) parts.push(`(AI) ${selector.description}`)
  return parts.join('  ') || step.note || '—'
}

const statusLabel = (s: number) =>
({
  [RecorderStatus.Idle]: '待启动', [RecorderStatus.Recording]: '录制中',
  [RecorderStatus.Stopped]: '已停止', [RecorderStatus.Failed]: '失败'
}[s] ?? '未知')

const statusTagType = (s: number) =>
({
  [RecorderStatus.Idle]: 'info', [RecorderStatus.Recording]: 'success',
  [RecorderStatus.Stopped]: 'info', [RecorderStatus.Failed]: 'danger'
}[s] ?? 'info')

async function loadCapabilities() {
  try {
    capabilities.value = await recorderCapabilitiesApi()
    if (capabilities.value.browsers.length > 0) form.browser = capabilities.value.browsers[0].id
  } catch {
    capabilities.value = { available: false, reason: '能力探测失败', browsers: [] }
  }
}

async function loadSessions() {
  sessions.value = await listRecorderSessionsApi()
}

async function loadProjects() {
  const res = await getProjects({ page: 1, pageSize: 100 })
  projects.value = res.items
  if (!form.projectId && projects.value.length > 0) form.projectId = projects.value[0].id
}

async function handleStart() {
  starting.value = true
  try {
    const session = await startRecorderSessionApi({
      projectId: form.projectId,
      name: form.name || undefined,
      baseUrl: form.baseUrl || undefined,
      browser: form.browser,
    })
    ElMessage.success('录制已开始，请在弹出的浏览器窗口中操作')
    active.value = session
    activeId.value = session.id
    snapshot.value = null
    await loadSessions()
    startPolling()
  } finally {
    starting.value = false
  }
}

function startPolling() {
  stopPolling()
  timer = setInterval(poll, POLL_INTERVAL_MS)
  void poll()
}

function stopPolling() {
  if (timer !== null) {
    clearInterval(timer)
    timer = null
  }
}

async function poll() {
  if (!activeId.value) return
  try {
    const snap = await pollRecorderStepsApi(activeId.value)
    snapshot.value = snap
    // 会话状态可能被服务端回收（浏览器被关掉 / 空闲超时），拉到最新状态
    if (active.value) {
      active.value = {
        ...active.value, status: snap.status, lastError: snap.lastError,
        savedTestCaseId: snap.savedTestCaseId, savedTestCaseName: snap.savedTestCaseName
      }
    }
    if (snap.status !== RecorderStatus.Recording && snap.status !== RecorderStatus.Idle) {
      stopPolling()
      await loadSessions()
    }
  } catch {
    // 轮询失败（网络抖动 / 会话被删）→ 停掉定时器，避免无限报错
    stopPolling()
  }
}

async function handleStop() {
  if (!activeId.value) return
  stopping.value = true
  try {
    active.value = await stopRecorderSessionApi(activeId.value)
    ElMessage.success('录制已停止')
    await poll()
    stopPolling()
    await loadSessions()
  } finally {
    stopping.value = false
  }
}

async function openSession(session: RecorderSession) {
  active.value = session
  activeId.value = session.id
  snapshot.value = null
  await poll()
  if (session.status === RecorderStatus.Recording || session.status === RecorderStatus.Idle) {
    startPolling()
  } else {
    stopPolling()
  }
}

function openSave() {
  if (!active.value) return
  saveForm.name = active.value.name
  saveForm.baseUrl = active.value.baseUrl ?? ''
  saveForm.module = ''
  saveForm.priority = ''
  saveForm.description = ''
  saveVisible.value = true
}

async function handleSave() {
  if (!activeId.value) return
  saving.value = true
  try {
    const result = await saveRecorderSessionApi(activeId.value, {
      name: saveForm.name || undefined,
      module: saveForm.module || undefined,
      priority: saveForm.priority || undefined,
      baseUrl: saveForm.baseUrl || undefined,
      description: saveForm.description || undefined,
    })
    ElMessage.success(result.message)
    saveVisible.value = false
    await loadSessions()
    await ElMessageBox.confirm('用例已创建，是否现在前往编辑？', '保存成功', {
      confirmButtonText: '去编辑', cancelButtonText: '留在本页', type: 'success',
    })
      .then(() => router.push(`/testcases/${result.testCaseId}/edit`))
      .catch(() => undefined)
  } finally {
    saving.value = false
  }
}

async function handleDelete(session: RecorderSession) {
  await ElMessageBox.confirm(
    `确定删除录制会话「${session.name}」？其脚本临时文件会一并清理（已保存的用例不受影响）。`,
    '删除录制会话',
    { type: 'warning' },
  )
  await deleteRecorderSessionApi(session.id)
  ElMessage.success('已删除')
  if (activeId.value === session.id) {
    stopPolling()
    active.value = null
    activeId.value = ''
    snapshot.value = null
  }
  await loadSessions()
}

onMounted(async () => {
  await Promise.all([loadCapabilities(), loadProjects()])
  await loadSessions()
})

onBeforeUnmount(stopPolling)
</script>

<style scoped>
.recorder {
  height: 100%;
}

.recorder-layout {
  display: grid;
  grid-template-columns: 380px 1fr;
  gap: 12px;
  height: 100%;
  align-items: start;
}

/* 窄屏：两栏并排会让每栏只剩一百多像素。改为上下堆叠，
   高度也交回给内容 —— 桌面的 height:100% 是给「左右各自内部滚动」用的。 */
@media (max-width: 1023px) {
  .recorder-layout {
    grid-template-columns: minmax(0, 1fr);
    height: auto;
  }

  .panel-right {
    height: auto;
  }
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.panel-title {
  font-weight: 600;
}

.panel-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.start-form {
  margin-bottom: 4px;
}

.w-full {
  width: 100%;
}

.tip {
  margin-bottom: 16px;
}

.mb-8 {
  margin-bottom: 8px;
}

.mb-12 {
  margin-bottom: 12px;
}

.history-title {
  font-weight: 600;
  margin-bottom: 8px;
  padding-top: 8px;
  border-top: 1px solid var(--el-border-color-lighter);
}

/* 历史会话滚动区。
   高度写成变量而不是散落的魔法数：想按屏幕调只改这一处。
   固定高度是刻意的——会话会一直攒，用 max-height 的话内容少时高度会跳，多了才封顶，
   列表位置不稳定，反而不好点。 */
.history-list {
  height: var(--history-list-height, 200px);
  overflow-y: auto;
  /* 滚动条与内容之间留一点空隙，否则会贴着右边的状态标签 */
  padding-right: 4px;
  /* 滚动到边界时不要让父级跟着滚（触控板/滚轮连续滚动时会"带动整页"） */
  overscroll-behavior: contain;
}

/* 没有记录时不占那 320px：一个空的大方框看起来像加载失败 */
.history-list.is-empty {
  height: auto;
  overflow: hidden;
}

/* 滚动条与 styles/table.css 里大列表的约定保持一致（8px、#c9d8ea 圆角），
   否则同一个应用里会出现两种滚动条长相 */
.history-list::-webkit-scrollbar {
  width: 8px;
}

.history-list::-webkit-scrollbar-thumb {
  background-color: #c9d8ea;
  border-radius: 4px;
}

.history-list::-webkit-scrollbar-thumb:hover {
  background-color: #a9c0dd;
}

.history-list::-webkit-scrollbar-track {
  background-color: transparent;
}

.history-item {
  padding: 8px 10px;
  border-radius: 4px;
  cursor: pointer;
  transition: background 0.15s;
}

.history-item:hover {
  background: var(--el-fill-color-light);
}

.history-item.active {
  background: var(--el-color-primary-light-9);
}

.hi-main {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.hi-right {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

.hi-del {
  color: #c0c6d0;
  cursor: pointer;
}

.hi-del:hover {
  color: var(--el-color-danger);
}

.hi-name {
  font-size: 13px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.hi-meta {
  margin-top: 2px;
  font-size: 12px;
  color: #9aa2ae;
}

.hi-saved {
  margin-left: 6px;
  color: var(--el-color-success);
}

.steps-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.polling-hint {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: var(--el-color-primary);
}

.spin {
  animation: spin 1.2s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.steps-table {
  margin-bottom: 8px;
}

.mono {
  font-family: "Consolas", "Menlo", monospace;
  font-size: 12.5px;
  word-break: break-all;
}

.src {
  font-family: "Consolas", "Menlo", monospace;
  font-size: 12px;
  color: #6b7480;
}

.script-collapse {
  margin-top: 4px;
}

.script {
  background: #f6f8fa;
  border: 1px solid #e6e9ee;
  border-radius: 4px;
  padding: 12px;
  max-height: 300px;
  overflow: auto;
  font-family: "Consolas", "Menlo", monospace;
  font-size: 12.5px;
  line-height: 1.6;
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
}

.option-note {
  margin-left: 8px;
  color: #9aa2ae;
  font-size: 12px;
}

.save-hint {
  color: #7a8290;
  font-size: 13px;
  margin: 0;
}

.panel-right {
  height: 98%;
}
</style>
