#!/usr/bin/env bash
# =============================================================================
# AI 自动化测试平台 —— 一键停止脚本
#
# 停止顺序：前端 → 后端 → AIWorker（PostgreSQL 容器默认保留，加 --with-db 一并停止）
#
# 用法：
#   bash scripts/stop-all.sh              # 停止三个应用服务
#   bash scripts/stop-all.sh --with-db    # 同时停止 PostgreSQL 容器
#
# 说明：服务进程按「监听端口」定位真实 Windows PID 后结束，
#       不使用 shell 的 $!（MSYS 模拟 PID，与 Windows PID 不一致，直接使用会误杀进程）。
# =============================================================================

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

BACKEND_PORT="${BACKEND_PORT:-5210}"
FRONTEND_PORT="${FRONTEND_PORT:-3000}"
WORKER_PORT="${WORKER_PORT:-8000}"
PG_CONTAINER="${PG_CONTAINER:-aitest-postgres-1}"
WSL_DISTRO="${WSL_DISTRO:-Ubuntu}"

WITH_DB=0
while [ $# -gt 0 ]; do
  case "$1" in
    --with-db) WITH_DB=1 ;;
    -h|--help) sed -n '2,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "未知参数：$1"; exit 1 ;;
  esac
  shift
done

if [ -t 1 ]; then
  C_G=$'\033[32m'; C_Y=$'\033[33m'; C_D=$'\033[90m'; C_B=$'\033[36m'; C_0=$'\033[0m'
else
  C_G=''; C_Y=''; C_D=''; C_B=''; C_0=''
fi
ok()   { printf '  %s✓%s %s\n' "$C_G" "$C_0" "$1"; }
warn() { printf '  %s!%s %s\n' "$C_Y" "$C_0" "$1"; }

port_pids() { netstat -ano 2>/dev/null | grep ":$1 " | grep LISTENING | awk '{print $NF}' | sort -u; }

stop_service() { # stop_service <名称> <端口>
  local name="$1" port="$2" p killed=0
  for p in $(port_pids "$port"); do
    if taskkill /PID "$p" /F /T >/dev/null 2>&1; then
      ok "已停止 $name（PID $p，端口 $port）"; killed=1
    fi
  done
  [ "$killed" = "0" ] && warn "$name 未在运行"
}

printf '\n%s==> 停止应用服务%s\n' "$C_B" "$C_0"
stop_service frontend "$FRONTEND_PORT"
stop_service backend  "$BACKEND_PORT"
stop_service worker   "$WORKER_PORT"

if [ "$WITH_DB" = "1" ]; then
  printf '\n%s==> 停止 PostgreSQL 容器%s\n' "$C_B" "$C_0"
  if wsl -d "$WSL_DISTRO" -e docker stop "$PG_CONTAINER" >/dev/null 2>&1; then
    ok "容器 $PG_CONTAINER 已停止"
  elif docker stop "$PG_CONTAINER" >/dev/null 2>&1; then
    ok "容器 $PG_CONTAINER 已停止"
  else
    warn "容器 $PG_CONTAINER 未在运行或停止失败"
  fi

fi

printf '\n'
