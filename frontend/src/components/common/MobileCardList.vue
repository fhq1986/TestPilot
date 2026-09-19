<template>
  <div class="mobile-card-list">
    <el-empty v-if="items.length === 0" :description="emptyText" :image-size="60" />

    <div v-for="(item, index) in items" :key="rowKey(item, index)" class="mobile-card">
      <div class="mobile-card__head">
        <div class="mobile-card__title">
          <slot name="title" :item="item" :index="index" />
        </div>
        <div v-if="$slots.badge" class="mobile-card__badge">
          <slot name="badge" :item="item" :index="index" />
        </div>
      </div>

      <div v-if="$slots.meta" class="mobile-card__meta">
        <slot name="meta" :item="item" :index="index" />
      </div>

      <div v-if="$slots.actions" class="mobile-card__actions">
        <slot name="actions" :item="item" :index="index" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts" generic="T">
/**
 * 列表页在窄屏下的卡片形态。
 *
 * 为什么不是「给 el-table 加横向滚动」：实测本平台列表页的列宽合计 1090–1660px，
 * 而操作列普遍 270–340px 又压在最右 —— 在 375px 的屏幕上横向滚动等于把
 * 「操作」这个最该点的东西藏到 3–4 屏之外。所以窄屏必须有另一套形态。
 *
 * 用法（页面里保留同一份数据源，只换渲染方式）：
 *   <el-table v-if="!isMobile" :data="rows"> … </el-table>
 *   <MobileCardList v-else :items="rows">
 *     <template #title="{ item }">…</template>
 *     <template #meta="{ item }">…</template>
 *     <template #actions="{ item }">…</template>
 *   </MobileCardList>
 *
 * 卡片里的字段一律**换行展示**，不用 `show-overflow-tooltip` —— 触屏没有 hover，
 * tooltip 里的内容等于看不到。
 */
withDefaults(
  defineProps<{
    items: T[]
    /** 空态文案。默认与 el-table 的空态保持一致 */
    emptyText?: string
    /** 列表项的稳定 key。默认取数组下标（这些列表都是整页替换，不会出现局部插入） */
    rowKey?: (item: T, index: number) => string | number
  }>(),
  {
    emptyText: '暂无数据',
    rowKey: (_item: T, index: number) => index,
  },
)
</script>

<style scoped>
.mobile-card-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  /* v-loading 的遮罩要给容器自己一个定位上下文 */
  position: relative;
}

.mobile-card {
  padding: 12px;
  background-color: #fff;
  border: 1px solid #d5e0ee;
  border-radius: 8px;
}

.mobile-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
}

.mobile-card__title {
  min-width: 0;
  font-size: 14px;
  font-weight: 600;
  line-height: 1.45;
  color: #1f3b73;
  word-break: break-word;
}

.mobile-card__badge {
  flex-shrink: 0;
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 6px;
}

.mobile-card__meta {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 14px;
  margin-top: 8px;
  font-size: 12px;
  line-height: 1.6;
  color: #606266;
}

/* 每个「标签 + 值」是一个内联块，长值自己换行，不把卡片撑开 */
.mobile-card__meta > :deep(*) {
  display: inline-flex;
  gap: 4px;
  max-width: 100%;
  word-break: break-word;
}

/* 字段名：比字段值浅一档，让值更醒目 */
.mobile-card__meta :deep(.mcl-label) {
  flex-shrink: 0;
  color: #909399;
}

.mobile-card__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px dashed #e4e7ed;
}
</style>
