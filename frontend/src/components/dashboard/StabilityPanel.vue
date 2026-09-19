<template>
  <!-- 稳定性榜：按「失败率」排而不是失败次数。
       按次数排会让跑得多的用例必然霸榜，而"跑 100 次失败 20 次"和
       "跑 5 次失败 5 次"是两回事——所以带出样本量让人自己判断可信度。 -->
  <el-card v-loading="loading" class="panel panel-stability">
    <template #header>
      <div class="panel-header">
        <span class="panel-title">近 30 天稳定性榜</span>
        <span class="panel-sub">按失败率排 · 样本 ≥ {{ MIN_RUNS_FOR_STABILITY }} 次</span>
      </div>
    </template>

    <div v-if="(data?.unstableTop.length ?? 0) > 0" class="failure-list">
      <div v-for="(item, index) in data?.unstableTop" :key="item.testCaseId" class="failure-item"
        @click="router.push(`/testcases/${item.testCaseId}`)">
        <span class="failure-rank" :class="`rank-${Math.min(index + 1, 4)}`">{{ index + 1 }}</span>
        <div class="failure-main">
          <div class="failure-name">
            <span :title="item.testCaseName">{{ item.testCaseName }}</span>
            <el-tag v-if="item.isFlaky" size="small" type="warning" effect="plain">flaky</el-tag>
          </div>
          <div class="failure-time">
            失败 {{ item.failCount }}/{{ item.totalRuns }} 次 ·
            {{ item.module || '未分模块' }} · 最近 {{ formatDateTime(item.lastFailedAt) }}
          </div>
        </div>
        <el-tag :type="item.failRate >= 0.5 ? 'danger' : 'warning'" size="small" round>
          通过率 {{ ((1 - item.failRate) * 100).toFixed(0) }}%
        </el-tag>
      </div>
    </div>
    <el-empty v-else description="近 30 天没有达到统计门槛的不稳定用例" :image-size="56" />
  </el-card>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { formatDateTime } from '@/utils/formatter'
import type { DashboardResponse } from '@/types/stats'

defineProps<{ data: DashboardResponse | null; loading: boolean }>()
const router = useRouter()

/** 稳定性榜的最小样本量。与后端 StatsApiExtensions.MinRunsForStability 保持一致 */
const MIN_RUNS_FOR_STABILITY = 5
</script>

<style scoped>
/* ---------------------------------------------------------------- 失败榜 */
.failure-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

.failure-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 8px;
  border-radius: 6px;
  border-bottom: 1px solid #eef2f8;
  cursor: pointer;
  transition: background-color 0.15s ease;
}

.failure-item:last-child {
  border-bottom: none;
}

.failure-item:hover {
  background-color: #f6f9fe;
}

.failure-rank {
  flex-shrink: 0;
  width: 22px;
  height: 22px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  font-weight: 600;
  color: #fff;
  background: #c0c8d6;
}

.rank-1 {
  background: linear-gradient(135deg, #f0685f, #c0272d);
}

.rank-2 {
  background: linear-gradient(135deg, #f5a25d, #d97706);
}

.rank-3 {
  background: linear-gradient(135deg, #f3c64d, #b26a00);
}

.failure-main {
  flex: 1;
  min-width: 0;
}

.failure-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
  color: #1f2d3d;
}

.failure-time {
  margin-top: 2px;
  font-size: 12px;
  color: #96a1b3;
}
</style>
