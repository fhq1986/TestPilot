/**
 * `@wangeditor/editor-for-vue`（Vue3 适配器）的**类型入口是坏的**，只能在这里补一份声明。
 *
 * 它 package.json 里同时写了：
 *   "types": "dist/src/index.d.ts",                        ← 声明文件确实存在
 *   "exports": { ".": { "import": ..., "require": ... } }  ← 但**没有 types 条件**
 * tsconfig 用的是 `moduleResolution: "bundler"`，该模式优先读 `exports`：
 * `exports` 里找不到 `types`，就当这个包没有类型，报 TS7016。
 *
 * 这是上游打包问题（该包发布于 `exports.types` 成为惯例之前），不是本项目配置有误。
 * 可选的出路只有两条：改全项目的 moduleResolution（为一个依赖动全局不值），
 * 或在本地补声明。这里选后者。
 *
 * ⚠ 下面的 props 是**按 5.1.12 的真实契约**写的，
 * 出处：node_modules/@wangeditor/editor-for-vue/dist/src/components/Editor.vue.d.ts。
 * 升级该依赖后若报类型不符，先去那儿对一遍，别凭记忆改。
 *
 * 注意本文件**不能有顶层 import/export**——有了它就变成模块，
 * 里面的 `declare module` 语义会从「环境模块声明」变成「模块增强」，
 * 而目标模块本来就解析不到，会直接报错。
 */
declare module '@wangeditor/editor-for-vue' {
  type DefineComponent = import('vue').DefineComponent
  type IDomEditor = import('@wangeditor/editor').IDomEditor
  type IEditorConfig = import('@wangeditor/editor').IEditorConfig
  type IToolbarConfig = import('@wangeditor/editor').IToolbarConfig
  type SlateDescendant = import('@wangeditor/editor').SlateDescendant

  export const Editor: DefineComponent<
    {
      /** 'default' | 'simple' */
      mode?: string
      defaultContent?: SlateDescendant[]
      defaultHtml?: string
      defaultConfig?: Partial<IEditorConfig>
      modelValue?: string
    },
    {},
    {},
    {},
    {},
    {},
    {},
    /*
      emits 用一张「任意事件名 → 处理函数」的签名表来声明：
      - wangEditor 的事件是自定义名字（onCreated/onChange/onDestroyed…），逐个列既啰嗦又容易漏；
      - 同时它也满足 EmitsOptions 的约束，`v-model` 展开出的 `onUpdate:modelValue` 也在其中。
    */
    Record<string, (...args: never[]) => void>
  >

  export const Toolbar: DefineComponent<{
    mode?: string
    defaultConfig?: Partial<IToolbarConfig>
    editor?: IDomEditor
  }>
}
