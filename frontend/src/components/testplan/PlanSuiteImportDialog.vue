<template>
  <el-dialog v-model="visible" title="从套件导入用例" width="700px">
    <el-alert type="warning" :closable="false" class="import-tip">
      <template #title>
        导入是**一次性**的：导入后这些用例就归本计划独立管理，
        套件以后再变也不会影响本计划的范围。
      </template>
    </el-alert>
    <el-form label-width="90px">
      <el-form-item label="套件">
        <el-select v-model="suiteId" placeholder="选择测试套件" class="w-full">
          <el-option v-for="s in suites" :key="s.id"
            :label="`${s.name}（${s.caseCount ?? 0} 个用例）`" :value="s.id" />
        </el-select>
      </el-form-item>
      <el-form-item label="导入方式">
        <el-radio-group v-model="mode">
          <el-radio-button value="append">追加（自动去重）</el-radio-button>
          <el-radio-button value="replace">替换现有范围</el-radio-button>
        </el-radio-group>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="primary" :disabled="!suiteId" :loading="importing" @click="apply">
        导入
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getSuites } from '@/api/suite'
import { importPlanItemsFromSuiteApi } from '@/api/testPlan'
import type { SuiteSummary } from '@/types/suite'

const props = defineProps<{
  /** 导入接口按计划落库 */
  planId: string
}>()

/** 导入成功后由父级整页刷新 */
const emit = defineEmits<{ imported: [] }>()

const visible = ref(false)
const importing = ref(false)
const suiteId = ref('')
const mode = ref<'append' | 'replace'>('append')
const suites = ref<SuiteSummary[]>([])

/** 打开弹窗。套件列表按项目懒加载，同页复用 */
const open = async (projectId: string | undefined) => {
  if (suites.value.length === 0) {
    const res = await getSuites({ projectId, page: 1, pageSize: 100 })
    suites.value = res.items
  }
  suiteId.value = ''
  mode.value = 'append'
  visible.value = true
}

async function apply() {
  importing.value = true
  try {
    const res = await importPlanItemsFromSuiteApi(props.planId, suiteId.value, mode.value)
    ElMessage.success(res.message)
    visible.value = false
    emit('imported')
  } finally {
    importing.value = false
  }
}

defineExpose({ open })
</script>

<style scoped>
.import-tip { margin-bottom: 14px; }
.w-full { width: 100%; }
</style>
