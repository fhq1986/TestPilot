#!/usr/bin/env bash
# =============================================================================
# 迭代 E·④：测试覆盖率门禁（可重复执行，替代"无 CI 时的人工检查"）。
#
# 流程：跑单元测试并采集 coverlet cobertura → 解析 → 对核心程序集强制**行覆盖率下限**。
#
# 用法：
#   bash scripts/coverage.sh                 # 使用默认下限（见 MIN_LINE）
#   MIN_LINE=50 bash scripts/coverage.sh     # 临时抬高/降低下限
#
# 阈值（ratchet/棘轮）约定：只允许**上调**，不允许为了"过门禁"而下调——
# 下调等于把门禁拆了。改动核心逻辑后覆盖率下降，这里会失败，请补测试而不是改小 MIN_LINE。
# =============================================================================
set -uo pipefail
export PATH="/usr/bin:/bin:$PATH"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUT="$ROOT/.run/coverage"
LOG_DIR="$ROOT/.run/logs"; mkdir -p "$LOG_DIR"

# 各程序集行覆盖率下限（ratchet 棘轮，只可上调）。基线取自 2026-09-22 实测：
#   Api=11.05%（端点编排/Playwright/Worker 单测覆盖不到，由集成测试兜）→ 下限 10
#   Application=70.28%（纯逻辑，应高覆盖）→ 下限 68
THRESHOLDS="${THRESHOLDS:-AI.TestPlatform.Api=10,AI.TestPlatform.Application=68}"

DOTNET_BIN="${DOTNET_BIN:-}"
[ -z "$DOTNET_BIN" ] && for p in "/c/Program Files/dotnet/dotnet.exe" "$(command -v dotnet 2>/dev/null || true)"; do
  [ -n "$p" ] && [ -x "$p" ] && DOTNET_BIN="$p" && break
done

PY="${PY:-}"
[ -z "$PY" ] && for p in \
  "/c/Users/${USERNAME:-$USER}/.workbuddy/binaries/python/envs/default/Scripts/python.exe" \
  "$(command -v python3 2>/dev/null || true)" "$(command -v python 2>/dev/null || true)"; do
  [ -n "$p" ] && [ -x "$p" ] && PY="$p" && break
done

echo "==> 采集覆盖率（单元测试）"
rm -rf "$OUT"
(
  cd "$ROOT/backend" || exit 1
  # dotnet 参数不能用 POSIX 绝对路径（/d/... 会被解析成 D:\d\...），故用相对路径
  env "APPDATA=${APPDATA:-$HOME/AppData/Roaming}" \
      "LOCALAPPDATA=${LOCALAPPDATA:-$HOME/AppData/Local}" \
      "PROGRAMFILES=${PROGRAMFILES:-C:/Program Files}" \
      "ProgramFiles(x86)=${ProgramFiles(x86):-C:/Program Files (x86)}" \
      "PROGRAMDATA=${PROGRAMDATA:-C:/ProgramData}" \
      "$DOTNET_BIN" test tests/AI.TestPlatform.UnitTests \
        --collect:"XPlat Code Coverage" \
        --settings ../coverlet.runsettings \
        --results-directory ../.run/coverage \
        --nologo -v q
) >>"$LOG_DIR/coverage.log" 2>&1
TEST_EXIT=$?
echo "    单测退出码=$TEST_EXIT（0=全通过） 日志：$LOG_DIR/coverage.log"

echo "==> 覆盖率门禁（${THRESHOLDS}）"
"$PY" "$SCRIPT_DIR/coverage-gate.py" --report "$OUT/**/coverage.cobertura.xml" \
  --thresholds "$THRESHOLDS"
GATE_EXIT=$?

if [ "$TEST_EXIT" -ne 0 ]; then echo "!! 单测未全通过"; exit "$TEST_EXIT"; fi
exit "$GATE_EXIT"
