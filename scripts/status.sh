#!/usr/bin/env bash
# =============================================================================
# AI 自动化测试平台 —— 服务状态检查
#
# 用法：bash scripts/status.sh
# =============================================================================

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RUN_DIR="$ROOT/.run"

BACKEND_PORT="${BACKEND_PORT:-5210}"
FRONTEND_PORT="${FRONTEND_PORT:-3000}"
WORKER_PORT="${WORKER_PORT:-8000}"
PG_CONTAINER="${PG_CONTAINER:-aitest-postgres-1}"
WSL_DISTRO="${WSL_DISTRO:-Ubuntu}"

if [ -t 1 ]; then
  C_G=$'\033[32m'; C_R=$'\033[31m'; C_B=$'\033[36m'; C_D=$'\033[90m'; C_0=$'\033[0m'
else
  C_G=''; C_R=''; C_B=''; C_D=''; C_0=''
fi

port_pid() { netstat -ano 2>/dev/null | grep ":$1 " | grep LISTENING | awk '{print $NF}' | head -1; }
# MSYS 下 curl -o /dev/null 会返回退出码 23，故统一用「取状态码」方式探测
http_code() { curl -s --noproxy '*' -o - --max-time 3 -w '\n%{http_code}' "$1" 2>/dev/null | tail -1; }
mark()    { [ "$1" = "1" ] && printf '%s运行中%s' "$C_G" "$C_0" || printf '%s未运行%s' "$C_R" "$C_0"; }

printf '\n%s==> 服务状态%s\n' "$C_B" "$C_0"

# PostgreSQL 容器
db_state="$(wsl -d "$WSL_DISTRO" -e docker inspect -f '{{.State.Status}}' "$PG_CONTAINER" 2>/dev/null)"
[ -z "$db_state" ] && db_state="$(docker inspect -f '{{.State.Status}}' "$PG_CONTAINER" 2>/dev/null)"
if [ "$db_state" = "running" ]; then db_up=1; else db_up=0; fi
printf '  PostgreSQL    %s  %s容器 %s：%s%s\n' "$(mark "$db_up")" "$C_D" "$PG_CONTAINER" "${db_state:-未知}" "$C_0"

# 三个应用服务
for entry in "backend:$BACKEND_PORT:/health" "frontend:$FRONTEND_PORT:/" "worker:$WORKER_PORT:/openapi.json"; do
  name="${entry%%:*}"; rest="${entry#*:}"; port="${rest%%:*}"; path="${rest#*:}"
  pid="$(port_pid "$port")"
  up=0; [ -n "$pid" ] && up=1
  extra=""
  if [ "$up" = "1" ]; then
    extra="HTTP $(http_code "http://127.0.0.1:$port$path")"
  fi
  printf '  %-13s %s  %s端口 %s  PID %-8s %s%s\n' "$name" "$(mark "$up")" "$C_D" "$port" "${pid:--}" "$extra" "$C_0"
done

printf '\n  %s日志目录%s %s\n\n' "$C_D" "$C_0" "$RUN_DIR/logs"
