<template>
  <div class="share-page">
    <div v-if="loading" class="share-state">正在加载报告…</div>

    <div v-else-if="error" class="share-state share-error">
      <el-icon :size="42" class="state-icon"><WarningFilled /></el-icon>
      <div class="state-title">{{ error }}</div>
      <div class="state-hint">报告链接可能有有效期限制，或已被出具者吊销。</div>
    </div>

    <template v-else-if="report">
      <header class="report-header">
        <div class="header-main">
          <h1 class="report-title">{{ report.title }}</h1>
          <div class="report-subtitle">{{ report.subtitle }}</div>
        </div>
        <div class="header-meta">
          <div class="meta-line">生成时间：{{ formatDateTime(report.generatedAt) }}</div>
          <div v-if="report.expiresAt" class="meta-line">链接有效期至：{{ formatDateTime(report.expiresAt) }}</div>
          <div v-else class="meta-line">链接长期有效</div>
          <el-tag v-if="report.overview.browsers.length > 1" size="small" type="primary" class="browser-tag">
            浏览器矩阵：{{ report.overview.browsers.join(' / ') }}
          </el-tag>
          <!-- 免登录下载：邮件里给的就是这个页面的链接，所以下载入口必须在这里，
               否则"能在线看却不能下载"，用户还得回头找发件人要附件 -->
          <el-button class="download-btn" :loading="downloading" @click="downloadReport">
            <el-icon><Download /></el-icon>
            <span>下载 Excel 报告</span>
          </el-button>
        </div>
      </header>

      <div v-if="report.missing" class="share-state share-error">
        <el-icon :size="42" class="state-icon"><WarningFilled /></el-icon>
        <div class="state-title">报告数据不存在或已被删除</div>
      </div>

      <template v-else>
        <!-- 概览 -->
        <section class="overview-grid">
          <div v-for="card in overviewCards" :key="card.label" class="overview-card" :class="`tone-${card.tone}`">
            <div class="card-value">{{ card.value }}</div>
            <div class="card-label">{{ card.label }}</div>
          </div>
        </section>

        <!-- 趋势 -->
        <section v-if="report.trend.length > 0" class="panel">
          <div class="panel-title">执行趋势</div>
          <div class="trend">
            <div v-for="point in report.trend" :key="point.date" class="trend-column">
              <div class="trend-total">{{ point.total }}</div>
              <div class="trend-bar-wrap">
                <div class="trend-bar trend-bar-failed" :style="barStyle(point.failed)"
                  :title="`失败 ${point.failed}`" />
                <div class="trend-bar trend-bar-passed" :style="barStyle(point.passed)"
                  :title="`通过 ${point.passed}`" />
              </div>
              <div class="trend-label">{{ point.date }}</div>
            </div>
          </div>
        </section>

        <!-- 模块汇总 -->
        <section v-if="report.modules.length > 0" class="panel">
          <div class="panel-title">模块汇总</div>
          <table class="summary-table">
            <thead>
              <tr>
                <th>模块</th>
                <th class="num">用例数</th>
                <th class="num">通过</th>
                <th class="num">未通过</th>
                <th class="num">通过率</th>
                <th class="rate-col">占比</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="m in report.modules" :key="m.module">
                <td>{{ m.module }}</td>
                <td class="num">{{ m.total }}</td>
                <td class="num pass-text">{{ m.passed }}</td>
                <td class="num" :class="{ 'fail-text': m.failed > 0 }">{{ m.failed }}</td>
                <td class="num">{{ m.passRate }}%</td>
                <td class="rate-col">
                  <div class="rate-bar">
                    <div class="rate-bar-inner" :style="{ width: `${Math.min(100, m.passRate)}%` }" />
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </section>

        <!-- flake 排行 -->
        <section v-if="report.flakeRank.length > 0" class="panel">
          <div class="panel-title">不稳定用例排行（flake）</div>
          <div class="flake-list">
            <div v-for="item in report.flakeRank" :key="item.testCaseId" class="flake-item">
              <span class="flake-name">{{ item.name }}</span>
              <span class="flake-rate">不稳定度 {{ Math.round(item.flakeRate * 100) }}%</span>
              <span class="flake-count">近 {{ item.executions }} 次执行</span>
            </div>
          </div>
        </section>

        <!-- 用例明细 -->
        <section class="panel">
          <div class="panel-title">
            用例明细
            <span class="panel-hint">共 {{ report.cases.length }} 条</span>
          </div>
          <div class="case-list">
            <div v-for="(item, index) in report.cases" :key="item.executionId ?? index" class="case-item">
              <div class="case-head" @click="toggle(index)">
                <el-icon class="case-arrow" :class="{ 'is-open': expanded.has(index) }">
                  <ArrowRight />
                </el-icon>
                <el-tag :type="statusTagType(item.status)" size="small">{{ statusLabels[item.status] }}</el-tag>
                <span class="case-name">{{ item.name }}</span>
                <span v-if="item.caseCode" class="case-code">{{ item.caseCode }}</span>
                <span v-if="item.priority" class="case-priority">{{ item.priority }}</span>
                <span v-if="item.browserName" class="case-tag">{{ browserText(item.browserName) }}</span>
                <span v-if="item.dataSetRowLabel" class="case-tag">{{ item.dataSetRowLabel }}</span>
                <span class="case-duration">{{ item.durationMs != null ? `${item.durationMs} ms` : '—' }}</span>
                <span class="case-history">
                  <span v-for="(point, i) in (item.history ?? []).slice(-8)" :key="i" class="history-dot"
                    :class="`dot-${point.status}`" :title="`${formatDateTime(point.at)} ${statusLabels[point.status]}`" />
                </span>
              </div>

              <div v-if="expanded.has(index)" class="case-body">
                <div v-if="item.errorMessage" class="error-block">
                  <div class="block-title">失败原因</div>
                  <pre class="block-pre">{{ item.errorMessage }}</pre>
                </div>

                <div v-if="item.aiDiagnosis" class="diag-block">
                  <div class="block-title">AI 诊断</div>
                  <div class="block-text">{{ item.aiDiagnosis }}</div>
                  <div v-if="item.aiSuggestedFix" class="block-text fix-text">
                    建议：{{ item.aiSuggestedFix }}
                  </div>
                </div>

                <div v-if="item.sourceSteps || item.expectedResult" class="meta-block">
                  <div v-if="item.sourceSteps" class="meta-row">
                    <span class="meta-label">操作步骤</span>
                    <span class="meta-value">{{ item.sourceSteps }}</span>
                  </div>
                  <div v-if="item.expectedResult" class="meta-row">
                    <span class="meta-label">预期结果</span>
                    <span class="meta-value">{{ item.expectedResult }}</span>
                  </div>
                </div>

                <table v-if="item.steps && item.steps.length > 0" class="step-table">
                  <thead>
                    <tr>
                      <th class="step-order">#</th>
                      <th>动作</th>
                      <th>结果</th>
                      <th>耗时</th>
                      <th>证据</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="step in item.steps" :key="step.stepOrder">
                      <td class="step-order">{{ step.stepOrder + 1 }}</td>
                      <td>{{ step.actionType }}</td>
                      <td>
                        <el-tag :type="statusTagType(step.status)" size="small">
                          {{ statusLabels[step.status] }}
                        </el-tag>
                        <el-tag v-if="step.visualStatus === 2" size="small" type="danger" class="step-tag">
                          视觉变化 {{ visualPercent(step.visualDiffRatio) }}
                        </el-tag>
                        <el-tag v-else-if="step.visualStatus === 1" size="small" type="success" class="step-tag">
                          视觉一致
                        </el-tag>
                        <el-tag v-else-if="step.visualStatus === 0" size="small" type="info" class="step-tag">
                          新建基线
                        </el-tag>
                      </td>
                      <td>{{ step.durationMs != null ? `${step.durationMs} ms` : '—' }}</td>
                      <td>
                        <div class="evidence">
                          <el-image v-if="step.screenshotUrl" :src="step.screenshotUrl" :preview-src-list="previewList(step)"
                            fit="cover" class="thumb" preview-teleported />
                          <span v-else class="muted">—</span>
                        </div>
                      </td>
                    </tr>
                  </tbody>
                </table>

                <div v-for="step in visualSteps(item)" :key="`v${step.stepOrder}`" class="visual-block">
                  <div class="block-title">
                    视觉差异 · 步骤 {{ step.stepOrder + 1 }}（{{ visualPercent(step.visualDiffRatio) }}）
                  </div>
                  <div class="visual-compare">
                    <figure>
                      <el-image :src="step.baselineImageUrl" fit="contain" class="compare-img" />
                      <figcaption>基线</figcaption>
                    </figure>
                    <figure>
                      <el-image :src="step.screenshotUrl" fit="contain" class="compare-img" />
                      <figcaption>本次实际</figcaption>
                    </figure>
                    <figure v-if="step.diffImageUrl">
                      <el-image :src="step.diffImageUrl" fit="contain" class="compare-img" />
                      <figcaption>差异高亮</figcaption>
                    </figure>
                  </div>
                  <div v-if="step.visualNote" class="visual-note">{{ step.visualNote }}</div>
                </div>
              </div>
            </div>
          </div>
        </section>
      </template>

      <footer class="report-footer">
        由 AI 自动化测试平台生成 · 只读报告
      </footer>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ArrowRight, Download, WarningFilled } from '@element-plus/icons-vue'
