<template>
  <!--
    内容一律 v-html。
    安全前提在后端（见 Common/RichText.cs）：**写侧和读侧都过了净化**——
    读侧那一遍专门是为了改版前的历史数据，那批纯文本从未被净化过，
    而且读侧会顺手把纯文本转成 HTML。所以这里拿到的必然是可安全渲染的 HTML，
    前端不再自己净化一遍（同一判据不留第二份）。
  -->
  <div v-if="value" class="rich-text-view" v-html="value" />
  <span v-else class="rich-text-view__empty">{{ emptyText }}</span>
</template>

<script setup lang="ts">
withDefaults(
  defineProps<{
    value?: string | null
    emptyText?: string
  }>(),
  { value: '', emptyText: '—' },
)
</script>

<style scoped>
/*
  v-html 生成的内容没有 scoped 的 data 属性，必须用 :deep() 才选得到。
  颜色全用 Element Plus 的 CSS 变量，跟随主题，不写死色值。
*/
.rich-text-view {
  font-size: 13px;
  line-height: 1.7;
  color: var(--el-text-color-primary);
  word-break: break-word;
}

.rich-text-view :deep(p) {
  margin: 0 0 8px;
}

.rich-text-view :deep(p:last-child) {
  margin-bottom: 0;
}

.rich-text-view :deep(img) {
  max-width: 100%;
  height: auto;
  border-radius: 4px;
  vertical-align: bottom;
}

.rich-text-view :deep(a) {
  color: var(--el-color-primary);
}

.rich-text-view :deep(ul),
.rich-text-view :deep(ol) {
  margin: 0 0 8px;
  padding-left: 22px;
}

.rich-text-view :deep(blockquote) {
  margin: 8px 0;
  padding: 4px 12px;
  border-left: 3px solid var(--el-border-color);
  color: var(--el-text-color-secondary);
}

.rich-text-view :deep(pre) {
  margin: 8px 0;
  padding: 8px 12px;
  background: var(--el-fill-color-light);
  border-radius: 4px;
  overflow-x: auto;
}

.rich-text-view :deep(code) {
  padding: 1px 4px;
  background: var(--el-fill-color-light);
  border-radius: 3px;
  font-size: 12px;
}

.rich-text-view :deep(pre code) {
  padding: 0;
  background: transparent;
}

.rich-text-view :deep(hr) {
  margin: 12px 0;
  border: none;
  border-top: 1px solid var(--el-border-color);
}

.rich-text-view__empty {
  color: var(--el-text-color-placeholder);
}
</style>
