#!/usr/bin/env node
/**
 * 前后端契约**响应方向**校验（迭代 F·②）。
 *
 * 与 gen-api-types.mjs 的分工：
 *   gen-api-types.mjs  —— 校验「类型漂移」：后端 DTO 改了，前端手写类型有没有跟上（静态比对生成物）
 *   本脚本             —— 校验「响应结构」：后端**实际返回的 JSON** 是否符合它在 Swagger 里声明的 schema
 *
 * 为什么需要后者：静态比对只能发现「声明的契约变了」，发现不了「实际返回与声明不符」——
 * 例如 DTO 加了字段却漏了 `.Produces<T>()`、或者端点改返回匿名对象后 schema 悄悄失真。
 *
 * 前置条件：后端必须以 **Development** 环境跑着（Swagger 只在 Development 注册），
 * 且端点已用 `.Produces<T>()` 声明响应类型（否则 Swagger 里没有响应体 schema，无可校验）。
 *
 * 用法：
 *   node scripts/validate-api-responses.mjs
 *   SWAGGER_URL=http://127.0.0.1:5199/swagger/v1/swagger.json node scripts/validate-api-responses.mjs
 *
 * 环境变量：
 *   SWAGGER_URL / SWAGGER_FILE  契约来源（同 gen-api-types.mjs）
 *   API_USER / API_PASSWORD     登录账号，默认 superadmin / super@135246（本地种子账号）
 *   STRICT=1                    把「跳过」也视为失败（用于要求全覆盖的场景）
 */
import { readFile } from 'node:fs/promises'
import Ajv from 'ajv'
import addFormats from 'ajv-formats'

const swaggerUrl = process.env.SWAGGER_URL ?? 'http://localhost:8088/swagger/v1/swagger.json'
const apiUser = process.env.API_USER ?? 'superadmin'
const apiPassword = process.env.API_PASSWORD ?? 'super@135246'
const strict = process.env.STRICT === '1'

// ---------------------------------------------------------------- 契约加载

async function loadSchema() {
  const file = process.env.SWAGGER_FILE
  if (file) return JSON.parse(await readFile(file, 'utf8'))

  const res = await fetch(swaggerUrl)
  if (!res.ok) throw new Error(`拉取 swagger 失败：${swaggerUrl} → HTTP ${res.status}`)
  const ct = res.headers.get('content-type') ?? ''
  if (!ct.includes('json')) {
    throw new Error(
      `swagger 端点未返回 JSON（content-type=${ct}）。` +
      `通常意味着后端不是 Development 环境（Swagger 只在 Development 注册）。`,
    )
  }
  return await res.json()
}

// ---------------------------------------------------------------- schema 归一化

/**
 * OpenAPI 3 的 schema 不完全是 JSON Schema：ajv 不认识 `nullable`，
 * 也不会忽略 `example` 这类注解关键字。这里做一次深拷贝式的归一化：
 *   - `nullable: true` + `type: X`  → `type: [X, 'null']`
 *   - `nullable: true` + `$ref`     → `anyOf: [{$ref}, {type:'null'}]`
 *   - 删掉对校验无意义、但会触发 ajv 抱怨的注解键
 * 不这么做的话，后端合法的 null 字段会被判成「类型不符」——那是假阳性，比不校验更糟。
 */
function normalize(node) {
  if (Array.isArray(node)) return node.map(normalize)
  if (node === null || typeof node !== 'object') return node

  const out = {}
  const nullable = node.nullable === true

  for (const [key, value] of Object.entries(node)) {
    if (key === 'nullable' || key === 'example' || key === 'xml' ||
        key === 'externalDocs' || key === 'discriminator') continue
    if (key === 'properties') {
      out.properties = Object.fromEntries(
        Object.entries(value).map(([k, v]) => [k, nullableRefProperty(v)]),
      )
    } else if (key === 'items' || key === 'additionalProperties' || key === 'not') {
      out[key] = normalize(value)
    } else if (key === 'anyOf' || key === 'oneOf' || key === 'allOf') {
      out[key] = value.map(normalize)
    } else {
      out[key] = value
    }
  }

  if (nullable) {
    if (typeof out.type === 'string') {
      out.type = [out.type, 'null']
    } else if (out.$ref) {
      const ref = out.$ref
      delete out.$ref
      return { anyOf: [{ $ref: ref }, { type: 'null' }] }
    } else if (!out.anyOf && !out.oneOf) {
      out.type = [out.type ?? 'object', 'null']
    }
  }

  return out
}