import axios from 'axios'
import { formatDateTime } from '@/utils/formatter'
import type { PublicReport, ReportStep } from '@/types/share'

const route = useRoute()
const loading = ref(true)
const error = ref('')
const report = ref<PublicReport | null>(null)
const expanded = ref(new Set<number>())

const statusLabels: Record<number, string> = {
  0: '等待中', 1: '执行中', 2: '通过', 3: '失败', 4: '错误', 5: '跳过',
}

const statusTagType = (status: number) =>
  ({ 2: 'success', 3: 'danger', 4: 'danger', 1: 'primary', 0: 'info', 5: 'info' }[status] ?? 'info') as
  'success' | 'danger' | 'primary' | 'info'

const browserText = (name?: string | null) =>
  ({ chromium: 'Chromium', firefox: 'Firefox', webkit: 'WebKit' }[name ?? ''] ?? name ?? '')

const visualPercent = (ratio?: number | null) =>
  ratio == null ? '—' : `${(ratio * 100).toFixed(2)}%`

const overviewCards = computed(() => {
  const o = report.value?.overview
  if (!o) return []
  return [
    { label: '用例总数', value: o.total, tone: 'blue' },
    { label: '通过', value: o.passed, tone: 'green' },
    { label: '未通过', value: o.failed + o.error, tone: 'red' },
    { label: '通过率', value: `${o.passRate}%`, tone: 'cyan' },
    { label: '总耗时', value: formatDuration(o.durationMs), tone: 'violet' },
  ]
})

