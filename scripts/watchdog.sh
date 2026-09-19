#!/usr/bin/env bash
# =============================================================================
# 服务体检 / 自愈守护（watchdog）
#
# 为什么需要它：这个栈有两种故障，而且都不会自己喊出来——
#   1) WSL 虚拟机在无客户端会话时被回收 → dockerd 停 → PostgreSQL 容器停。
#      容器本身有 `--restart unless-stopped`，但**前提是有人把 WSL 唤醒**；
#      没人碰它，它就一直是停的（后端随后也会因为连不上库而退出）。
#   2) 前端 / 后端 / AIWorker 进程消失，后端不会自己回来。
#
# 不盯着的话，症状是"页面打不开"或"接口报错"，等发现时半天已经过去了。
# 本脚本按间隔体检，掉了就拉起来，并且**只在出事的时刻写日志**——
# 所以早上扫一眼 .run/logs/watchdog.log 就知道夜里有没有自愈过、自愈了几次。
# 每天第一次运行会写一行"体检启动"，作为"它还活着"的证明。
#
# 用法：
#   bash scripts/watchdog.sh                  # 常驻，默认 120 秒一轮
#   bash scripts/watchdog.sh --once           # 只体检一次（排查看）
#                                             # ⚠ 它拉起的服务**不会比这条命令活得更久**：
#                                             #   进程是被这条命令的 shell 托着的，命令一返回就回收。
#                                             #   要恢复得"留住"，必须用常驻模式（或自己跑 start-all.sh）。
#   bash scripts/watchdog.sh --interval 60    # 自定义间隔（秒）
#   bash scripts/watchdog.sh --quiet          # 只写日志，不往屏幕打
#
# 构建 / 迁移时想让它别插手：
#   touch .run/watchdog.pause     # 暂停自动恢复（常驻循环生效，--once 不受影响）
#   rm .run/watchdog.pause        # 恢复
#   —— 否则守护会在你起构建、服务正被停掉的瞬间把后端拉回来，锁住 bin 下的 DLL，
#      构建直接报 MSB3027「文件被 .NET Host 锁定」（踩过）。
#
# 说明：
#   - 探针一律用**真实 TCP 连接 / HTTP 状态码**，不用 netstat，也不看
#     `wsl -l -v` 的 STATE 列（那一列反映的是"有没有客户端会话"，不是存活状态，
#     本项目在它上面误判过两次）。
#   - 恢复动作全部复用 start-all.sh，不在这里重写一遍数据库/服务的启动逻辑
#     （同一件事两处实现必然分叉）。
# =============================================================================

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RUN_DIR="$ROOT/.run"
LOG_DIR="$RUN_DIR/logs"
mkdir -p "$LOG_DIR"

WATCHDOG_LOG="$LOG_DIR/watchdog.log"
LOCK="$RUN_DIR/watchdog.lock"
# 存在这个文件时，常驻循环会跳过自动恢复（构建 / 迁移前 touch，完事删掉）
PAUSE_FILE="$RUN_DIR/watchdog.pause"
START_ALL="$SCRIPT_DIR/start-all.sh"

BACKEND_PORT="${BACKEND_PORT:-5210}"
FRONTEND_PORT="${FRONTEND_PORT:-3000}"
WORKER_PORT="${WORKER_PORT:-8000}"
PG_PORT="${PG_PORT:-5432}"

INTERVAL=120
ONCE=0
QUIET=0

while [ $# -gt 0 ]; do
  case "$1" in
    --once)     ONCE=1 ;;
    --interval) shift; INTERVAL="${1:-120}" ;;
    --quiet)    QUIET=1 ;;
    -h|--help)  sed -n '2,30p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "未知参数：$1" >&2; exit 2 ;;
  esac
  shift
done

C_B=$'\033[1m'; C_G=$'\033[32m'; C_Y=$'\033[33m'; C_R=$'\033[31m'; C_D=$'\033[90m'; C_0=$'\033[0m'
[ -t 1 ] || { C_B=''; C_G=''; C_Y=''; C_R=''; C_D=''; C_0=''; }

