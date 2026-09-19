<template>
  <div>
    <el-empty v-if="!report || report.trends.length === 0" description="还没有可汇总的执行数据" />

    <template v-else>
      <div class="report-grid">
        <div class="stat-card tone-total">
          <div class="stat-value">{{ report.gate.stats.total }}</div>
          <div class="stat-label">参与判定样本</div>
          <div class="stat-note">已扣除跳过与排除的 flaky</div>
        </div>
        <div class="stat-card tone-ok">
          <div class="stat-value ok">{{ report.gate.stats.passed }}</div>
          <div class="stat-label">通过</div>
        </div>
        <div class="stat-card tone-bad">
          <div class="stat-value bad">{{ report.gate.stats.failed }}</div>
          <div class="stat-label">失败</div>
        </div>
        <div class="stat-card tone-warn">
          <div class="stat-value warn">{{ report.gate.stats.error }}</div>
          <div class="stat-label">错误</div>
        </div>
        <div class="stat-card" :class="report.gate.passed ? 'tone-ok' : 'tone-bad'">
          <div class="stat-value" :class="report.gate.passed ? 'ok' : 'bad'">
            {{ (report.gate.stats.passRate * 100).toFixed(1) }}%
          </div>
          <div class="stat-label">通过率</div>
          <div class="stat-note">
            目标 {{ (report.gate.targetPassRate * 100).toFixed(0) }}% · {{ report.gate.passed ? '已达标' : '未达标' }}
          </div>
        </div>
      </div>

      <el-divider content-position="left">轮次趋势</el-divider>
      <el-table :data="report.trends" size="small">
        <el-table-column label="轮次" width="80">
          <template #default="{ row }">第 {{ row.roundNo }} 轮</template>
        </el-table-column>
        <el-table-column label="通过率" min-width="240">
          <template #default="{ row }">
            <el-progress :percentage="Math.round(row.passRate * 100)"
              :status="row.gatePassed ? 'success' : 'exception'" :stroke-width="12" />
          </template>
        </el-table-column>
        <el-table-column label="通过/失败/错误/跳过" width="200">
          <template #default="{ row }">
            {{ row.passed }} / {{ row.failed }} / {{ row.error }} / {{ row.skipped }}
          </template>
        </el-table-column>
        <el-table-column label="完成时间" width="180">
          <template #default="{ row }">{{ formatDateTime(row.completedAt) }}</template>
        </el-table-column>
      </el-table>

      <el-divider content-position="left">模块通过率</el-divider>
      <el-table :data="report.modules" size="small">
        <el-table-column prop="module" label="模块" min-width="180" />
        <el-table-column label="通过率" min-width="220">
          <template #default="{ row }">
            <el-progress :percentage="Math.round(row.passRate * 100)"
              :status="row.passRate >= targetPassRate ? 'success' : 'exception'"
              :stroke-width="12" />
          </template>
        </el-table-column>
        <el-table-column label="通过/失败/错误/跳过" width="200">
          <template #default="{ row }">
            {{ row.passed }} / {{ row.failed }} / {{ row.error }} / {{ row.skipped }}
          </template>
        </el-table-column>
      </el-table>

      <el-divider content-position="left">
        阻断达标的用例（{{ report.blockingCases.length }}）
      </el-divider>
      <el-empty v-if="report.blockingCases.length === 0" description="没有阻断用例" :image-size="60" />
      <el-table v-else :data="report.blockingCases" size="small">
        <el-table-column label="用例名称" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">
            {{ row.name }}
            <el-tag v-if="row.isFlaky" type="warning" size="small" effect="plain" class="ml-6">flaky</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="模块" width="140">
          <template #default="{ row }">{{ row.module || '—' }}</template>
        </el-table-column>
        <el-table-column label="状态" width="80">
          <template #default="{ row }">
            <el-tag :type="row.status === 3 ? 'warning' : 'danger'" size="small" effect="plain">
              {{ row.status === 3 ? '错误' : '失败' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="错误信息" min-width="320"  class-name="wrap-cell">
          <template #default="{ row }">{{ row.errorMessage || '—' }}</template>
        </el-table-column>
      </el-table>
    </template>
  </div>
</template>

<script setup lang="ts">
import { formatDateTime } from '@/utils/formatter'
import type { TestPlanReport } from '@/types/testPlan'

defineProps<{
  report: TestPlanReport | null
  /** 目标通过率：进度条达标配色的判定线 */
  targetPassRate: number
}>()
</script>

<style scoped>
.report-grid { display: grid; grid-template-columns: repeat(5, 1fr); gap: 12px; margin-bottom: 8px; }

/* 窄屏：5 个报表指标并排会把每个压到 ~60px，数字都显示不全 —— 改成两列 */
@media (max-width: 767px) {
  .report-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
.stat-card {
  position: relative;
  overflow: hidden;
  padding: 18px 12px 14px;
  text-align: center;
  background: #fff;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  transition: transform 0.18s ease, box-shadow 0.18s ease;
}

/* 淡彩边框 + 同色浅底：边框随主题色走，比灰边更有辨识度又不抢数字 */
.tone-total {
  border-color: rgba(63, 127, 212, 0.38);
  background: linear-gradient(180deg, #f6faff 0%, #fff 46%);
}

.tone-ok {
  border-color: rgba(103, 194, 58, 0.45);
  background: linear-gradient(180deg, #f6fcf3 0%, #fff 46%);
}

.tone-bad {
  border-color: rgba(245, 108, 108, 0.45);
  background: linear-gradient(180deg, #fef5f5 0%, #fff 46%);
}

.tone-warn {
  border-color: rgba(230, 162, 60, 0.5);
  background: linear-gradient(180deg, #fdf8ef 0%, #fff 46%);
}

/* hover 轻浮起，让"这是可看的卡片"有反馈 */
.stat-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 14px rgba(31, 59, 115, 0.10);
}

/* 顶部 3px 主题色条：扫一眼就能按颜色分辨指标含义 */
.stat-card::before {
  content: '';
  position: absolute;
  inset: 0 0 auto;
  height: 3px;
}

.tone-total::before { background: linear-gradient(90deg, #1f3b73, #3f7fd4); }
.tone-ok::before    { background: var(--el-color-success); }
.tone-bad::before   { background: var(--el-color-danger); }
.tone-warn::before  { background: var(--el-color-warning); }

.stat-value {
  font-size: 30px;
  font-weight: 600;
  line-height: 1.2;
  font-variant-numeric: tabular-nums;
}

.stat-label { color: #7a8290; font-size: 13px; margin-top: 4px; }
.stat-note { color: #b4b2a9; font-size: 11px; margin-top: 2px; }

.ok   { color: var(--el-color-success); }
.bad  { color: var(--el-color-danger); }
.warn { color: var(--el-color-warning); }
.ml-6 { margin-left: 6px; }
.wrap-cell .cell {
  white-space: normal !important;
  word-break: break-all;
}
</style>