const formatDuration = (ms?: number | null) => {
  if (!ms) return '—'
  if (ms < 1000) return `${ms} ms`
  if (ms < 60000) return `${(ms / 1000).toFixed(1)} s`
  return `${Math.floor(ms / 60000)} 分 ${Math.round((ms % 60000) / 1000)} 秒`
}

const maxTrendTotal = computed(() =>
  Math.max(1, ...(report.value?.trend ?? []).map((p) => p.total)))

const barStyle = (count: number) => ({
  height: count > 0 ? `${Math.max(4, (count / maxTrendTotal.value) * 100)}%` : '0',
})

const visualSteps = (item: { steps?: ReportStep[] | null }) =>
  (item.steps ?? []).filter((s) => s.visualStatus === 2 && s.diffImageUrl)

const previewList = (step: ReportStep) => {
  const list: string[] = []
  if (step.baselineImageUrl) list.push(step.baselineImageUrl)
  if (step.screenshotUrl) list.push(step.screenshotUrl)
  if (step.diffImageUrl) list.push(step.diffImageUrl)
  return list
}

const toggle = (index: number) => {
  const next = new Set(expanded.value)
  if (next.has(index)) next.delete(index)
  else next.add(index)
  expanded.value = next
}

const downloading = ref(false)

/**
 * 免登录下载 Excel 报告。
 * 用 axios 拿 blob 而不是 `<a href>`：后者带不上错误信息，
 * 链接失效时会静默跳到浏览器展示一段 JSON 错误页，用户只会以为"点坏了"。
 */
