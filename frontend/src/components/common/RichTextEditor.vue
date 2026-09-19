<template>
  <div class="rte" :style="{ '--rte-height': `${height}px` }">
    <Toolbar class="rte-toolbar" :editor="editorRef" :default-config="toolbarConfig" mode="default" />
    <Editor
      v-model="html"
      class="rte-body"
      :default-config="editorConfig"
      mode="default"
      @on-created="onCreated"
    />
  </div>
</template>

<script setup lang="ts">
import '@wangeditor/editor/dist/css/style.css'
import { onBeforeUnmount, ref, shallowRef, watch } from 'vue'
// 注意装的是 editor-for-vue@5.x（Vue3 版）。它的 `latest` 标签指向 1.x，那是 Vue2 版，
// 直接 `npm i @wangeditor/editor-for-vue` 会装错——升级依赖时不要顺手把版本抹掉。
import { Editor, Toolbar } from '@wangeditor/editor-for-vue'
import type { IDomEditor, IEditorConfig } from '@wangeditor/editor'
import { uploadRichTextImage } from '@/api/artifact'

/** 图片大小上限。**必须与后端 ArtifactApiExtensions.MaxBytes 一致** */
const MAX_IMAGE_BYTES = 5 * 1024 * 1024

const props = withDefaults(
  defineProps<{
    modelValue?: string | null
    placeholder?: string
    /** 正文区高度（px） */
    height?: number
  }>(),
  {
    modelValue: '',
    placeholder: '请输入内容，可粘贴或拖入图片',
    height: 240,
  },
)

const emit = defineEmits<{ (e: 'update:modelValue', value: string): void }>()

// shallowRef：编辑器实例是个庞然大物，放进 ref 会被深度代理，既慢又可能踩到内部私有字段
const editorRef = shallowRef<IDomEditor>()
const html = ref(props.modelValue ?? '')

// 双向桥：编辑器 → 父组件
watch(html, (value) => {
  if (value !== (props.modelValue ?? '')) emit('update:modelValue', value)
})

// 父组件 → 编辑器（弹窗复用时最典型：关掉再打开另一条记录）。
// `@wangeditor/editor-for-vue` 内部也会 watch modelValue 并 setHtml，
// 这里同步本地 ref 是为了让上面那个 watch 的判断基准保持一致，避免来回抖动。
watch(
  () => props.modelValue,
  (value) => {
    const next = value ?? ''
    if (next !== html.value) html.value = next
  },
)

const toolbarConfig = {
  // 不提供视频：它需要另一套上传与存储配置，而「说明/描述」里贴视频也不是这个功能的诉求
  excludeKeys: ['group-video', 'fullScreen'],
}

const editorConfig: Partial<IEditorConfig> = {
  placeholder: props.placeholder,
  MENU_CONF: {
    uploadImage: {
      // 默认值是 2M，小于后端的 5M——不显式抬高，就会出现
      // 「服务端明明收得下，编辑器却先弹窗拒绝」的诡异现象
      maxFileSize: MAX_IMAGE_BYTES,
      // 0 = 永远不走 base64。base64 会把整张图塞进正文，
      // 描述列里躺着一串几百 KB 的字符，之后每次列表/详情查询都要拖着它走
      base64LimitSize: 0,
      customUpload(file: File, insertFn: (url: string, alt?: string, href?: string) => void) {
        uploadRichTextImage(file)
          .then(({ url }) => insertFn(url, file.name, url))
          .catch(() => {
            // axios 拦截器已经弹过一次「人话版」错误提示了，这里只需吞掉
            // 未处理的 Promise 拒绝，别让它冒到控制台
          })
      },
    },
  },
}

function onCreated(editor: IDomEditor) {
  editorRef.value = editor
}

// 编辑器持有全局事件监听，不销毁会在反复开关弹窗时不断堆积
onBeforeUnmount(() => {
  editorRef.value?.destroy()
})
</script>

<style scoped>
.rte {
  width: 100%;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  /* 编辑器内部的工具栏/下拉浮层会溢出容器，裁掉比留一条奇怪的横线好 */
  overflow: hidden;
}

.rte-toolbar {
  border-bottom: 1px solid var(--el-border-color);
}

/*
  !important 是必需的：组件根节点上写了行内 `style="height:100%"`
  （见 editor-for-vue 的渲染函数），行内样式优先级高于类选择器，
  不加 !important 这里的高度根本不会生效。
*/
.rte-body {
  height: var(--rte-height) !important;
  /* wangEditor 要求正文容器自己滚动；不隐藏溢出，多出来的部分会顶破边框 */
  overflow-y: hidden;
}
</style>