say()  { [ "$QUIET" = "1" ] || printf '%s\n' "$1"; }
ok()   { printf '  %s✓%s %s\n' "$C_G" "$C_0" "$1"; }
warn() { printf '  %s!%s %s\n' "$C_Y" "$C_0" "$1"; }
info() { printf '  %s·%s %s\n' "$C_D" "$C_0" "$1"; }

log_event() { printf '[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$1" >> "$WATCHDOG_LOG"; }

# --------------------------------- 探针 --------------------------------------
# 真实 TCP 连接。不用 netstat：它对 WSL 转发的端口给不出可信结论，
# 而 /dev/tcp 是真的建了一条连接。
tcp_open() {
  (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null && { exec 3<&- 2>/dev/null; return 0; }
  return 1
}

# MSYS 下 curl -o /dev/null 会因写入失败返回 23，因此取状态码而不是退出码
http_code() { curl -s --noproxy '*' -o - --max-time 5 -w '\n%{http_code}' "$1" 2>/dev/null | tail -1; }

probe_db()       { tcp_open "$PG_PORT"; }
probe_backend()  { [ "$(http_code "http://127.0.0.1:$BACKEND_PORT/health")" = "200" ]; }
probe_frontend() { tcp_open "$FRONTEND_PORT"; }
probe_worker()   { [ "$(http_code "http://127.0.0.1:$WORKER_PORT/openapi.json")" = "200" ]; }

# ------------------------------- 恢复动作 ------------------------------------
# 全部复用 start-all.sh：数据库用 --only db（它内部会唤醒 WSL 并确保容器在跑），
# 应用服务用 --only <名字> --skip-db（库这边由本脚本单独负责，避免重复跑一遍）。
# --append-logs：恢复拉起时不截断服务日志——旧日志里可能正是崩溃现场（审查发现）
recover_db()       { bash "$START_ALL" --only db       --no-build --append-logs >/dev/null 2>&1; }
recover_backend()  { bash "$START_ALL" --only backend  --no-build --skip-db --inherit-env --append-logs >/dev/null 2>&1; }
recover_frontend() { bash "$START_ALL" --only frontend --no-build --skip-db --inherit-env --append-logs >/dev/null 2>&1; }
recover_worker()   { bash "$START_ALL" --only worker   --no-build --skip-db --inherit-env --append-logs >/dev/null 2>&1; }

# 一次体检 + 自愈。返回 0 表示本轮无需恢复，1 表示发生过恢复
run_pass() {
  local down=() name

  probe_db       || down+=(db)
  probe_backend  || down+=(backend)
  probe_frontend || down+=(frontend)
  probe_worker   || down+=(worker)

  if [ ${#down[@]} -eq 0 ]; then
    return 0
  fi

  # 顺序有意义：库必须先好，否则起来的后端立刻又会连不上库而退出
  local failure=0
  for name in db backend frontend worker; do
    local hit=0 d
    for d in "${down[@]}"; do [ "$d" = "$name" ] && hit=1; done
    [ "$hit" = "1" ] || continue

    say "$(printf '%s[异常]%s %s 不可用，正在拉起 …' "$C_Y" "$C_0" "$name")"
    log_event "检测到 $name 不可用，开始恢复"

    # 恢复链最坏可达数分钟（后端重试 2×60s + 前端 90s），超过锁阈值（INTERVAL×3）
    # 的话另一个实例会判定「心跳过期」强行接管，造成并发恢复（审查发现）——
    # 每完成一个服务的恢复就刷一次心跳
    touch "$LOCK"

    if "recover_$name"; then
      # 恢复动作本身返回成功还不够，必须**再探一次**才算数
      if "probe_$name"; then
        ok "$name 已恢复"
        log_event "$name 恢复成功"
      else
        warn "$name 拉起后仍不可用"
        log_event "$name 恢复失败（拉起后探针仍不通）"
        failure=1
      fi
    else
      warn "$name 恢复命令执行失败"
      log_event "$name 恢复命令执行失败"
      failure=1
    fi
  done

  [ "$failure" = "0" ] && log_event "本轮异常已全部恢复" || log_event "本轮仍有未恢复项，下轮重试"
  return 1
}

# --------------------------------- 锁 ----------------------------------------
# 用锁文件的 mtime 当心跳：每轮刷新。若发现一份"心跳很新"的锁，说明已经有一个在跑。
# 这样不需要跨 MSYS/Windows 比较 PID（MSYS 的 $! 不是 Windows PID，用它判断会误杀）。
# --once 也走同一把锁（审查发现）：否则一次性体检可与常驻循环并发触发 start-all，
# 两边同时拉起同一个服务。
acquire_lock() {
  if [ -f "$LOCK" ]; then
    local beat age
    beat="$(stat -c %Y "$LOCK" 2>/dev/null || echo 0)"
    age=$(( $(date +%s) - beat ))
    if [ "$age" -lt $(( INTERVAL * 3 )) ]; then
      say "已有体检进程在运行（${age}s 前还有心跳），本次退出"
      exit 0
    fi
    warn "发现过期锁（心跳停在 ${age}s 前），接管"
  fi
  touch "$LOCK"
}

release_lock() { rm -f "$LOCK"; }

# --------------------------------- 主流程 ------------------------------------

if [ "$ONCE" = "1" ]; then
  # --once 也走锁（审查发现）：不锁的话一次性体检可与常驻循环并发触发 start-all，
  # 两边同时拉起同一个服务。锁被常驻占用（心跳新鲜）时直接退出——常驻会处理恢复。
  acquire_lock
  trap release_lock EXIT INT TERM
  say "$(printf '%s==> 体检一次%s' "$C_B" "$C_0")"
  # 注意先存结果再 printf：printf 会把 $? 覆盖掉
  if run_pass; then
    printf '\n'
    ok "四项服务全部正常"
  else
    printf '\n'
    warn "本轮发生过恢复，详见 $WATCHDOG_LOG"
  fi
  exit 0
fi

acquire_lock
trap release_lock EXIT INT TERM

say "$(printf '%s==> 服务体检守护已启动%s（间隔 %ss，日志 %s）' "$C_B" "$C_0" "$INTERVAL" "$WATCHDOG_LOG")"

# 每天第一次运行记一行"体检启动"，作为存活证明——否则全绿的日子里日志是空的，
# 分不清"没出事"和"根本没跑"
last_day=""
paused_logged=0
while true; do
  today="$(date '+%Y-%m-%d')"
  if [ "$today" != "$last_day" ]; then
    log_event "体检启动（间隔 ${INTERVAL}s，监控 db/backend/frontend/worker）"
    last_day="$today"
  fi

  # 暂停开关：构建 / 迁移期间服务会被反复重启，守护这时候把后端起回来
  # 会锁住 bin 下的 DLL，导致构建报 MSB3027（踩过）。
  # 做法：构建前 `touch .run/watchdog.pause`，构建完删掉即可。
  # 暂停只在常驻循环里生效——手动跑 --once 是明确意图，照常体检。
  if [ -f "$PAUSE_FILE" ]; then
    if [ "$paused_logged" = "0" ]; then
      log_event "检测到 $PAUSE_FILE，暂停自动恢复（不会再动任何服务）"
      say "$(printf '%s[暂停]%s 存在 %s，本轮起跳过自动恢复' "$C_Y" "$C_0" "$PAUSE_FILE")"
      paused_logged=1
    fi
  else
    if [ "$paused_logged" = "1" ]; then
      log_event "暂停标记已移除，恢复自动恢复"
      paused_logged=0
    fi
    run_pass
  fi

  touch "$LOCK"
  sleep "$INTERVAL"
done
