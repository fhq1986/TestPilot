<template>
  <!-- 执行趋势：时间范围可切换（14/30 天）；缺陷进出与执行共用日期轴，质量因果一眼可见 -->
  <el-card v-loading="loading" class="panel panel-trend">
    <!-- 页头分两行：标题 + 天数开关一行，图例一行。
         四样东西挤在一行时，图例会被压成竖排（面板只有半屏宽），
         连标题都会折行。 -->
    <template #header>
      <div class="panel-header panel-header-stacked">
        <div class="panel-header-top">
          <span class="panel-title">近 {{ trendDays }} 天质量趋势</span>
          <el-radio-group v-model="days" size="small" class="trend-range" @change="emit('range-change')">
            <el-radio-button :value="14">14 天</el-radio-button>
            <el-radio-button :value="30">30 天</el-radio-button>
          </el-radio-group>
        </div>
        <div class="legend">
          <span class="legend-item"><i class="dot dot-pass" />通过</span>
          <span class="legend-item"><i class="dot dot-fail" />失败</span>
          <span class="legend-item"><i class="line-sample" />通过率</span>
          <span class="legend-item"><i class="line-sample line-created" />缺陷新增</span>
          <span class="legend-item"><i class="line-sample line-closed" />缺陷闭环</span>
        </div>
      </div>
    </template>

    <div v-if="maxTrendTotal > 0" class="chart-area">
      <div class="chart-grid">
        <span v-for="n in 4" :key="n" class="grid-line" :style="{ bottom: `${n * 25}%` }" />
      </div>
      <div class="trend-chart">
        <!-- 折线叠加层：与 .chart-grid 用同一套绝对定位，因此和柱子的绘图区严格对齐。
             preserveAspectRatio=none 把 viewBox 拉满整块区域，x/y 直接用百分比坐标即可；
             但拉伸会让线条粗细失真，所以描边必须加 vector-effect=non-scaling-stroke。
             圆点用 HTML 而不是 SVG circle —— 非等比缩放下 circle 会被压成椭圆。 -->
        <div class="trend-overlay">
          <svg class="trend-svg" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
            <polyline v-for="(seg, i) in rateSegments" :key="i" :points="toPolyline(seg)"
              fill="none" stroke="#e8952f" stroke-width="2" stroke-linejoin="round"
              stroke-linecap="round" vector-effect="non-scaling-stroke" />
            <!-- 缺陷曲线：按当日缺陷量归一化（绿色=闭环向上是好，红色=新增向上要警惕） -->
            <polyline :points="toPolyline(defectClosedSegments)" fill="none" stroke="#67c23a"
              stroke-width="1.5" stroke-dasharray="4 3" stroke-linejoin="round"
              vector-effect="non-scaling-stroke" />
            <polyline :points="toPolyline(defectCreatedSegments)" fill="none" stroke="#f56c6c"
              stroke-width="1.5" stroke-dasharray="4 3" stroke-linejoin="round"
              vector-effect="non-scaling-stroke" />
          </svg>
          <span v-for="(dot, i) in rateDots" :key="i" class="rate-dot"
            :style="{ left: `${dot.x}%`, top: `${dot.y}%` }" />
        </div>

        <div v-for="(point, index) in data?.trend ?? []" :key="point.date" class="trend-col"
          :title="`${point.date}｜执行 ${point.total}（通过 ${point.passed} / 失败 ${point.failed}）${point.total > 0 ? `｜通过率 ${((point.passed / point.total) * 100).toFixed(1)}%` : ''}｜缺陷 新增 ${point.defectsCreated ?? 0} / 闭环 ${point.defectsClosed ?? 0}`">
          <div class="trend-total">{{ point.total > 0 ? point.total : '' }}</div>
          <div class="trend-bars">
            <div class="bar bar-fail" :style="barStyle(point.failed)" />
            <div class="bar bar-pass" :style="barStyle(point.passed)" />
          </div>
          <div class="trend-date">{{ dateLabel(point.date, index) }}</div>
        </div>
      </div>
    </div>
    <el-empty v-else :description="`近 ${trendDays} 天暂无执行数据`" :image-size="56" />
  </el-card>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { buildCountSegments, buildRateDots, buildRateSegments, toPolyline } from '@/utils/trendChart'
import type { DashboardResponse } from '@/types/stats'

const props = defineProps<{
  data: DashboardResponse | null
  loading: boolean
  trendDays: number
}>()

const emit = defineEmits<{ 'range-change': []; 'update:trendDays': [value: number] }>()

/** 支持父组件 v-model:trend-days：切天数由父组件重新拉数据 */
const days = computed({
  get: () => props.trendDays,
  set: (v: number) => emit('update:trendDays', v),
})

const maxTrendTotal = computed(() =>
  Math.max(1, ...(props.data?.trend ?? []).map((p) => p.total)),
)

const barStyle = (count: number) => ({
  height: count > 0 ? `${Math.max(4, (count / maxTrendTotal.value) * 100)}%` : '2px',
})

/**
 * 日期标签抽稀。
 *
 * 30 天模式下 30 个「09-01」在半屏宽里必然互相叠住（每个标签约 30px，而一根柱子只有十几 px）。
 * 按槽位数算步长，只渲染整步长的那个标签——**每列仍然占位**，所以抽稀不会让柱子或折线错位。
 */
