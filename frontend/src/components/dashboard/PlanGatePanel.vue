<template>
  <!-- 进行中计划的达标态势：判定数字全部来自后端，前端只负责显示 -->
  <el-card v-loading="loading" class="panel panel-plan">
    <template #header>
      <div class="panel-header">
        <span class="panel-title">进行中计划达标态势</span>
        <el-button link type="primary" @click="router.push('/test-plans')">全部计划</el-button>
      </div>
    </template>

    <div v-if="(data?.activePlans.length ?? 0) > 0" class="plan-list">
      <div v-for="item in data?.activePlans" :key="item.planId" class="plan-item"
        @click="router.push(`/test-plans/${item.planId}`)">
        <div class="plan-main">
          <div class="plan-name">
            <span :title="item.planName">{{ item.planName }}</span>
            <span v-if="item.releaseName" class="plan-release">{{ item.releaseName }}</span>
          </div>
          <div class="plan-meta">
            {{ item.projectName }} · 目标 {{ (item.targetPassRate * 100).toFixed(0) }}% ·
            {{ item.caseCount }} 条用例
          </div>
        </div>
        <div class="plan-right">
          <el-tag v-if="item.evaluatedRoundNo == null" size="small" type="info" effect="plain">
            未执行
          </el-tag>
          <el-tag v-else size="small" :type="item.gatePassed ? 'success' : 'danger'" effect="plain">
            {{ item.gatePassed ? '达标' : '未达标' }}
            {{ (item.evaluatedPassRate * 100).toFixed(1) }}%
          </el-tag>
          <span v-if="item.daysToDeadline != null" class="plan-deadline"
            :class="{ 'deadline-bad': item.daysToDeadline < 0, 'deadline-warn': item.daysToDeadline >= 0 && item.daysToDeadline <= 3 }">
            {{ deadlineText(item.daysToDeadline) }}
          </span>
        </div>
      </div>
    </div>
    <el-empty v-else description="当前没有进行中的测试计划" :image-size="56" />
  </el-card>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import type { DashboardResponse } from '@/types/stats'

defineProps<{ data: DashboardResponse | null; loading: boolean }>()
const router = useRouter()

const deadlineText = (days: number) => {
  if (days < 0) return `已过期 ${Math.abs(days)} 天`
  if (days === 0) return '今天截止'
  return `剩 ${days} 天`
}
</script>

<style scoped>
/* ---------------------------------------------------------------- 计划达标态势 */
.plan-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

.plan-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 4px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  cursor: pointer;
}

.plan-item:last-child {
  border-bottom: none;
}

.plan-item:hover {
  background: var(--el-fill-color-light);
}

.plan-main {
  min-width: 0;
}

.plan-name {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.plan-release {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.plan-meta {
  margin-top: 2px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.plan-right {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
  flex-shrink: 0;
}

.plan-deadline {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.deadline-warn {
  color: var(--el-color-warning);
}

.deadline-bad {
  color: var(--el-color-danger);
}
</style>
