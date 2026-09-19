<template>
  <div class="page-header-bar" :class="{ 'is-sticky': sticky }">
    <div class="phb-left">
      <!-- 一级导航页（如缺陷管理）没有"上一页"语义，传 show-back=false 隐藏 -->
      <el-button v-if="showBack" type="primary" class="phb-back" :icon="ArrowLeft" @click="handleBack">返回</el-button>
      <div class="phb-heading">
        <span class="phb-dot" />
        <span class="phb-title">{{ title }}</span>
        <span v-if="subtitle" class="phb-subtitle"></span>
      </div>
    </div>
    <!-- 右侧操作区。
         除了 #actions，这里**故意也渲染默认插槽**：曾经 RequirementListView / DefectListView
         把按钮当默认插槽塞进来，而这里只认 $slots.actions → 按钮被静默丢弃，
         页面不报错、只是"少了个新建按钮"，最后是靠用户反馈才发现的。
         静默丢弃是最坏的失败形态，所以两种写法都让它渲染。 -->
    <div v-if="$slots.actions || $slots.default" class="phb-actions">
      <slot name="actions" />
      <slot />
    </div>
  </div>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router'
import { ArrowLeft } from '@element-plus/icons-vue'

const props = withDefaults(
  defineProps<{
    title: string
    subtitle?: string
    /** 返回目标路径；不传时退化为浏览器返回 */
    backTo?: string
    /** 是否显示返回按钮；详情页默认显示，一级导航页传 false 隐藏 */
    showBack?: boolean
    /** 吸顶：页面滚动时固定在滚动容器（MainLayout 的 el-main）顶部 */
    sticky?: boolean
  }>(),
  {
    // **必须给默认 true**：绝大多数用法是详情页/编辑页，它们传 back-to 表达"返回到哪"，
    // 本来就该有返回按钮。不设默认值时 showBack 是 undefined，`v-if="showBack"` 恒为假——
    // 于是所有详情页的返回按钮一起消失（本组件从 el-page-header 迁移过来时就是这么丢的，
    // el-page-header 自带返回按钮，而这里要求显式传 true）。
    // 一级导航页（缺陷管理、需求覆盖）显式传 :show-back="false" 关掉，语义清晰。
    showBack: true,
    sticky: false,
  },
)

const router = useRouter()

const handleBack = () => {
  if (props.backTo) router.push(props.backTo)
  else router.back()
}
</script>

<style scoped>
.page-header-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
  padding: 10px 14px;
  background: #fff;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  box-shadow: 0 1px 2px rgba(31, 59, 115, 0.04);
}

.phb-left {
  display: flex;
  align-items: center;
  gap: 14px;
  min-width: 0;
}

/* 返回按钮：primary 主题 + 常规直角（不加圆角装饰） */
.phb-back {
  height: 32px;
  padding: 0 14px 0 11px;
  font-weight: 500;
}

.phb-back :deep(.el-icon) {
  transition: transform 0.18s ease;
}

.phb-back:hover :deep(.el-icon) {
  transform: translateX(-2px);
}

.phb-heading {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.phb-dot {
  width: 4px;
  height: 16px;
  border-radius: 2px;
  background: linear-gradient(180deg, #1f3b73, #3f7fd4);
}

.phb-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--el-text-color-primary);
  white-space: nowrap;
}

.phb-subtitle {
  font-size: 13px;
  color: var(--el-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.phb-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

/* 吸顶：滚动容器是 MainLayout 的 el-main（自带 overflow:auto），sticky 相对它生效，
   顶部栏 AppHeader 在 el-main 之外本来就不滚，所以 top:0 就是"顶栏正下方"。
   z-index 压过卡片/表格即可；不能设太大去盖 el-dialog / 下拉浮层（那些是 2000+）。 */
.page-header-bar.is-sticky {
  position: sticky;
  top: 0;
  z-index: 10;
}
</style>
