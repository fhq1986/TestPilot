#!/usr/bin/env node
/**
 * 前后端契约 codegen（迭代 E·⑤）：从后端 Swagger 生成前端类型，让契约有单一真源。
 *
 * 背景：前端 `src/types/*` 一直是手写的，后端 DTO 改了不会有人提醒，
 * 只能等线上报错才发现（本项目就踩过 agentLoopEnabled 字段丢失）。
 * 这里把后端 Swagger 落成一份**机器生成**的类型快照并提交，
 * CI 里重新生成后比对——不一致即说明契约漂移，直接失败。
 *
 * 用法：
 *   node scripts/gen-api-types.mjs           生成 / 覆盖 src/types/generated/api.d.ts
 *   node scripts/gen-api-types.mjs --check   只校验：与已提交文件不一致则退出码 1（CI 用）
 *
 * 契约来源（按优先级）：
 *   SWAGGER_FILE=/path/to/swagger.json
 *   SWAGGER_URL=http://localhost:8088/swagger/v1/swagger.json   （默认）
 */
import { readFile, writeFile, mkdir } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import openapiTS, { astToString } from 'openapi-typescript'

const here = dirname(fileURLToPath(import.meta.url))
const OUT = resolve(here, '../src/types/generated/api.d.ts')
const checkOnly = process.argv.includes('--check')

const HEADER = `/**
 * 由后端 Swagger 自动生成 —— 请勿手改。
 * 重新生成：npm run api:types
 * 校验漂移：npm run api:types:check
 */
`

async function loadSchema() {
  const file = process.env.SWAGGER_FILE
  if (file) return JSON.parse(await readFile(file, 'utf8'))

  const url = process.env.SWAGGER_URL ?? 'http://localhost:8088/swagger/v1/swagger.json'
  const res = await fetch(url)
  if (!res.ok) throw new Error(`拉取 swagger 失败：${url} → HTTP ${res.status}`)

  const ct = res.headers.get('content-type') ?? ''
  if (!ct.includes('json')) {
    throw new Error(
      `swagger 端点未返回 JSON（content-type=${ct}）。` +
      `通常意味着后端不是 Development 环境（Swagger 只在 Development 注册）。`,
    )
  }
  return await res.json()
}

const schema = await loadSchema()
const generated = HEADER + astToString(await openapiTS(schema)) + '\n'

if (checkOnly) {
  if (!existsSync(OUT)) {
    console.error(`✗ 契约基线缺失：${OUT}\n  先运行 npm run api:types 生成并提交。`)
    process.exit(1)
  }
  const current = await readFile(OUT, 'utf8')
  if (current !== generated) {
    console.error('✗ 前后端契约已漂移：后端 Swagger 与已提交的 api.d.ts 不一致。')
    console.error('  修复：npm run api:types 重新生成并提交。')
    process.exit(1)
  }
  console.log('✓ 契约无漂移')
} else {
  await mkdir(dirname(OUT), { recursive: true })
  await writeFile(OUT, generated, 'utf8')
  console.log(`✓ 已生成 ${OUT}`)
}
