<template>
  <div class="approval-page">
    <el-card class="approval-card">

      <el-tabs v-model="activeTab" @tab-change="onTabChange">
        <el-tab-pane label="待审批" name="pending" />
        <el-tab-pane label="已批准" name="approved" />
        <el-tab-pane label="已拒绝" name="rejected" />
      </el-tabs>
      <div style="padding-bottom:5px;">
        <el-alert type="warning" :closable="false" class="boundary-tip">
          <template #title>
            需人工确认的 Agent 修复建议：破坏性 / 结构类改动默认不自动应用，采纳后会写入真实用例步骤
          </template>
        </el-alert>
      </div>

      <div class="table-wrap">
        <el-table v-loading="loading" :data="items" height="100%" border class="wrap-table">
          <el-table-column label="用例" width="180" fixed="left">
            <template #default="{ row }">
              <el-link type="primary" @click="openExecution(row.executionId)">{{ row.testCaseName }}</el-link>
            </template>
          </el-table-column>
          <el-table-column label="步骤" width="60">
            <template #default="{ row }">#{{ row.targetStepOrder }}</template>
          </el-table-column>
          <el-table-column label="修复类别" width="120">
            <template #default="{ row }">{{ FIX_CATEGORY_LABELS[row.fixCategory] ?? '未知' }}</template>
          </el-table-column>
          <el-table-column label="置信度" width="70">
            <template #default="{ row }">{{ Math.round((row.confidence ?? 0) * 100) }}%</template>
          </el-table-column>
          <el-table-column label="修复建议">
            <template #default="{ row }">{{ row.fixSummary || '—' }}</template>
          </el-table-column>
          <el-table-column label="结果" width="90">
            <template #default="{ row }">
              <el-tag size="small" :type="agentAttemptResultTagType(row.result)">
                {{ AGENT_ATTEMPT_RESULT_LABELS[row.result] ?? '未知' }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="时间" width="130">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="140" fixed="right">
            <template #default="{ row }">
              <template v-if="activeTab === 'pending'">
                <el-button size="small" type="primary" :loading="acting === row.attemptId"
                  @click="approve(row)">采纳</el-button>
                <el-button size="small" :loading="acting === row.attemptId" @click="reject(row)">驳回</el-button>
              </template>
              <span v-else class="muted">{{ row.approved ? '已批准' : '已拒绝' }}</span>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <el-pagination v-model:current-page="page" v-model:page-size="pageSize"
        :total="total" :page-sizes="[20, 50, 100]"
        layout="total, sizes, prev, pager, next"
        class="pager" @current-change="onPageChange" @size-change="onPageSizeChange" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { approveAgentAttempt, getAgentApprovals, rejectAgentAttempt } from '@/api/execution'
import {
  AGENT_ATTEMPT_RESULT_LABELS, FIX_CATEGORY_LABELS, agentAttemptResultTagType,
  type AgentApprovalItem,
} from '@/types/execution'
import { formatDateTime } from '@/utils/formatter'

const router = useRouter()
const activeTab = ref<'pending' | 'approved' | 'rejected'>('pending')
const items = ref<AgentApprovalItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const loading = ref(false)
/** 正在处理的 attemptId（按钮 loading，防重复点击） */
const acting = ref<string | null>(null)

const load = async () => {
  loading.value = true
  try {
    const res = await getAgentApprovals({
      status: activeTab.value,
      page: page.value,
      pageSize: pageSize.value,
    })
    items.value = res.items
    total.value = res.total
  } catch {
    items.value = []
    total.value = 0
  } finally {
    loading.value = false
  }
}

const onTabChange = () => {
  page.value = 1
  void load()
}

const onPageChange = () => void load()
const onPageSizeChange = (size: number) => {
  pageSize.value = size
  page.value = 1
  void load()
}

const openExecution = (id: string) => router.push(`/executions/${id}`)

const approve = async (row: AgentApprovalItem) => {
  const confirmed = await ElMessageBox.confirm(
    `将把该修复应用到真实用例步骤，请确认：\n${row.fixSummary || '（无描述）'}`,
    '采纳 Agent 修复',
    { type: 'warning', confirmButtonText: '采纳', cancelButtonText: '取消' },
  ).catch(() => false)
  if (!confirmed) return
  acting.value = row.attemptId
  try {
    const res = await approveAgentAttempt(row.attemptId)
    ElMessage.success(res?.applied ? `已采纳并应用到 ${res.applied} 个步骤` : '已标记采纳（无可用动作可应用）')
    await load()
  } finally {
    acting.value = null
  }
}

const reject = async (row: AgentApprovalItem) => {
  acting.value = row.attemptId
  try {
    await rejectAgentAttempt(row.attemptId)
    ElMessage.success('已驳回')
    await load()
  } finally {
    acting.value = null
  }
}

onMounted(load)
</script>

<style scoped>
/* 整页纵向布局：高度 = 视口 - 顶栏 56px - main 上下内边距 32px */
.approval-page {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 56px - 32px);
  overflow: hidden;
}

.approval-card {
  flex: 1;
  min-height: 0;
  margin-top: 0;
  /* 让 el-card body 也继承 flex 布局 */
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.approval-card :deep(.el-card__body) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.muted {
  color: var(--el-text-color-secondary);
}

.wrap-table :deep(.cell) {
  white-space: normal;
  word-break: break-all;
  line-height: 1.5;
}

.table-wrap {
  flex: 1;
  min-height: 0;
  margin-top: 8px;
}

.pager {
  margin-top: 12px;
  justify-content: flex-end;
  flex-shrink: 0;
}
</style>
