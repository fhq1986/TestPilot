<template>
  <div class="visual-page">
    <el-card class="list-card">

      <div class="toolbar">
        <el-select v-model="projectId" placeholder="选择项目" clearable filterable class="project-select" @change="load(1)">
          <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-input v-model="keyword" placeholder="搜索用例名称" clearable class="keyword-input" :prefix-icon="Search"
          @keyup.enter="load(1)" @clear="load(1)" />
        <div class="toolbar-spacer" />
        <el-button :icon="Refresh" @click="load()">刷新</el-button>
      </div>
      <el-alert type="warning" :closable="false" class="boundary-tip">
        <template #title>
          基线按「用例 + 步骤」保存；在用例编辑页开启「视觉回归」后，执行时会自动与基线比对
        </template>
      </el-alert>
      <div v-loading="loading" class="baseline-wrap">
        <el-empty v-if="baselines.length === 0" description="还没有视觉基线：给用例开启视觉回归并执行一次即可生成" />
        <div v-else class="baseline-grid">
          <div v-for="item in baselines" :key="item.id" class="baseline-card">
            <el-image :src="item.imageUrl" :preview-src-list="[item.imageUrl]" fit="cover" class="baseline-img"
              preview-teleported />
            <div class="baseline-body">
              <div class="baseline-name" :title="item.testCaseName">{{ item.testCaseName }}</div>
              <div class="baseline-meta">
                <el-tag size="small" type="info">步骤 {{ item.stepOrder + 1 }}</el-tag>
                <span class="meta-text">{{ item.width }}×{{ item.height }}</span>
              </div>
              <div class="baseline-meta">
                <span class="meta-text">比对 {{ item.compareCount }} 次</span>
                <span v-if="item.lastComparedAt" class="meta-text">· {{ formatDateTime(item.lastComparedAt) }}</span>
              </div>
              <div class="baseline-actions">
                <el-button link type="primary" @click="goDetail(item)">查看用例</el-button>
                <el-button link type="danger" @click="handleDelete(item)">删除基线</el-button>
              </div>
            </div>
          </div>
        </div>
      </div>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize" :total="total"
        :page-sizes="[12, 24, 48]" layout="total, sizes, prev, pager, next" @current-change="load()"
        @size-change="load(1)" />
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, toRefs } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh, Search } from '@element-plus/icons-vue'
import { getProjects } from '@/api/project'
import { deleteVisualBaseline, getVisualBaselines } from '@/api/visual'
import { formatDateTime } from '@/utils/formatter'
import type { VisualBaseline } from '@/types/visual'
import type { Project } from '@/types/project'
import { usePagedList } from '@/composables/usePagedList'

const router = useRouter()
const projectOptions = ref<Project[]>([])
const projectId = ref('')
const keyword = ref('')
// 分页列表状态机（页码/页大小/总数/loading），见 composables/usePagedList.ts
const list = usePagedList<VisualBaseline>((p, ps) => getVisualBaselines({
  projectId: projectId.value || undefined,
  keyword: keyword.value || undefined,
  page: p,
  pageSize: ps,
}), { pageSize: 12 })
const load = (targetPage?: number) => list.load(targetPage)
const { items: baselines, total, page, pageSize, loading } = toRefs(list)

const goDetail = (item: VisualBaseline) => router.push(`/testcases/${item.testCaseId}`)

const handleDelete = async (item: VisualBaseline) => {
  try {
    await ElMessageBox.confirm(
      `删除「${item.testCaseName}」步骤 ${item.stepOrder + 1} 的基线？下次执行会重新建立基线。`,
      '删除基线', { type: 'warning' })
  } catch {
    return
  }
  await deleteVisualBaseline(item.id)
  ElMessage.success('已删除')
  await load()
}

onMounted(async () => {
  const projects = await getProjects({ page: 1, pageSize: 100 })
  projectOptions.value = projects.items
  // 搜索条件「项目」默认为空（全部项目），用户按需筛选
  await load()
})
</script>

<style scoped>
.visual-page {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.list-card {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.list-card :deep(.el-card__body) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.baseline-wrap {
  flex: 1;
  min-height: 0;
  overflow: auto;
}

.baseline-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(230px, 1fr));
  gap: 14px;
}

.baseline-card {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  overflow: hidden;
  background: #fff;
  transition: box-shadow 0.2s, border-color 0.2s;
}

.baseline-card:hover {
  box-shadow: 0 2px 10px rgb(0 0 0 / 8%);
  border-color: var(--el-color-primary-light-5);
}

.baseline-img {
  width: 100%;
  height: 150px;
  background: var(--el-fill-color-light);
  display: block;
}

.baseline-body {
  padding: 10px 12px 8px;
}

.baseline-name {
  font-weight: 500;
  font-size: 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.baseline-meta {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 5px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.meta-text {
  color: var(--el-text-color-secondary);
}

.baseline-actions {
  margin-top: 4px;
}

.toolbar {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
  align-items: center;
  flex-wrap: wrap;
}

/* 刷新按钮推到工具栏最右 */
.toolbar-spacer {
  flex: 1;
}

.project-select {
  width: 200px;
}

.keyword-input {
  width: 200px;
}

.toolbar-hint {
  margin-left: auto;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}

.boundary-tip {
  margin-bottom: 10px;
}
</style>
