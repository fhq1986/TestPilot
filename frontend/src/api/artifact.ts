import request from './request'

/**
 * 富文本正文里插入的图片。
 *
 * 走与业务接口**同一个 axios 实例**，而不是 wangEditor 默认的 XMLHttpRequest：
 * 默认实现不带 `Authorization`，在有鉴权的环境里上传必然 401，
 * 而且失败只会弹一个编辑器内部的英文提示，用户根本不知道为什么传不上去。
 * 复用实例后，JWT 注入与「人话版错误提示」都是现成的。
 */
export const uploadRichTextImage = (file: File) => {
  const form = new FormData()
  // 字段名必须与后端 `IFormFile file` 的参数名一致，否则绑定不到、报 400
  form.append('file', file)
  // 不要手动设 Content-Type：multipart 的 boundary 必须由 axios 自己生成
  return request.post<unknown, { url: string }>('/artifacts/image', form)
}