/**
 * 对象属性的「可空性」在 OpenAPI 3.0 里表达不出来，只能放宽——见下。
 *
 * Swashbuckle 6.x 生成的是 OpenAPI 3.0：当属性类型是另一个 schema（`$ref`）时，
 * 序列化器只写 `$ref` 就返回，`nullable: true` 会被丢掉（3.0 规范里 `$ref`
 * 不允许有兄弟键）。而值类型/字符串属性不受影响（实测 `url` 能正确带上
 * `nullable: true`）。
 *
 * 后果：契约对这类属性**完全没说**它能不能为 null，`SelectorConfig? Selector`
 * 与 `SelectorConfig Selector` 生成的 schema 一模一样。此时若按「非空对象」校验，
 * 后端合法返回的 null 会被判成漂移——假阳性比不校验更糟（会让人去改本来正确的代码）。
 * 因此这里显式放宽成 `anyOf: [$ref, null]`：仍然校验「非 null 时结构正确」，
 * 只是不再对契约本身无法表达的那一维下断言。
 *
 * 将来若换成 OpenAPI 3.1（`$ref` 允许兄弟键）或改用能内联 nullable 的生成器，
 * 这个放宽就应该去掉——它是格式限制的妥协，不是设计意图。
 */
function nullableRefProperty(schema) {
  const normalized = normalize(schema)
  const keys = Object.keys(normalized)
  if (keys.length === 1 && normalized.$ref) {
    return { anyOf: [normalized, { type: 'null' }] }
  }
  return normalized
}

// ---------------------------------------------------------------- HTTP

/** 从 swagger URL 推导 API 根地址（去掉 /swagger/... 后缀） */
function apiBaseOf(url) {
  const u = new URL(url)
  const idx = u.pathname.indexOf('/swagger')
  return `${u.protocol}//${u.host}${idx >= 0 ? u.pathname.slice(0, idx) : ''}`
}

const apiBase = apiBaseOf(swaggerUrl)
let token = ''

