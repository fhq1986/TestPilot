import request from './request'

/**
 * 测试报告导出（xlsx）。
 * 后端直接返回文件流，这里以 blob 接收后由调用方触发下载。
 */
const reportRequestConfig = {
  responseType: 'blob' as const,
  // 项目汇总报告涉及大量执行与截图，放宽超时
  timeout: 300000,
}

export const downloadExecutionReport = (executionId: string) =>
  request.get<unknown, Blob>(`/reports/executions/${executionId}`, reportRequestConfig)

// 批量导出：为选中的执行记录生成一份报告
export const downloadExecutionsReport = (executionIds: string[]) =>
  request.post<unknown, Blob>('/reports/executions', { executionIds }, reportRequestConfig)

/**
 * 项目汇总报告。
 *
 * planId 可选：传入时只统计该测试计划范围内的用例，报告结构不变、只是用例集合被缩小。
 * 界面上刻意不再暴露这个入口——要按计划看验收结果，切到「测试计划验收报告」更贴切；
 * 这里保留是为了脚本 / CI 能直接调。
 */
export const downloadProjectReport = (
  projectId: string,
  params?: { from?: string; to?: string; planId?: string },
) => request.get<unknown, Blob>(`/reports/projects/${projectId}`, { ...reportRequestConfig, params })

/** 触发浏览器下载，文件名优先从响应头解析（兼容中文文件名）。 */
export function saveBlobAsFile(blob: Blob, fallbackName: string, contentDisposition?: string) {
  let fileName = fallbackName
  if (contentDisposition) {
    const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition)
    const plain = /filename="?([^";]+)"?/i.exec(contentDisposition)
    if (utf8?.[1]) fileName = decodeURIComponent(utf8[1])
    else if (plain?.[1]) fileName = plain[1]
  }
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
}
