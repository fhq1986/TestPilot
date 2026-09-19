<template>
  <div class="node-page">
    <el-card shadow="never" class="list-card">
      <template #header>
        <div class="card-header">
          <div class="header-title">
            <span>执行节点</span>
            <span class="header-sub">
              在线 {{ summary.online }} · 离线 {{ summary.offline }}
            </span>
          </div>
          <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        </div>
      </template>

      <el-alert v-if="!loading && nodes.length === 0" type="info" :closable="false"
        title="尚无节点登记。任何运行执行器的平台实例（包括当前主服务）启动后都会自动出现在这里。" />

      <el-table v-else-if="!isMobile" v-loading="loading" :data="nodes" row-key="id" height="100%">
        <el-table-column label="节点" min-width="180">
          <template #default="{ row }">
            <div class="node-cell">
              <span class="status-dot" :class="row.online ? 'is-online' : 'is-offline'" />
              <span class="node-name">{{ row.name }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.online ? 'success' : 'info'" size="small" effect="plain">
              {{ row.online ? '在线' : '离线' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="并发负载" min-width="170">
          <template #default="{ row }">
            <div class="load-cell">
              <el-progress
                :percentage="row.maxConcurrency > 0 ? Math.min(100, Math.round(row.runningCount * 100 / row.maxConcurrency)) : 0"
                :stroke-width="8"
                :color="loadColor(row)"
                :show-text="false"
                class="load-bar"
              />
              <span class="load-text">{{ row.runningCount }} / {{ row.maxConcurrency }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="今日完成" width="100" align="center">
          <template #default="{ row }">{{ row.todayCompleted }}</template>
        </el-table-column>
        <el-table-column label="最近心跳" width="170">
          <template #default="{ row }">
            <span :class="{ 'text-muted': !row.online }">{{ formatDateTime(row.lastHeartbeatAt) }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="version" label="版本" width="110" />
        <el-table-column prop="machineName" label="机器" min-width="130" />
        <el-table-column label="启动时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.startedAt) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="100" fixed="right">
          <template #default="{ row }">
            <el-button v-if="!row.online" link type="danger" @click="removeNode(row)">移除记录</el-button>
            <span v-else class="text-muted">—</span>
          </template>
        </el-table-column>
      </el-table>

      <!-- 窄屏：表格换成卡片。⚠️ 这条链是 alert → 表格 → 卡片，
           三个分支共用一个 v-if/v-else-if/v-else，顺序不能插错 -->
      <MobileCardList v-else v-loading="loading" :items="nodes" :row-key="(row) => row.id"
        empty-text="暂无节点">
        <template #title="{ item }">
          <span class="node-name">{{ item.name }}</span>
        </template>

        <template #badge="{ item }">
          <el-tag :type="item.online ? 'success' : 'info'" size="small" effect="plain">
            {{ item.online ? '在线' : '离线' }}
          </el-tag>
        </template>

        <template #meta="{ item }">
          <span><span class="mcl-label">负载</span>{{ item.runningCount }} / {{ item.maxConcurrency }}</span>
          <span><span class="mcl-label">今日完成</span>{{ item.todayCompleted }}</span>
          <span><span class="mcl-label">最近心跳</span>{{ formatDateTime(item.lastHeartbeatAt) }}</span>
          <span v-if="item.version"><span class="mcl-label">版本</span>{{ item.version }}</span>
          <span v-if="item.machineName"><span class="mcl-label">机器</span>{{ item.machineName }}</span>
          <span><span class="mcl-label">启动</span>{{ formatDateTime(item.startedAt) }}</span>
        </template>

        <template #actions="{ item }">
          <el-button v-if="!item.online" link type="danger" @click="removeNode(item)">移除记录</el-button>
          <span v-else class="text-muted">在线节点无需移除</span>
        </template>
      </MobileCardList>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { listNodesApi, deleteNodeApi } from '@/api/node'
import type { NodeView } from '@/types/node'
import { formatDateTime } from '@/utils/formatter'
import MobileCardList from '@/components/common/MobileCardList.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'

/** 窄屏（< 1024px）：表格换成卡片形态，见下方模板 */
const { isMobile } = useBreakpoint()

const loading = ref(false)
const nodes = ref<NodeView[]>([])
const onlineCount = ref(0)
const offlineCount = ref(0)

const summary = computed(() => ({ online: onlineCount.value, offline: offlineCount.value }))

let timer: number | undefined

async function load() {
  loading.value = true
  try {
    const res = await listNodesApi()
    nodes.value = res.nodes
    onlineCount.value = res.onlineCount
    offlineCount.value = res.offlineCount
  } finally {
    loading.value = false
  }
}

function loadColor(row: NodeView) {
  if (!row.online) return '#c0c4cc'
  const ratio = row.maxConcurrency > 0 ? row.runningCount / row.maxConcurrency : 0
  if (ratio >= 1) return '#f56c6c'
  if (ratio >= 0.7) return '#e6a23c'
  return '#67c23a'
}

async function removeNode(row: NodeView) {
  await ElMessageBox.confirm(
    `移除离线节点「${row.name}」的登记记录？（该节点若重新启动会再次自动登记）`,
    '移除节点记录',
    { type: 'warning', confirmButtonText: '移除', cancelButtonText: '取消' },
  )
  await deleteNodeApi(row.name)
  ElMessage.success('已移除')
  await load()
}

onMounted(() => {
  load()
  // 看板是"活"的：15s 自动刷新一次（心跳间隔 20s，足够追上状态变化）
  timer = window.setInterval(load, 15000)
})

onBeforeUnmount(() => {
  if (timer) window.clearInterval(timer)
})
</script>

<style scoped>
.node-page {
  height: calc(100vh - 56px - 32px);
  display: flex;
  flex-direction: column;
}

.list-card {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.list-card :deep(.el-card__body) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.header-title {
  display: flex;
  align-items: baseline;
  gap: 12px;
  font-weight: 600;
}

.header-sub {
  font-size: 13px;
  font-weight: normal;
  color: var(--el-text-color-secondary);
}

.node-cell {
  display: flex;
  align-items: center;
  gap: 8px;
}

.node-name {
  font-weight: 500;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex: none;
}

.status-dot.is-online {
  background: var(--el-color-success);
  box-shadow: 0 0 0 3px rgba(103, 194, 58, 0.15);
}

.status-dot.is-offline {
  background: var(--el-text-color-disabled);
}

.load-cell {
  display: flex;
  align-items: center;
  gap: 8px;
}

.load-bar {
  flex: 1;
}

.load-text {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  white-space: nowrap;
}

.text-muted {
  color: var(--el-text-color-disabled);
}
</style>