async function call(method, path, { body } = {}) {
  const res = await fetch(apiBase + path, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  let json
  try { json = text ? JSON.parse(text) : undefined } catch { json = undefined }
  return { status: res.status, json, text }
}

// ---------------------------------------------------------------- 待校验端点

/**
 * 只覆盖**只读 GET**：CI 里跑不产生副作用，可以放心对生产/共享环境执行。
 * `id` 字段是取参函数，依赖前面 seed 出来的数据；取不到就跳过（例如 CI 上的空库）。
 */
function endpoints(ctx) {
  return [
    // 认证 / 用户 / 审计
    { path: '/api/auth/me' },
    { path: '/api/auth/sso/providers' },
    { path: '/api/users/me/sso' },
    { path: '/api/users/options' },
    { path: '/api/users/roles' },
    { path: '/api/audit', query: '?page=1&pageSize=5' },

    // 项目与成员
    { path: '/api/projects', query: '?page=1&pageSize=5' },
    { path: '/api/projects/{id}', id: ctx.projectId },
    { path: '/api/projects/{id}/api-tokens', id: ctx.projectId },
    { path: '/api/projects/{id}/members', id: ctx.projectId },
    { path: '/api/projects/{id}/members/candidates', id: ctx.projectId, query: '?search=a' },
    { path: '/api/projects/{id}/custom-fields', id: ctx.projectId },
    { path: '/api/projects/{id}/environments', id: ctx.projectId },

    // 用例 / 版本 / 共享步骤 / 视觉
    { path: '/api/testcases', query: '?page=1&pageSize=5' },
    { path: '/api/testcases/{id}', id: ctx.testCaseId },
    { path: '/api/testcases/{id}/versions', id: ctx.testCaseId },
    { path: '/api/testcases/modules', query: ctx.projectId ? `?projectId=${ctx.projectId}` : '' },
    { path: '/api/shared-steps', query: '?page=1&pageSize=5' },
    { path: '/api/shared-steps/{id}', id: ctx.sharedStepId },
    { path: '/api/shared-steps/{id}/usages', id: ctx.sharedStepId },
    { path: '/api/shared-steps/options', query: ctx.projectId ? `?projectId=${ctx.projectId}` : '' },
    { path: '/api/visual/baselines', query: '?page=1&pageSize=5' },
    { path: '/api/visual/cases/{id}', id: ctx.testCaseId },

    // 执行 / 节点 / 自愈
    { path: '/api/executions', query: '?page=1&pageSize=5' },
    { path: '/api/executions/{id}', id: ctx.executionId },
    { path: '/api/executions/{id}/defect-links', id: ctx.executionId },
    { path: '/api/executions/{id}/agent-attempts', id: ctx.executionId },
    { path: '/api/executions/agent-approvals', query: '?page=1&pageSize=5' },
    { path: '/api/executions/agent-heal-metrics' },
    { path: '/api/nodes' },

    // 计划 / 套件 / 定时 / 需求 / 缺陷
    { path: '/api/test-plans', query: '?page=1&pageSize=5' },
    { path: '/api/test-plans/releases', query: ctx.projectId ? `?projectId=${ctx.projectId}` : '' },
    { path: '/api/test-plans/{id}', id: ctx.planId },
    { path: '/api/test-plans/{id}/items', id: ctx.planId },
    { path: '/api/test-plans/{id}/items/validate', id: ctx.planId },
    { path: '/api/test-plans/{id}/rounds', id: ctx.planId, query: '?take=5' },
    { path: '/api/test-plans/{id}/gate', id: ctx.planId },
    { path: '/api/test-plans/{id}/report', id: ctx.planId },
    { path: '/api/suites', query: '?page=1&pageSize=5' },
    { path: '/api/suites/{id}', id: ctx.suiteId },
    { path: '/api/suites/{id}/runs', id: ctx.suiteId, query: '?take=5' },
    { path: '/api/schedules', query: '?page=1&pageSize=5' },
    { path: '/api/schedules/{id}', id: ctx.scheduleId },
    { path: '/api/requirements', query: '?page=1&pageSize=5' },
    { path: '/api/requirements/{id}', id: ctx.requirementId },
    { path: '/api/requirements/{id}/plans', id: ctx.requirementId },
    { path: '/api/requirements/coverage', query: ctx.projectId ? `?projectId=${ctx.projectId}` : '' },
    { path: '/api/defects', query: '?page=1&pageSize=5' },
    { path: '/api/defects/stats', query: ctx.projectId ? `?projectId=${ctx.projectId}` : '' },
    { path: '/api/defects/{id}', id: ctx.defectId },
    { path: '/api/defects/external-providers' },

    // 数据集 / Mock / 设置 / 分享 / 脚本 / 录制 / 统计 / 评论 / 消息
    { path: '/api/datasets', query: '?page=1&pageSize=5' },
    { path: '/api/datasets/{id}', id: ctx.dataSetId },
    { path: '/api/datasets/check/{id}', id: ctx.testCaseId },
    { path: '/api/mocks' },
    { path: '/api/settings' },
    { path: '/api/settings/ai-providers' },
    { path: '/api/shares', query: '?page=1&pageSize=5' },
    { path: '/api/scripts/export/{id}', id: ctx.testCaseId },
    { path: '/api/recorder/capabilities' },
    { path: '/api/recorder/sessions', query: ctx.projectId ? `?projectId=${ctx.projectId}` : '' },
    { path: '/api/stats/dashboard', query: '?trendDays=14' },
    { path: '/api/comments', query: ctx.testCaseId ? `?target=TestCase&targetId=${ctx.testCaseId}` : '' },
    { path: '/api/notifications', query: '?page=1&pageSize=5' },
    { path: '/api/notifications/unread-count' },
  ]
}

// ---------------------------------------------------------------- 主流程

const swagger = await loadSchema()

/**
 * swagger 里的路径带路由约束（`/api/projects/{id:guid}`），本脚本的清单里写的是
 * 干净的 `{id}`。归一化掉花括号里的内容后建索引，按我们的写法查找。
 */
const normalizePathTemplate = (p) => p.replace(/\{[^}]*\}/g, '{}')
const getOperations = new Map()
for (const [p, ops] of Object.entries(swagger.paths ?? {})) {
  if (ops.get) getOperations.set(normalizePathTemplate(p), ops.get)
}

// 归一化所有响应 schema，并登记到 ajv
const ajv = new Ajv({ strict: false, allErrors: true, validateFormats: true, allowUnionTypes: true })
addFormats(ajv)