const downloadReport = async () => {
  const token = route.params.token as string
  downloading.value = true
  try {
    const { data } = await axios.get<Blob>(`/api/public/reports/${token}/export`, {
      responseType: 'blob',
      timeout: 120000,
    })
    const url = URL.createObjectURL(data)
    const link = document.createElement('a')
    link.href = url
    link.download = `${report.value?.title ?? '测试报告'}.xlsx`
    document.body.appendChild(link)
    link.click()
    link.remove()
    URL.revokeObjectURL(url)
  } catch (err) {
    const response = (err as { response?: { status?: number } }).response
    if (response?.status === 410) error.value = '报告链接已过期，无法下载'
    else if (response?.status === 404) error.value = '报告链接无效或已被吊销'
    else error.value = '报告下载失败，请稍后重试'
  } finally {
    downloading.value = false
  }
}

onMounted(async () => {
  const token = route.params.token as string
  try {
    // 免登录接口：不走带鉴权拦截器的实例，错误自行处理（404 失效 / 410 过期）
    const { data } = await axios.get<PublicReport>(`/api/public/reports/${token}`, { timeout: 30000 })
    report.value = data
  } catch (err) {
    const response = (err as { response?: { status?: number } }).response
    if (response?.status === 410) error.value = '报告链接已过期'
    else if (response?.status === 404) error.value = '报告链接无效或已被吊销'
    else error.value = '报告加载失败，请稍后重试'
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
.share-page {
  min-height: 100vh;
  background: #f5f7fa;
  padding: 24px;
  color: #1f2329;
}

.share-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 10px;
  padding: 80px 0;
  color: #606266;
}

.share-error .state-icon {
  color: #e6a23c;
}

.state-title {
  font-size: 17px;
  font-weight: 600;
  color: #303133;
}

.state-hint {
  font-size: 13px;
  color: #909399;
}

.report-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;
  gap: 24px;
  background: #fff;
  border-radius: 6px;
  padding: 20px 24px;
  margin-bottom: 16px;
  box-shadow: 0 1px 3px rgb(0 0 0 / 6%);
}

.report-title {
  margin: 0 0 6px;
  font-size: 22px;
  font-weight: 600;
}

.report-subtitle {
  font-size: 13px;
  color: #606266;
}

.header-meta {
  text-align: right;
  font-size: 12px;
  color: #909399;
  line-height: 1.9;
}

.browser-tag {
  margin-top: 4px;
}

.download-btn {
  margin-top: 8px;
}

/* 下载按钮里的图标与文字之间留一点空隙（el-button 两个子元素之间默认无间距） */
.download-btn .el-icon + span {
  margin-left: 5px;
}

.overview-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 12px;
  margin-bottom: 16px;
}

.overview-card {
  background: #fff;
  border-radius: 6px;
  padding: 16px;
  border-left: 3px solid var(--tone);
  box-shadow: 0 1px 3px rgb(0 0 0 / 6%);
}

.card-value {
  font-size: 24px;
  font-weight: 600;
  color: var(--tone);
}

.card-label {
  margin-top: 4px;
  font-size: 12px;
  color: #909399;
}

