<template>
  <div class="comment-section">
    <div class="comment-head">
      <span>评论（{{ comments.length }}）</span>
      <el-button size="small" :loading="loading" @click="load" type="primary">刷新</el-button>
    </div>

    <div class="comment-list">
      <div v-for="c in comments" :key="c.id" class="comment-item">
        <div class="comment-meta">
          <span class="comment-author">{{ c.authorName }}</span>
          <span class="comment-time">{{ formatDateTime(c.createdAt) }}</span>
          <el-button link type="danger" size="small" class="comment-del" @click="handleDelete(c)">删除</el-button>
        </div>
        <div class="comment-body" v-html="renderBody(c.body)" />
      </div>
      <el-empty v-if="!loading && comments.length === 0" description="还没有评论" :image-size="50" />
    </div>

    <el-input v-model="draft" type="textarea" :rows="3" maxlength="2000" show-word-limit
      placeholder="写下评论…（@用户名 可以提醒同事）" />
    <div class="comment-actions">
      <el-button type="primary" size="small" :loading="saving" :disabled="!draft.trim()" @click="handleCreate">
        发表评论
      </el-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { createComment, deleteComment, listComments, type CommentItem } from '@/api/comments'
import { formatDateTime } from '@/utils/formatter'
const props = defineProps<{ target: number; targetId: string }>()

const comments = ref<CommentItem[]>([])
const loading = ref(false)
const saving = ref(false)
const draft = ref('')

// 删除按钮对所有人可见、服务端裁定（403 提示"只有作者本人或管理员"）——
// 与其前端猜权限漏判，不如把裁定收敛在一处
const canDelete = (_c: CommentItem) => true

const load = async () => {
  loading.value = true
  try {
    comments.value = await listComments(props.target, props.targetId)
  } finally {
    loading.value = false
  }
}

const handleCreate = async () => {
  const body = draft.value.trim()
  if (!body) return
  saving.value = true
  try {
    await createComment(props.target, props.targetId, body)
    draft.value = ''
    await load()
  } finally {
    saving.value = false
  }
}

const handleDelete = async (c: CommentItem) => {
  await ElMessageBox.confirm('确定删除这条评论？', '删除评论',
    { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' })
  try {
    await deleteComment(c.id)
    await load()
  } catch {
    ElMessage.error('删除失败：只有作者本人或管理员可以删除')
  }
}

// 正文渲染：先转义再高亮 @提及——评论区是用户输入区，直接 v-html 等于自建 XSS
const renderBody = (body: string) =>
  body
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/@([\w\u4e00-\u9fa5.-]+)/g, '<span class="comment-mention">@$1</span>')
    .replace(/\n/g, '<br>')

onMounted(load)
</script>

<style scoped>
.comment-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.comment-list {
  margin-bottom: 12px;
}

.comment-item {
  padding: 8px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.comment-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 4px;
}

.comment-author {
  font-weight: 600;
  font-size: 13px;
}

.comment-time {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.comment-del {
  margin-left: auto;
}

.comment-body {
  font-size: 13px;
  line-height: 1.6;
  white-space: pre-wrap;
}

.comment-body :deep(.comment-mention) {
  color: var(--el-color-primary);
  font-weight: 600;
}

.comment-actions {
  margin-top: 8px;
  display: flex;
  justify-content: flex-end;
}
</style>
