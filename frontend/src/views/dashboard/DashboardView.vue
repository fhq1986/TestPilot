<template>
  <div class="dashboard">
    <MetricOverview :data="data" />

    <div class="panels">
      <TrendPanel v-model:trend-days="trendDays" :data="data" :loading="loading" @range-change="load" />
      <StabilityPanel :data="data" :loading="loading" />
      <PlanGatePanel :data="data" :loading="loading" />
      <ScheduleHealthPanel :data="data" :loading="loading" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { getDashboard } from '@/api/stats'
import MetricOverview from '@/components/dashboard/MetricOverview.vue'
import TrendPanel from '@/components/dashboard/TrendPanel.vue'
import StabilityPanel from '@/components/dashboard/StabilityPanel.vue'
import PlanGatePanel from '@/components/dashboard/PlanGatePanel.vue'
import ScheduleHealthPanel from '@/components/dashboard/ScheduleHealthPanel.vue'
import type { DashboardResponse } from '@/types/stats'

const data = ref<DashboardResponse | null>(null)
const loading = ref(false)
/** 趋势窗口：14 天看短期波动，30 天看周级走向 */
const trendDays = ref(14)
let refreshTimer: number | undefined

const runningCount = () => data.value?.overview.runningCount ?? 0

const load = async () => {
  loading.value = data.value === null
  try {
    data.value = await getDashboard(trendDays.value)
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  void load()
  // 有执行进行中时 15s 轮询刷新
  refreshTimer = window.setInterval(() => {
    if (runningCount() > 0) void load()
  }, 15000)
})

onUnmounted(() => {
  if (refreshTimer) window.clearInterval(refreshTimer)
})
</script>

<style scoped>
/* 整页撑满：指标卡固定高度，下方两张面板自适应并内部滚动 */
.dashboard {
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

/* 窄屏（< 1024px）：
   桌面版是「固定高度 + 四张面板各自内部滚动」，一屏之内看全；
   手机上照搬的话四个面板各分到约 1/4 屏高，每个只剩两三行可见 ——
   不如让它自然堆叠、整页滚动。 */
@media (max-width: 1023px) {
  .dashboard {
    height: auto;
    min-height: 100%;
  }

  .panels {
    grid-template-columns: minmax(0, 1fr);
    /* 取消显式均分行高，改由内容撑开 */
    grid-template-rows: none;
    flex: none;
    gap: 12px;
  }

  .panel {
    /* 面板内部不再自己滚动：高度交给内容，滚动交给整页 */
    min-height: 0;
  }
}

/* ---------------------------------------------------------------- 面板 */
/* 2×2 网格：四张面板各占一半宽度与高度，内容超出时面板内部滚动。
   用 grid-template-rows 显式均分高度，否则 flex 时代的 flex:1 语义会丢失。 */
.panels {
  flex: 1;
  min-height: 0;
  display: grid;
  /* 不用等分：趋势图是唯一"按天铺开"的图（14/30 根柱子 + 日期 + 两条折线），
     等分时它最挤；1.4:1 让左列宽一些，右列（稳定性榜、定时任务健康）靠文字换行就够 */
  grid-template-columns: 1.4fr 1fr;
  grid-template-rows: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

.panel {
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.panel :deep(.el-card__body) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.panel-sub {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.panel-title {
  font-weight: 600;
  color: #1f2d3d;
}
</style>
