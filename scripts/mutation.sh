#!/usr/bin/env bash
# =============================================================================
# 迭代 E·④：变异测试（Stryker.NET）——针对**自愈与授权**这类"改坏了也不报错"的关键纯逻辑。
#
# 为什么值得单独做：普通单测只能证明"给定输入得到预期输出"，无法证明断言足够强；
# 变异测试会主动把代码改坏（如把 >= 改成 >、把 true 改成 false），若测试仍然通过，
# 说明测试根本没覆盖这个判定。AgentLoopService / FixActionApplier / ProjectScopeRules
# 的判断逻辑一旦退化，是"静默失效"级事故，必须用变异测试兜住。
#
# 依赖：Stryker.NET 工具（首次需联网安装）：
#     dotnet tool install -g dotnet-stryker
# 安装后本脚本可重复运行；产物在 .run/mutation/（html 报告）。
# =============================================================================
set -uo pipefail
export PATH="/usr/bin:/bin:$PATH"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DOTNET_BIN="${DOTNET_BIN:-}"
[ -z "$DOTNET_BIN" ] && for p in "/c/Program Files/dotnet/dotnet.exe" "$(command -v dotnet 2>/dev/null || true)"; do
  [ -n "$p" ] && [ -x "$p" ] && DOTNET_BIN="$p" && break
done

if ! "$DOTNET_BIN" tool list -g 2>/dev/null | grep -qi "dotnet-stryker"; then
  echo "!! 未安装 Stryker.NET。请先执行（需联网）：" >&2
  echo "     dotnet tool install -g dotnet-stryker" >&2
  echo "   安装后重跑本脚本。配置见 backend/stryker-config.json" >&2
  exit 127
fi

echo "==> 运行变异测试（配置：backend/stryker-config.json）"
(
  cd "$ROOT/backend" || exit 1
  env "APPDATA=${APPDATA:-$HOME/AppData/Roaming}" \
      "LOCALAPPDATA=${LOCALAPPDATA:-$HOME/AppData/Local}" \
      "PROGRAMFILES=${PROGRAMFILES:-C:/Program Files}" \
      "ProgramFiles(x86)=${ProgramFiles(x86):-C:/Program Files (x86)}" \
      "PROGRAMDATA=${PROGRAMDATA:-C:/ProgramData}" \
      "$DOTNET_BIN" stryker --config-file stryker-config.json
)
echo "==> 完成。报告：.run/mutation/（html）"