const schemas = swagger.components?.schemas ?? {}
for (const [name, schema] of Object.entries(schemas)) {
  try {
    ajv.addSchema(normalize(schema), `#/components/schemas/${name}`)
  } catch (e) {
    console.warn(`  ! 跳过无法编译的 schema ${name}：${e.message}`)
  }
}

// 登录
const login = await call('POST', '/api/auth/login', { body: { username: apiUser, password: apiPassword } })
if (login.status !== 200 || !login.json?.token) {
  console.error(`✗ 登录失败：HTTP ${login.status}。可用 API_USER / API_PASSWORD 覆盖账号。`)
  process.exit(2)
}
token = login.json.token

/** 从列表响应里取第一条的 id，供详情类端点使用 */
const firstId = (res) => res.json?.items?.[0]?.id ?? res.json?.[0]?.id ?? undefined

// seed：详情端点需要一个真实 id。取不到就留空，对应端点会被记为「跳过」
const ctx = {}
const seeds = [
  ['/api/projects?page=1&pageSize=1', 'projectId'],
  ['/api/testcases?page=1&pageSize=1', 'testCaseId'],
  ['/api/executions?page=1&pageSize=1', 'executionId'],
  ['/api/test-plans?page=1&pageSize=1', 'planId'],
  ['/api/suites?page=1&pageSize=1', 'suiteId'],
  ['/api/schedules?page=1&pageSize=1', 'scheduleId'],
  ['/api/requirements?page=1&pageSize=1', 'requirementId'],
  ['/api/defects?page=1&pageSize=1', 'defectId'],
  ['/api/datasets?page=1&pageSize=1', 'dataSetId'],
  ['/api/shared-steps?page=1&pageSize=1', 'sharedStepId'],
]
for (const [p, key] of seeds) {
  const res = await call('GET', p)
  ctx[key] = res.status === 200 ? firstId(res) : undefined
}

const validated = []
const skipped = []
const failures = []

for (const ep of endpoints(ctx)) {
  if (ep.id === undefined && ep.path.includes('{id}')) {
    skipped.push({ path: ep.path, why: '无可用 id（库中无数据）' })
    continue
  }
  const url = ep.path.replace('{id}', ep.id) + (ep.query ?? '')

  // 从 swagger 里取该端点的 200 响应 schema
  const op = getOperations.get(normalizePathTemplate(ep.path))
  const respSchema = op?.responses?.['200']?.content?.['application/json']?.schema
  if (!respSchema) {
    skipped.push({ path: url, why: 'Swagger 未声明响应 schema（端点缺 .Produces<T>()）' })
    continue
  }

  const res = await call('GET', url)
  if (res.status !== 200) {
    skipped.push({ path: url, why: `HTTP ${res.status}` })
    continue
  }

  const validate = ajv.compile(normalize(respSchema))
  if (validate(res.json)) {
    validated.push(url)
  } else {
    failures.push({
      path: url,
      errors: (validate.errors ?? []).slice(0, 5).map((e) => `${e.instancePath || '/'} ${e.message}`),
    })
  }
}

// ---------------------------------------------------------------- 报告

console.log('')
console.log(`契约来源：${swaggerUrl}`)
console.log(`响应校验通过：${validated.length} 个端点`)
console.log(`跳过：${skipped.length} 个端点`)

if (skipped.length > 0) {
  console.log('\n跳过的端点（不计失败，但说明覆盖缺口）：')
  for (const s of skipped) console.log(`  - ${s.path}  ← ${s.why}`)
}

if (failures.length > 0) {
  console.error('\n✗ 响应与 Swagger 声明不符：')
  for (const f of failures) {
    console.error(`  ${f.path}`)
    for (const e of f.errors) console.error(`      ${e}`)
  }
  console.error('\n  这类不一致说明「实际返回」与「声明的契约」已经分叉，请修 DTO 或补 .Produces<T>()。')
  process.exit(1)
}

if (validated.length === 0) {
  console.error('\n✗ 没有任何端点被真正校验（全部跳过）——门禁等于没跑，请检查后端是否为 Development 环境。')
  process.exit(1)
}

if (strict && skipped.length > 0) {
  console.error(`\n✗ STRICT=1：存在 ${skipped.length} 个跳过项。`)
  process.exit(1)
}

console.log('\n✓ 响应结构无漂移')
