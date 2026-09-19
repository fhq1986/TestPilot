<template>
  <el-dialog v-model="visible" title="批量执行" width="480px">
    <div class="batch-body">
      <p>将按顺序入队 <b>{{ selectedRows.length }}</b> 条用例（当前队列串行执行）。</p>
      <el-select v-model="environmentId" placeholder="选择执行环境（可选）" clearable class="w-full">
        <el-option label="不使用环境" value="" />
        <el-option v-for="env in environments" :key="env.id" :label="`${env.name}（${env.baseUrl}）`" :value="env.id" />
      </el-select>
      <div class="batch-field">
        <div class="batch-label">浏览器矩阵</div>
        <el-select v-model="browsers" multiple placeholder="不指定则按环境/用例配置" class="w-full">
          <el-option v-for="b in BROWSER_OPTIONS" :key="b.id" :label="b.label" :value="b.id" />
        </el-select>
        <div class="batch-hint">多选时同一条用例会在每个浏览器上各跑一次</div>
      </div>
      <div class="batch-field">
        <el-checkbox v-model="expandDataSets">按数据行展开（用例绑定数据集时）</el-checkbox>
      </div>
    </div>
    <template #footer>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="primary" :loading="executing" @click="handleExecute">开始执行</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { batchExecute } from '@/api/execution'
import { getEnvironments } from '@/api/environment'
import { BROWSER_OPTIONS, type TestCaseSummary } from '@/types/testcase'
import type { EnvironmentView } from '@/types/environment'

const props = defineProps<{
  /** 勾选的用例行：决定入队数量，也作为未筛项目时取环境的兜底 */
  selectedRows: TestCaseSummary[]
  /** 当前项目筛选：环境按项目隔离，有值时按它拉环境列表 */
  projectId: string
}>()

const router = useRouter()

const visible = ref(false)
const executing = ref(false)
const environmentId = ref('')
const browsers = ref<string[]>([])
const expandDataSets = ref(true)
const environments = ref<EnvironmentView[]>([])

const loadEnvironments = async () => {
  if (environments.value.length > 0) return
  // 环境按项目隔离：取当前筛选项目的环境；未筛选时取第一条选中用例的项目兜底
  const targetProjectId = props.projectId || props.selectedRows[0]?.projectId
  if (!targetProjectId) return
  try {
    environments.value = await getEnvironments(targetProjectId)
  } catch {
    environments.value = []
  }
}

const open = () => {
  void loadEnvironments()
  visible.value = true
}

const handleExecute = async () => {
  if (props.selectedRows.length === 0) return
  executing.value = true
  try {
    const res = await batchExecute({
      testCaseIds: props.selectedRows.map((r) => r.id),
      environmentId: environmentId.value || null,
      browsers: browsers.value.length > 0 ? browsers.value : null,
      expandDataSets: expandDataSets.value,
    })
    const dataHint = res.casesWithoutData > 0 ? `，${res.casesWithoutData} 条用例的数据集为空` : ''
    ElMessage.success(
      `已入队 ${res.created} 条执行${res.skippedCaseIds.length > 0 ? `，${res.skippedCaseIds.length} 条被跳过（不支持的类型）` : ''}${dataHint}`,
    )
    visible.value = false
    router.push('/executions')
  } finally {
    executing.value = false
  }
}

defineExpose({ open })
</script>

<style scoped>
.w-full {
  width: 100%;
}

.batch-field {
  margin-top: 12px;
}

.batch-label {
  margin-bottom: 6px;
  font-size: 13px;
  color: var(--el-text-color-regular);
}

.batch-hint {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.batch-body p {
  margin: 0 0 12px;
  color: #606266;
}
</style>