.tone-blue { --tone: #1f3b73; }
.tone-green { --tone: #1f7a33; }
.tone-red { --tone: #b3261e; }
.tone-cyan { --tone: #0e7490; }
.tone-violet { --tone: #6d28d9; }

.panel {
  background: #fff;
  border-radius: 6px;
  padding: 16px 20px;
  margin-bottom: 16px;
  box-shadow: 0 1px 3px rgb(0 0 0 / 6%);
}

.panel-title {
  font-size: 15px;
  font-weight: 600;
  margin-bottom: 12px;
}

.panel-hint {
  margin-left: 8px;
  font-size: 12px;
  font-weight: 400;
  color: #909399;
}

.trend {
  display: flex;
  align-items: flex-end;
  gap: 10px;
  height: 170px;
  overflow-x: auto;
}

.trend-column {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: 40px;
  height: 100%;
}

.trend-total {
  font-size: 11px;
  color: #909399;
}

.trend-bar-wrap {
  flex: 1;
  width: 22px;
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  gap: 1px;
}

.trend-bar {
  width: 100%;
  border-radius: 2px 2px 0 0;
  transition: height 0.3s;
}

.trend-bar-passed {
  background: linear-gradient(180deg, #34a853, #1f7a33);
}

.trend-bar-failed {
  background: linear-gradient(180deg, #e3574d, #b3261e);
}

.trend-label {
  margin-top: 4px;
  font-size: 11px;
  color: #909399;
}

.summary-table,
.step-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.summary-table th,
.summary-table td,
.step-table th,
.step-table td {
  border-bottom: 1px solid #ebeef5;
  padding: 7px 8px;
  text-align: left;
}

.summary-table th,
.step-table th {
  background: #fafafa;
  font-weight: 500;
  color: #606266;
}

.num {
  text-align: right;
  width: 80px;
}

.rate-col {
  width: 140px;
}

.rate-bar {
  height: 6px;
  background: #f0f2f5;
  border-radius: 3px;
  overflow: hidden;
}

.rate-bar-inner {
  height: 100%;
  background: linear-gradient(90deg, #34a853, #1f7a33);
}

.pass-text {
  color: #1f7a33;
}

.fail-text {
  color: #b3261e;
  font-weight: 600;
}

.flake-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.flake-item {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 13px;
}

.flake-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.flake-rate {
  color: #b3261e;
}

.flake-count {
  color: #909399;
  font-size: 12px;
}

.case-list {
  display: flex;
  flex-direction: column;
}

.case-item {
  border-bottom: 1px solid #ebeef5;
}

.case-head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 9px 4px;
  cursor: pointer;
  font-size: 13px;
}

.case-head:hover {
  background: #fafcff;
}

.case-arrow {
  transition: transform 0.2s;
  color: #909399;
}

.case-arrow.is-open {
  transform: rotate(90deg);
}

.case-name {
  font-weight: 500;
}

.case-code,
.case-tag,
.case-priority {
  font-size: 12px;
  color: #909399;
}

.case-priority {
  padding: 0 5px;
  border: 1px solid #dcdfe6;
  border-radius: 3px;
}

.case-duration {
  margin-left: auto;
  font-size: 12px;
  color: #909399;
}

.case-history {
  display: flex;
  gap: 3px;
}

.history-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #dcdfe6;
}

.dot-2 { background: #1f7a33; }
.dot-3 { background: #b3261e; }
.dot-4 { background: #e6a23c; }
.dot-5 { background: #c0c4cc; }

.case-body {
  padding: 8px 4px 16px 26px;
}

.block-title {
  font-size: 12px;
  font-weight: 600;
  color: #606266;
  margin: 8px 0 4px;
}

.block-pre {
  margin: 0;
  padding: 10px;
  background: #fafafa;
  border-radius: 4px;
  font-size: 12px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-all;
  color: #b3261e;
}

.block-text {
  font-size: 13px;
  line-height: 1.7;
  color: #303133;
}

.fix-text {
  color: #1f7a33;
}

.meta-block {
  margin-top: 6px;
}

.meta-row {
  display: flex;
  gap: 8px;
  font-size: 13px;
  line-height: 1.7;
}

.meta-label {
  flex: none;
  width: 66px;
  color: #909399;
}

.step-table {
  margin-top: 10px;
}

.step-order {
  width: 40px;
  text-align: center;
}

.step-tag {
  margin-left: 6px;
}

.evidence {
  display: flex;
  gap: 6px;
}

.thumb {
  width: 72px;
  height: 46px;
  border: 1px solid #ebeef5;
  border-radius: 3px;
}

.visual-block {
  margin-top: 12px;
}

.visual-compare {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
}

.visual-compare figure {
  margin: 0;
}

.compare-img {
  width: 100%;
  height: 170px;
  border: 1px solid #ebeef5;
  border-radius: 4px;
  background: #fafafa;
}

.visual-compare figcaption {
  margin-top: 4px;
  text-align: center;
  font-size: 12px;
  color: #909399;
}

.visual-note {
  margin-top: 8px;
  padding: 8px 10px;
  background: #fff8e6;
  border-radius: 4px;
  font-size: 12px;
  line-height: 1.7;
}

.muted {
  color: #c0c4cc;
}

.report-footer {
  text-align: center;
  font-size: 12px;
  color: #a8abb2;
  padding: 16px 0 8px;
}
</style>
