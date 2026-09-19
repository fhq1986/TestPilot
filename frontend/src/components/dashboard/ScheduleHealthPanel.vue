<template>
  <!-- 定时任务健康：这是最容易「悄悄坏掉」的一环 -->
  <el-card v-loading="loading" class="panel panel-schedule">
    <template #header>
      <div class="panel-header">
        <span class="panel-title">定时任务健康</span>
        <el-button link type="primary" @click="router.push('/schedules')">管理</el-button>
      </div>
    </template>

    <div class="health-rows">
      <div class="health-row">
        <span class="health-label">启用中 / 总数</span>
        <span class="health-value">
          {{ data?.scheduleHealth.enabled ?? '—' }} / {{ data?.scheduleHealth.total ?? '—' }}
        </span>
      </div>
      <div class="health-row">
        <span class="health-label">上次触发</span>
        <span class="health-value">
          {{ data?.scheduleHealth.lastRunAt ? formatDateTime(data.scheduleHealth.lastRunAt) : '从未触发' }}
        </span>
      </div>
      <div class="health-row">
        <span class="health-label">下次触发</span>
        <span class="health-value">
          {{ data?.scheduleHealth.nextRunAt ? formatDateTime(data.scheduleHealth.nextRunAt) : '—' }}
        </span>
      </div>
      <div class="health-row">
        <span class="health-label">上次执行报错</span>
        <el-tag v-if="(data?.scheduleHealth.withError ?? 0) > 0" size="small" type="danger" effect="plain">
          {{ data?.scheduleHealth.withError }} 个｜{{ data?.scheduleHealth.lastErrorScheduleName }}
        </el-tag>
        <el-tag v-else size="small" type="success" effect="plain">正常</el-tag>
      </div>
    </div>

    <div class="health-hint">
      调度器停掉时页面照常打开、没有任何报错，只是再也不跑测试了——
      所以这里的「上次触发」比任何面板都值得定期看一眼。
    </div>
  </el-card>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { formatDateTime } from '@/utils/formatter'
import type { DashboardResponse } from '@/types/stats'

defineProps<{ data: DashboardResponse | null; loading: boolean }>()
const router = useRouter()
</script>

<style scoped>
/* ---------------------------------------------------------------- 定时任务健康 */
.health-rows {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.health-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 13px;
}

.health-label {
  color: var(--el-text-color-secondary);
}

.health-value {
  font-weight: 500;
}

.health-hint {
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid var(--el-border-color-lighter);
  font-size: 12px;
  line-height: 1.6;
  color: var(--el-text-color-secondary);
}
</style>