const dateLabelStep = computed(() => {
  const count = props.data?.trend?.length ?? 0
  return Math.max(1, Math.ceil(count / 8))
})

const dateLabel = (date: string, index: number) =>
  index % dateLabelStep.value === 0 ? date : ''

// 折线坐标计算抽到 @/utils/trendChart 并配了单测——「无执行的日期要断线」这条
// 有边界情况（空档、孤点、全空），放在组件里没法验，靠真实数据也不容易碰到
const rateSegments = computed(() => buildRateSegments(props.data?.trend ?? []))
const rateDots = computed(() => buildRateDots(props.data?.trend ?? []))
// 缺陷进出曲线：与执行柱共用同一 x 轴，按各自窗口最大值归一化（全程 0 时无段可画）
const defectCreatedSegments = computed(() =>
  buildCountSegments((props.data?.trend ?? []).map((p) => p.defectsCreated ?? 0))[0] ?? [])
const defectClosedSegments = computed(() =>
  buildCountSegments((props.data?.trend ?? []).map((p) => p.defectsClosed ?? 0))[0] ?? [])
</script>

<style scoped>
/* ---------------------------------------------------------------- 趋势图 */
/* 页头占两行（标题+开关 / 图例），只在这一个面板上用，不动共用的 .panel-header */
.panel-header-stacked {
  flex-direction: column;
  align-items: stretch;
  gap: 6px;
}

.panel-header-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.panel-header-top .panel-title {
  white-space: nowrap;
}

.trend-range {
  margin-left: 12px;
}

/* 图例整体可以换行，但**每一项内部绝不折行**：
   否则「通过率」会被压成"通/过/率"竖排——这正是之前那版拥挤时的样子 */
.legend {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 14px;
  font-size: 12px;
  color: #7a8699;
}

.legend-item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  white-space: nowrap;
  flex-shrink: 0;
}

.dot {
  display: inline-block;
  width: 9px;
  height: 9px;
  border-radius: 3px;
}

.dot-pass {
  background: linear-gradient(180deg, #7dd47f, #4caf50);
}

.dot-fail {
  background: linear-gradient(180deg, #ff8f8f, #e85454);
}

/* 图例里的折线示意（与圆点同色，一眼能对上） */
.line-sample {
  display: inline-block;
  width: 14px;
  height: 0;
  border-top: 2px solid #e8952f;
  vertical-align: middle;
}

/* 缺陷两条曲线分开展示，颜色与图中 stroke 一一对应。
   原来合成一条（上绿下红双边框）看着像"一条线两种颜色"，反而对不上图。 */
.line-created {
  border-top-style: dashed;
  border-top-color: #f56c6c;
}

.line-closed {
  border-top-style: dashed;
  border-top-color: #67c23a;
  height: 3px;
}

.chart-area {
  position: relative;
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  padding-top: 6px;
}

/* 水平网格线，便于估读数值 */
.chart-grid {
  position: absolute;
  left: 0;
  right: 0;
  top: 20px;
  bottom: 24px;
  pointer-events: none;
}

.grid-line {
  position: absolute;
  left: 0;
  right: 0;
  border-top: 1px dashed #e8eef7;
}

/* 注意：这里**不能**用 gap —— 折线的横坐标按「列宽等分」算（(i+0.5)/n），
   有 gap 的话每列实际中心会偏移，圆点就落不到柱子正上方。
   改用列内 padding 造出等量留白：4px×2 = 原来的 6px gap + 1px×2 padding。 */
.trend-chart {
  position: relative;
  flex: 1;
  min-height: 0;
  display: flex;
  align-items: stretch;
}

.trend-col {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: flex-end;
  border-radius: 6px;
  padding: 0 4px;
  transition: background-color 0.15s ease;
}

/* 折线叠加层：与网格线用同一套定位，因此与柱子绘图区严格对齐 */
.trend-overlay {
  position: absolute;
  left: 0;
  right: 0;
  top: 20px;
  bottom: 24px;
  pointer-events: none;
}

.trend-svg {
  width: 100%;
  height: 100%;
  overflow: visible;
}

.rate-dot {
  position: absolute;
  width: 6px;
  height: 6px;
  margin: -3px 0 0 -3px;
  border-radius: 50%;
  background: #e8952f;
  box-shadow: 0 0 0 1.5px #fff;
}

.trend-col:hover {
  background-color: #f6f9fe;
}

.trend-total {
  height: 20px;
  font-size: 11px;
  color: #7a8699;
  font-variant-numeric: tabular-nums;
}

.trend-bars {
  flex: 1;
  min-height: 0;
  width: 100%;
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  align-items: center;
}

.bar {
  width: 56%;
  max-width: 26px;
  border-radius: 3px 3px 0 0;
  transition: filter 0.15s ease;
}

.bar-pass {
  background: linear-gradient(180deg, #7dd47f, #4caf50);
}

.bar-fail {
  background: linear-gradient(180deg, #ff8f8f, #e85454);
  margin-bottom: 1px;
}

.trend-col:hover .bar {
  filter: brightness(1.06);
}

.trend-date {
  height: 24px;
  line-height: 24px;
  font-size: 11px;
  color: #96a1b3;
  white-space: nowrap;
}
</style>
