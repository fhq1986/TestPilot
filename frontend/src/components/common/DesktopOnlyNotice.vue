<template>
  <el-alert v-if="isNarrow" type="warning" :closable="false" show-icon class="desktop-only-notice">
    <template #title>{{ title }}</template>
    <div class="desktop-only-notice__body">{{ reason }}</div>
  </el-alert>
</template>

<script setup lang="ts">
import { useBreakpoint } from '@/composables/useBreakpoint'

/**
 * 「这一页请在桌面端使用」的统一提示。
 *
 * 为什么要有它，而不是让页面在窄屏硬排版：
 * 有些功能不是「挤一挤还能用」，而是**物理上做不到** ——
 * 比如脚本录制要在运行录制服务的机器上弹出浏览器窗口供人操作。
 * 这种情况下把桌面布局硬塞进 375px，用户会以为功能坏了；
 * 明确说清原因反而是更好的体验。
 *
 * 组件自己判断窄屏，调用方直接写 `<DesktopOnlyNotice reason="…" />` 即可，
 * 不用再写一遍 v-if。注意它只负责「提示」，页面该隐藏的交互部分要自己用 v-if 收起。
 */
withDefaults(
  defineProps<{
    /** 具体原因 —— 要说明「为什么不行」，不要只说「请用桌面端」 */
    reason: string
    title?: string
  }>(),
  { title: '该功能需要桌面端' },
)

const { isNarrow } = useBreakpoint()
</script>

<style scoped>
.desktop-only-notice {
  margin-bottom: 12px;
}

.desktop-only-notice__body {
  margin-top: 4px;
  line-height: 1.6;
}
</style>
