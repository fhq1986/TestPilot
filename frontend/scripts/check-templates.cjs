// 秒级模板语法门禁：只做 SFC 解析，不做类型检查与打包。
// 存在的理由：vue-tsc 会**容忍**模板里的失衡标签（例如多余的 </el-card>），
// 只有这里和生产构建（vite build）才报。构建太慢时用它先快速兜一遍。
const { parse } = require('@vue/compiler-sfc')
const fs = require('fs')
const path = require('path')

function walk(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, entry.name)
    if (entry.isDirectory()) walk(p, out)
    else if (entry.name.endsWith('.vue')) out.push(p)
  }
  return out
}

const files = walk('src')
let bad = 0
for (const file of files) {
  const { errors } = parse(fs.readFileSync(file, 'utf8'), { filename: file })
  if (errors.length) {
    bad++
    for (const err of errors) console.log(`  ${file}: ${err.message}`)
  }
}
console.log(bad === 0 ? `OK — ${files.length} 个 .vue 模板全部解析通过` : `FAIL — ${bad}/${files.length} 个文件有模板错误`)
process.exit(bad === 0 ? 0 : 1)
