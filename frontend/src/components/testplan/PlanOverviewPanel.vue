<template>
  <div>
    <el-descriptions v-if="plan" :column="3" border class="overview">
      <!-- 所属项目放在最前：它是计划的硬边界（计划创建后不能换项目），
           也应该是最先被确认的一件事实 -->
      <el-descriptions-item label="所属项目">
        <el-button v-if="plan.projectName" link type="primary" class="project-link"
          @click="router.push(`/projects/${plan.projectId}`)">
          {{ plan.projectName }}
        </el-button>
        <span v-else class="muted">—</span>
      </el-descriptions-item>
      <el-descriptions-item label="状态">
        <el-tag :type="planStatusTagType(plan.status)" size="small">{{ STATUS_LABELS[plan.status] }}</el-tag>
      </el-descriptions-item>
      <el-descriptions-item label="版本标识">{{ plan.releaseName || '—' }}</el-descriptions-item>
      <el-descriptions-item label="负责人">{{ plan.ownerName || '—' }}</el-descriptions-item>
      <el-descriptions-item label="周期">
        {{ formatDate(plan.startsAt) }} ~ {{ formatDate(plan.endsAt) }}
      </el-descriptions-item>
      <el-descriptions-item label="目标通过率">
        {{ (plan.targetPassRate * 100).toFixed(0) }}%
      </el-descriptions-item>
      <el-descriptions-item label="达标口径">
        {{ plan.gateMode === 1 ? '任意一轮达标' : '最后一轮达标' }}
      </el-descriptions-item>
      <el-descriptions-item label="质量口径">
        {{ plan.allowErrors ? '允许 Error' : '不允许 Error' }} ·
        {{ plan.excludeFlakyFromFailure ? '排除 flaky 失败' : '不排除 flaky' }}
      </el-descriptions-item>
      <el-descriptions-item label="缺陷门槛">
        <el-tag :type="plan.defectGateEnabled ? 'danger' : 'info'" size="small" effect="plain">
          {{ plan.defectGateEnabled ? '致命/严重未闭环=0 才达标' : '未启用' }}
        </el-tag>
      </el-descriptions-item>
      <el-descriptions-item label="默认环境">{{ plan.environmentName || '按用例地址' }}</el-descriptions-item>
      <el-descriptions-item label="浏览器矩阵">
        {{ plan.browsers.length > 0 ? plan.browsers.join(' / ') : '按用例逐条解析' }}
      </el-descriptions-item>
      <el-descriptions-item label="范围用例数">{{ plan.caseCount }}</el-descriptions-item>
      <el-descriptions-item label="轮次数">{{ plan.roundCount }}</el-descriptions-item>
      <el-descriptions-item label="数据驱动">
        {{ plan.expandDataSets ? '按数据行展开' : '不展开' }}
      </el-descriptions-item>
      <el-descriptions-item label="定时任务" :span="3">
        <template v-if="plan.schedules && plan.schedules.length > 0">
          <el-tag v-for="sc in plan.schedules" :key="sc.id" size="small"
            :type="sc.enabled ? 'success' : 'info'" effect="plain" class="ml-6">
            {{ sc.name }}（{{ sc.cronExpression }}）{{ sc.enabled ? '' : ' 已停用' }}
          </el-tag>
        </template>
        <span v-else class="muted">
          未被任何定时任务引用。如需按 Cron 自动开轮，请到
          <el-link type="primary" :underline="false" @click="router.push('/schedules')">
            定时任务
          </el-link>
          页把「执行范围」设为「测试计划」并选中本计划。
        </span>
      </el-descriptions-item>
      <el-descriptions-item label="描述" :span="3">{{ plan.description || '—' }}</el-descriptions-item>
    </el-descriptions>

    <div class="round-actions">
      <el-button type="primary" :icon="VideoPlay" :loading="starting"
        :disabled="!plan || plan.caseCount === 0" @click="emit('start')">
        开新一轮
      </el-button>
      <span class="round-hint">
        同一计划同时只能有一轮进行中；开轮后会固化当轮的范围快照，之后编辑用例不影响历史轮次。
      </span>
    </div>

    <el-alert v-if="runningRound" type="info" :closable="false" class="running-tip">
      <template #title>
        第 {{ runningRound.roundNo }} 轮进行中：{{ runningRound.stats.passed }}/{{
          runningRound.stats.total }} 已完成
      </template>
      <template #default>
        <el-progress :percentage="roundPercent(runningRound)" :stroke-width="12" class="running-progress" />
        <el-button link type="danger" @click="emit('abort', runningRound)">中止本轮</el-button>
      </template>
    </el-alert>
  </div>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { VideoPlay } from '@element-plus/icons-vue'
import { TEST_PLAN_STATUS_LABELS as STATUS_LABELS, type PlanRoundSummary, type TestPlanDetail } from '@/types/testPlan'
import { planStatusTagType, roundPercent } from '@/utils/planRound'

defineProps<{
  plan: TestPlanDetail | null
  runningRound: PlanRoundSummary | null
  /** 开轮请求进行中（按钮 loading） */
  starting: boolean
}>()

const emit = defineEmits<{ start: []; abort: [round: PlanRoundSummary] }>()

const router = useRouter()

const formatDate = (value?: string | null) => (value ? value.slice(0, 10) : '未设置')
</script>

<style scoped>
.overview { margin-bottom: 16px; }

/* 标签列加宽：「所属项目」「目标通过率」这类 5 字标签不再折行 */
.overview :deep(.el-descriptions__label) {
  width: 104px;
  min-width: 104px;
}
.overview :deep(.el-descriptions__content) {
  min-width: 180px;
}

.round-actions { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; }
.round-hint { color: #9aa2ae; font-size: 12px; }
.running-tip { margin-top: 4px; }
.running-progress { margin: 8px 0; }

.ml-6 { margin-left: 6px; }
.muted { color: #9aa2ae; }
</style>
