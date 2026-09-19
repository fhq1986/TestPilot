#!/usr/bin/env bash
# =============================================================================
# AI 自动化测试平台 —— 本地一键启动脚本（Windows + Git Bash + WSL Docker）
#
# 启动顺序：
#   1) PostgreSQL 容器（WSL Ubuntu 内的 Docker）
#   2) AIWorker      (FastAPI + uvicorn, 127.0.0.1:8000)
#   3) 后端 API      (.NET 8, 127.0.0.1:5210)
#   4) 前端 Web      (Vite,     127.0.0.1:3000)
#
# 用法：
#   bash scripts/start-all.sh                 # 全量启动（自动构建/装依赖/建库）
#   bash scripts/start-all.sh --no-build      # 跳过构建与依赖安装
#   bash scripts/start-all.sh --rebuild       # 强制重新编译后端
#   bash scripts/start-all.sh --skip-db       # 跳过数据库检查（假定已就绪）
#   bash scripts/start-all.sh --only backend  # 只启动指定服务（可重复：backend|frontend|worker）
#   bash scripts/start-all.sh --only db --no-build   # 只准备数据库（唤醒 WSL + 确保容器在跑），不启服务
#                                                    # 供 watchdog.sh 恢复数据库时复用
#   bash scripts/start-all.sh --clean-env     # 强制后端用「干净环境变量」启动
#   bash scripts/start-all.sh --inherit-env   # 强制后端继承当前环境变量启动
#   bash scripts/start-all.sh -h              # 帮助
#
# 说明：部分 IDE 内置终端会向 Windows 子进程注入重复的 Path/PATH 或安全垫片目录，
#       导致 ASP.NET 进程静默退出。脚本会自动识别并为后端切换到干净环境变量模式。
# =============================================================================

set -uo pipefail

# ------------------------------- 路径与常量 ---------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RUN_DIR="$ROOT/.run"
LOG_DIR="$RUN_DIR/logs"
mkdir -p "$LOG_DIR"

BACKEND_PORT="${BACKEND_PORT:-5210}"
FRONTEND_PORT="${FRONTEND_PORT:-3000}"
WORKER_PORT="${WORKER_PORT:-8000}"
# 数据库在 Windows 侧映射的端口（watchdog.sh 用的是同名变量，保持一致）
PG_PORT="${PG_PORT:-5432}"

PG_CONTAINER="${PG_CONTAINER:-aitest-postgres-1}"
PG_IMAGE="${PG_IMAGE:-swr.cn-north-4.myhuaweicloud.com/ddn-k8s/docker.io/pgvector/pgvector:pg18}"
PG_USER="${POSTGRES_USER:-postgres}"
PG_PASSWORD="${POSTGRES_PASSWORD:-postgres}"
PG_DB="${POSTGRES_DB:-ai_test}"
WSL_DISTRO="${WSL_DISTRO:-Ubuntu}"

BACKEND_DIR="$ROOT/backend/src/AI.TestPlatform.Api"
BACKEND_DLL="$BACKEND_DIR/bin/Debug/net8.0/AI.TestPlatform.Api.dll"
# 传给 dotnet 的必须是 Windows 能识别的相对路径（POSIX 绝对路径会被当成命令名解析而报错）
BACKEND_DLL_ARG="bin/Debug/net8.0/AI.TestPlatform.Api.dll"
FRONTEND_DIR="$ROOT/frontend"
WORKER_DIR="$ROOT/backend/src/AIWorker"

PIP_MIRROR="${PIP_MIRROR:-https://mirrors.aliyun.com/pypi/simple/}"

# 本机回环直连：部分宿主会注入 HTTP_PROXY（且不带 NO_PROXY），
# 会导致后端/浏览器把 localhost 调用交给代理，出现「绝对 URI 请求行」而路由 404。
# 这里统一为所有子进程声明代理白名单（NO_PROXY / no_proxy 同时设置，兼容大小写实现）。
LOCAL_NO_PROXY="${NO_PROXY:-127.0.0.1,localhost,::1,0.0.0.0}"
export NO_PROXY="$LOCAL_NO_PROXY"
export no_proxy="$LOCAL_NO_PROXY"

# ------------------------------- 参数解析 -----------------------------------
BUILD=1; REBUILD=0; CHECK_DB=1; ENV_MODE_OPT="auto"; ONLY=(); APPEND_LOGS=0
while [ $# -gt 0 ]; do
  case "$1" in
    --no-build)    BUILD=0 ;;
    --rebuild)     REBUILD=1 ;;
    --skip-db)     CHECK_DB=0 ;;
    --clean-env)   ENV_MODE_OPT="clean" ;;
    --inherit-env) ENV_MODE_OPT="inherit" ;;
    --append-logs) APPEND_LOGS=1 ;;
    --only)        shift; ONLY+=("${1:-}") ;;
    -h|--help)     sed -n '2,25p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "未知参数：$1（用 -h 查看帮助）"; exit 1 ;;
  esac
  shift
done

want() { # want <service>：本次是否需要启动该服务
  [ "${#ONLY[@]}" -eq 0 ] && return 0
  local s; for s in "${ONLY[@]}"; do [ "$s" = "$1" ] && return 0; done
  return 1
}

# ------------------------------- 输出工具 -----------------------------------
if [ -t 1 ]; then
  C_G=$'\033[32m'; C_Y=$'\033[33m'; C_R=$'\033[31m'; C_B=$'\033[36m'; C_D=$'\033[90m'; C_0=$'\033[0m'
else
  C_G=''; C_Y=''; C_R=''; C_B=''; C_D=''; C_0=''
fi
step() { printf '\n%s==> %s%s\n' "$C_B" "$1" "$C_0"; }
ok()   { printf '  %s✓%s %s\n' "$C_G" "$C_0" "$1"; }
warn() { printf '  %s!%s %s\n' "$C_Y" "$C_0" "$1"; }
err()  { printf '  %s✗%s %s\n' "$C_R" "$C_0" "$1"; }
info() { printf '  %s·%s %s\n' "$C_D" "$C_0" "$1"; }

# ------------------------------- 工具探测 -----------------------------------
win_dir_of() { # 输入命令名或 POSIX 路径，输出其所在目录的 Windows 形式
  local p="$1" d
  [ -z "$p" ] && return 0
  d="$(dirname "$p")"
  case "$d" in
    [A-Za-z]:*) printf '%s' "${d//\//\\}" ;;
    *) cygpath -w "$d" 2>/dev/null | tr '/' '\\' ;;
  esac
}

resolve_tools() {
  BASH_BIN="$(command -v bash || echo /usr/bin/bash)"

  DOTNET_BIN="$(command -v dotnet 2>/dev/null || true)"
  if [ -z "$DOTNET_BIN" ]; then
    for p in "/c/Program Files/dotnet/dotnet.exe" "$HOME/.dotnet/dotnet.exe"; do
      [ -x "$p" ] && DOTNET_BIN="$p" && break
    done
  fi
  DOTNET_WIN_DIR="$(win_dir_of "$DOTNET_BIN")"

  NODE_BIN="$(command -v node 2>/dev/null || true)"
  # PATH 里没有 node 时，回退到 WorkBuddy 托管 node。
  # **必须用通配而不是写死版本号**：托管目录名带版本（如 22.22.2-3），
  # 升级一次就变，写死会突然报找不到（前端因此起不来，实测踩过）。
  if [ -z "$NODE_BIN" ]; then
    for c in "$HOME"/.workbuddy/binaries/node/versions/*/node.exe; do
      [ -f "$c" ] && NODE_BIN="$c" && break
    done
  fi
  NPM_BIN="$(command -v npm 2>/dev/null || true)"
  NODE_WIN_DIR="$(win_dir_of "$NODE_BIN")"
  # npm 本体（避免依赖 shell PATH，直接用 node 执行 npm-cli.js）
  NPM_CLI_JS=""
  if [ -n "$NODE_BIN" ]; then
    local nd; nd="$(dirname "$NODE_BIN")"
    for p in "$nd/node_modules/npm/bin/npm-cli.js" "$nd/../lib/node_modules/npm/bin/npm-cli.js"; do
      [ -f "$p" ] && NPM_CLI_JS="$p" && break
    done
  fi

  # Python：项目 venv → WorkBuddy 托管 venv → 系统 python
  PYTHON_BIN=""
  for p in \
    "$WORKER_DIR/.venv/Scripts/python.exe" \
    "$WORKER_DIR/venv/Scripts/python.exe" \
    "/c/Users/${USERNAME:-$USER}/.workbuddy/binaries/python/envs/default/Scripts/python.exe" \
    "$(command -v python 2>/dev/null || true)" \
    "$(command -v python3 2>/dev/null || true)"; do
    [ -n "$p" ] && [ -x "$p" ] && PYTHON_BIN="$p" && break
  done
  PYTHON_WIN_DIR="$(win_dir_of "$PYTHON_BIN")"

  npm_run() { # 用 node 直接执行 npm-cli.js，规避 PATH 依赖
    if [ -n "$NPM_CLI_JS" ]; then "$NODE_BIN" "$NPM_CLI_JS" "$@"
    elif [ -n "$NPM_BIN" ]; then "$NPM_BIN" "$@"
    else return 127; fi
  }
}

# --------------------------- 后端启动环境模式 --------------------------------
# 返回 0 表示需要「干净环境变量」模式
needs_clean_env() {
  [ "$ENV_MODE_OPT" = "clean" ]   && return 0
  [ "$ENV_MODE_OPT" = "inherit" ] && return 1
  # auto：识别常见的异常环境
  [ "$(env 2>/dev/null | wc -l)" -lt 5 ] && return 0          # 环境块几乎为空
  case "${PATH:-}" in *safe-bin*) return 0 ;; esac            # 安全垫片目录注入
  case "${PATH:-}" in *".asar"*)  return 0 ;; esac            # IDE 内置终端
  case "${PATH:-}" in
    [A-Za-z]:\\*|[A-Za-z]:/*) return 0 ;;                     # PATH 首段为 Windows 风格
  esac
  return 1
}

build_clean_prefix() {
  CLEAN_PREFIX=()
  local win_home; win_home="$(cygpath -w "$HOME" 2>/dev/null)"
  [ -z "$win_home" ] && win_home="C:\\Users\\${USERNAME:-user}"
  local clean_path="C:\\Windows\\system32;C:\\Windows"
  [ -n "$DOTNET_WIN_DIR" ] && clean_path="$clean_path;$DOTNET_WIN_DIR"
  [ -n "$NODE_WIN_DIR" ]   && clean_path="$clean_path;$NODE_WIN_DIR"
  [ -n "$PYTHON_WIN_DIR" ] && clean_path="$clean_path;$PYTHON_WIN_DIR"

  CLEAN_PREFIX=( env -u PATH
    "Path=$clean_path"
    "PATHEXT=.COM;.EXE;.BAT;.CMD;.VBS;.JS;.WS"
    "OS=Windows_NT"
    "SystemRoot=C:\\Windows"
    "windir=C:\\Windows"
    "SystemDrive=C:"
    "TEMP=$win_home\\AppData\\Local\\Temp"
    "TMP=$win_home\\AppData\\Local\\Temp"
    "APPDATA=$win_home\\AppData\\Roaming"
    "LOCALAPPDATA=$win_home\\AppData\\Local"
    "USERPROFILE=$win_home"
    "PROGRAMFILES=C:\\Program Files"
    "ProgramFiles(x86)=C:\\Program Files (x86)"
    "PROGRAMDATA=C:\\ProgramData"
    "COMPUTERNAME=${COMPUTERNAME:-DESKTOP}"
  )
}

# ------------------------------- 服务启动 -----------------------------------
RUN_ENV=()      # 本次 spawn 附带的变量（如 ASPNETCORE_*）
RUN_PREFIX=()   # 本次 spawn 的环境前缀
CLEAN_PREFIX=() # 干净环境变量模式的前缀（只在触发时构建；set -u 下空数组也必须先声明）
PS_EXE="/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe"
[ -x "$PS_EXE" ] || PS_EXE="$(command -v powershell.exe 2>/dev/null || true)"

spawn() { # spawn [--append] <名称> <工作目录> <命令...>
  local append=0
  # 选项必须在 name/workdir 解析之前处理（shift 2 会先吃掉前两个位置参数）
  if [ "$1" = "--append" ]; then append=1; shift; fi
  local name="$1" workdir="$2"; shift 2

  local log="$LOG_DIR/$name.log"
  # append：失败重试的第二次 spawn 保留首次日志（首次报错是唯一诊断线索，截断等于销毁证据）；
  # --append-logs（watchdog 恢复路径用）：整次调用都不截断——恢复拉起时旧日志里
  # 可能正是崩溃现场，清掉它等于销毁诊断线索。
  if [ "$append" = "1" ] || [ "$APPEND_LOGS" = "1" ]; then
    printf '\n===== spawn %s @ %s =====\n' "$name" "$(date '+%H:%M:%S')" >>"$log"
  else
    : >"$log"
  fi

  # ⚠ 为什么不走 nohup … &：
  #   MSYS(Git Bash) 下用 & 把 native 子进程（dotnet.exe / node.exe 等）放到后台，
  #   进程会被静默杀死——日志 0 字节、端口不监听、事件日志也没有崩溃记录，
  #   而前台跑同一条命令完全正常（有无 nohup、父 shell 是否存活都不影响结局，实测多日）。
  #   于是改为：把启动命令写成 Windows 批处理（环境变量也在批处理里 set），
  #   用 PowerShell Start-Process 以「独立隐藏控制台」拉起——彻底脱离 MSYS 进程树，
  #   不受终端关闭 / Ctrl+C 影响，任何调用方（含 watchdog）都可用。
  local dir_win log_win cmd_win prog_win a argstr=""
  dir_win="$(cygpath -w "$workdir")"
  log_win="$(cygpath -w "$log")"
  cmd_win="$(cygpath -w "$RUN_DIR/spawn-$name.cmd")"
  prog_win="$(cygpath -w "$1")"; shift
  for a in "$@"; do
    # POSIX 绝对路径参数（如 worker-run.sh、npm-cli.js）转成 Windows 形式；相对路径不动
    case "$a" in
      /[A-Za-z]/*) a="$(cygpath -w "$a")" ;;
    esac
    argstr="$argstr \"${a//\"/\\\"}\""
  done

  # RUN_PREFIX / RUN_ENV 里 NAME=VALUE 形式的元素 → 批处理里的 set 行
  # （env / -u / PATH 是旧 env 前缀的控制元素，跳过；Path=... 会整体覆盖继承来的 PATH）
  {
    printf '@echo off\r\n'
    printf 'cd /d "%s"\r\n' "$dir_win"
    local e
    for e in ${RUN_PREFIX[@]+"${RUN_PREFIX[@]}"} ${RUN_ENV[@]+"${RUN_ENV[@]}"}; do
      case "$e" in
        env|-u|PATH) continue ;;
        *=*) printf 'set "%s"\r\n' "$e" ;;
      esac
    done
    printf '"%s"%s >> "%s" 2>&1\r\n' "$prog_win" "$argstr" "$log_win"
  } >"$RUN_DIR/spawn-$name.cmd"

  if [ -n "$PS_EXE" ]; then
    "$PS_EXE" -NoProfile -NonInteractive -Command \
      "Start-Process -FilePath '$cmd_win' -WindowStyle Hidden" \
      || err "启动 $name 失败（PowerShell Start-Process 返回非零）"
  else
    # 兜底：极罕见地没有 PowerShell 时退回旧方案（子 shell 延迟退出，降低静默死亡概率）
    (
      cd "$workdir" || exit 1
      nohup "$@" >>"$log" 2>&1 &
      disown 2>/dev/null || true
      sleep 2
    )
  fi
  RUN_ENV=(); RUN_PREFIX=()
}

# ------------------------------- 探测工具 -----------------------------------
# 端口是否在监听。
# ⚠ 不写成 `netstat | grep -q`：本脚本开了 pipefail，grep -q 命中即退会让 netstat 吃 SIGPIPE，
# 管道被判失败 —— 判定会**静默翻转**成"没在监听"，进而重复拉起服务。
# （现在之所以还能工作，只是因为 netstat 输出小、整块写进了管道缓冲区；这是运气，不是正确。）
port_listening() {
  local out; out="$(netstat -ano 2>/dev/null || true)"
  grep -q ":$1 .*LISTENING" <<<"$out"
}
# 注意：MSYS 下 curl -o /dev/null 会因写入失败返回退出码 23，
#       因此这里显式取 HTTP 状态码判断，而不是依赖 curl 的退出码。
http_code() { curl -s --noproxy '*' -o - --max-time 3 -w '\n%{http_code}' "$1" 2>/dev/null | tail -1; }
http_ok()   { [ "$(http_code "$1")" = "200" ]; }

wait_for() { # wait_for <秒> <描述> <命令...>
  local secs="$1" desc="$2"; shift 2
  local i=0
  while [ "$i" -lt "$secs" ]; do
    if "$@" >/dev/null 2>&1; then ok "$desc"; return 0; fi
    sleep 1; i=$((i+1))
    [ $((i % 10)) -eq 0 ] && info "等待 $desc …（${i}s/${secs}s）"
  done
  err "$desc 超时（${secs}s）"
  return 1
}

# =============================================================================
step "1/5 检查运行环境"
# =============================================================================
resolve_tools

[ -n "$DOTNET_BIN" ] && ok ".NET SDK      ${C_D}$("$DOTNET_BIN" --version 2>/dev/null | head -1)${C_0}" \
                     || err ".NET SDK 未找到（需 .NET 8 SDK）"
[ -n "$NODE_BIN" ]   && ok "Node.js       ${C_D}$("$NODE_BIN" -v 2>/dev/null)${C_0}" \
                     || err "Node.js 未找到"
[ -n "$PYTHON_BIN" ] && ok "Python        ${C_D}$("$PYTHON_BIN" -V 2>&1 | head -1)${C_0}" \
                     || err "Python 未找到"

if needs_clean_env; then
  build_clean_prefix
  BACKEND_IN_CLEAN_ENV=1
  warn "当前终端环境会干扰 .NET 进程启动，后端将使用「干净环境变量」模式"
  info "如需强制其它模式：--clean-env / --inherit-env"
else
  BACKEND_IN_CLEAN_ENV=0
fi

# =============================================================================
step "2/5 准备 PostgreSQL（WSL Docker）"
# =============================================================================
DOCKER_MODE="none"
detect_docker() {
  if docker info >/dev/null 2>&1; then DOCKER_MODE=local
  elif wsl -d "$WSL_DISTRO" -e docker info >/dev/null 2>&1; then DOCKER_MODE=wsl
  else DOCKER_MODE=none; fi
}
docker_sh() { # 统一的 docker 调用入口
  if [ "$DOCKER_MODE" = "wsl" ]; then wsl -d "$WSL_DISTRO" -e docker "$@"
  else docker "$@"; fi
}

# -----------------------------------------------------------------------------
# WSL 生命周期提示：**这是"数据库莫名停止"的根因**
#
# 现象：过一段时间（比如隔夜），PostgreSQL 变成 `Exited (0)`，后端连不上库直接退出。
# 看起来像数据库自己崩了，实际是宿主虚拟机没了：
#   WSL 在没有**客户端会话**时回收虚拟机 → dockerd 停 → 容器被优雅关闭（Exited 0）
#
# 排查时别被骗的两个地方：
#   1) 容器退出码是 0（优雅关闭），不是崩溃码；
#   2) `wsl -l -v` 的 STATE 列反映的是"有没有客户端会话"，
#      虚拟机还在正常服务时它也可能显示 Stopped —— **这个列不能当存活判据**，
#      判断数据库在不在要用真实 TCP 连接（见下面的 probe 提示）。
#
# 试过但**无效**的两种做法（别再走一遍）：
#   - [wsl2] vmIdleTimeout=86400000：官方文档确有此项（默认 60000），
#     但在本机 WSL 2.7.14 上实测无效——静默 110 秒后虚拟机照样被回收。
#   - `wsl -e sleep infinity` 当保活进程：进程会随调用它的 shell 一起死，
#     shell 一退出 WSL 立刻被回收。看着像解法，其实是假的。
#
# 真正有效的：让 **Windows 侧常驻一个附着的 wsl.exe 客户端**（例如把一个
# `wsl -d <发行版> -e sleep infinity` 窗口一直开着，或做成登录时自启的计划任务）。
# 不打算这么做的，就接受"闲置后数据库会停"，靠再跑一次本脚本恢复——
# 容器已设 unless-stopped，唤醒 WSL 后会自动拉起，不需要手工 docker start。
wsl_lifecycle_note() {
  [ "$DOCKER_MODE" = "wsl" ] || return 0
  info "Docker 跑在 WSL（$WSL_DISTRO）内：虚拟机在无客户端会话时会被回收"
  info "  · 闲置一段时间后 docker 与 PostgreSQL 容器会一起停（容器显示 Exited 0）"
  info "  · 恢复方式：再跑一次本脚本即可（唤醒 WSL 后容器会自动拉起）"
  info "  · 想长期不停：让 Windows 侧常驻一个 'wsl -d $WSL_DISTRO -e sleep infinity'"
}

if [ "$CHECK_DB" = "1" ]; then
  detect_docker
  if [ "$DOCKER_MODE" = "none" ]; then
    err "Docker 不可用（本地与 WSL 均探测失败）"
    warn "请在 WSL 内启动 dockerd：wsl -d $WSL_DISTRO -e sudo service docker start"
    warn "或使用 --skip-db 跳过数据库检查"
  else
    [ "$DOCKER_MODE" = "wsl" ] && info "Docker 入口：WSL（$WSL_DISTRO）" || info "Docker 入口：本地"
    wsl_lifecycle_note
    state="$(docker_sh inspect -f '{{.State.Status}}' "$PG_CONTAINER" 2>/dev/null)"
    if [ "$state" = "running" ]; then
      ok "容器 $PG_CONTAINER 已在运行"
    elif [ -n "$state" ]; then
      info "容器 $PG_CONTAINER 当前状态 $state，正在启动 …"
      docker_sh start "$PG_CONTAINER" >/dev/null 2>&1 && ok "容器已启动" || err "容器启动失败"
    else
      info "容器不存在，按镜像 $PG_IMAGE 创建 …"
      # --restart unless-stopped：dockerd（含 WSL 被唤醒后）重启时自动拉起。
      # 不加这个的话容器默认策略是 no，dockerd 一重启它就一直躺着不动。
      # ⚠ 卷必须挂在 /var/lib/postgresql/data（镜像的 PGDATA），挂在上一层会导致
      #   初始化脚本落点偏移、重建容器丢数据。老卷 aitest_pgdata 的数据在 data/ 子目录，
      #   从旧挂载迁移时：docker run --rm -v aitest_pgdata:/v alpine sh -c 'cp -a /v/data/. /new/'
      docker_sh run -d --name "$PG_CONTAINER" --restart unless-stopped \
        -e "POSTGRES_USER=$PG_USER" -e "POSTGRES_PASSWORD=$PG_PASSWORD" -e "POSTGRES_DB=$PG_DB" \
        -p 5432:5432 -v aitest_pgdata:/var/lib/postgresql/data "$PG_IMAGE" >/dev/null 2>&1 \
        && ok "容器已创建" \
        || err "容器创建失败（可在 WSL 内先执行 docker pull $PG_IMAGE）"
    fi
    # 老容器（脚本早期版本创建）没有重启策略，这里幂等补一次
    docker_sh update --restart unless-stopped "$PG_CONTAINER" >/dev/null 2>&1 \
      && ok "容器重启策略：unless-stopped" \
      || warn "未能设置容器重启策略，dockerd 重启后它可能不会自动恢复"
    # 复用同一个 port_listening（而不是再写一遍 netstat|grep）：
    # 「端口在不在监听」只该有一处实现，否则迟早分叉
    wait_for 60 "PostgreSQL 就绪（Windows 侧 5432）" port_listening "$PG_PORT" \
      || warn "端口未就绪，后端连接数据库可能失败"

    # WSL 保活（根治「隔夜数据库又停」，2026-09-15 事故）：
    # WSL 虚拟机在没有客户端会话时会被回收，回收后 dockerd/PG 全停、
    # 后端连接池里的连接全坏——所有查库接口报 transient failure。
    # 起一个隐藏的 `wsl -e sleep infinity` 常驻会话即可让 WSL 永不回收。
    # 幂等：pid 文件里的进程还活着就跳过，不会堆出多个会话。
    keepalive_pid_file="$RUN_DIR/wsl-keepalive.pid"
    if [ -f "$keepalive_pid_file" ] \
      && powershell.exe -NoProfile -Command "Get-Process -Id (Get-Content '$(cygpath -w "$keepalive_pid_file")' -ErrorAction SilentlyContinue) -ErrorAction SilentlyContinue" >/dev/null 2>&1; then
      info "WSL 保活会话已在运行（PID $(cat "$keepalive_pid_file" 2>/dev/null)）"
    else
      powershell.exe -NoProfile -Command \
        "Start-Process wsl -ArgumentList '-d','$WSL_DISTRO','-e','sleep','infinity' -WindowStyle Hidden -PassThru | Select-Object -ExpandProperty Id" \
        >"$keepalive_pid_file" 2>/dev/null
      info "已启动 WSL 保活会话（PID $(cat "$keepalive_pid_file" 2>/dev/null)），数据库不再随闲置回收"
    fi
  fi
else
  info "已跳过数据库检查（--skip-db）"
fi

# =============================================================================
step "3/5 构建 / 依赖准备"
# =============================================================================
if [ "$BUILD" = "1" ]; then
  # --- 后端 ---
  if want backend; then
    if [ "$REBUILD" = "1" ] || [ ! -f "$BACKEND_DLL" ]; then
      info "编译后端（dotnet build）…"
      if ( cd "$ROOT/backend" && ${RUN_PREFIX[@]+"${RUN_PREFIX[@]}"} "$DOTNET_BIN" build src/AI.TestPlatform.Api --nologo -v q ); then
        ok "后端编译完成"
      else
        err "后端编译失败，请查看上方输出"
      fi
    else
      ok "后端已编译（加 --rebuild 可强制重建）"
    fi
  fi

  # --- 前端 ---
  if want frontend; then
    if [ ! -d "$FRONTEND_DIR/node_modules" ]; then
      info "安装前端依赖（npm install）…"
      ( cd "$FRONTEND_DIR" && npm_run install ) && ok "前端依赖安装完成" || err "前端依赖安装失败"
    else
      ok "前端依赖已就绪"
    fi
  fi

  # --- AIWorker ---
  if want worker; then
    if "$PYTHON_BIN" -c "import fastapi, uvicorn, httpx, pydantic" >/dev/null 2>&1; then
      ok "AIWorker 依赖已就绪"
    else
      info "安装 AIWorker 依赖（pip install）…"
      "$PYTHON_BIN" -m pip install -q -r "$WORKER_DIR/requirements.txt" -i "$PIP_MIRROR" \
        && ok "AIWorker 依赖安装完成" || err "AIWorker 依赖安装失败"
    fi
  fi
else
  info "已跳过构建与依赖安装（--no-build）"
fi

# =============================================================================
step "4/5 启动服务"
# =============================================================================

# --- AIWorker（FastAPI + uvicorn）---
if want worker; then
  if port_listening "$WORKER_PORT"; then
    warn "端口 $WORKER_PORT 已被占用，跳过 AIWorker 启动"
  elif [ -z "$PYTHON_BIN" ]; then
    err "缺少 Python，无法启动 AIWorker"
  else
    info "启动 AIWorker …"
    # 通过 bash 读取 .env.worker（LLM 配置），再 exec uvicorn。
    # 逻辑必须写进脚本文件而不是 -c 字符串：spawn 会把命令拼进 Windows 批处理，
    # 内联字符串里的 && 等字符会被 cmd 当作命令连接符吞掉。
    cat >"$RUN_DIR/worker-run.sh" <<EOF
#!/usr/bin/env bash
cd '$WORKER_DIR' || exit 1
set -a
if [ -f .env.worker ]; then . ./.env.worker; fi
set +a
exec "\$1" -m uvicorn app:app --host 127.0.0.1 --port $WORKER_PORT
EOF
    spawn worker "$WORKER_DIR" "$BASH_BIN" "$RUN_DIR/worker-run.sh" "$PYTHON_BIN"
    # 存活探针用 /openapi.json（/api/ping 会真实调用 LLM，不适合做探针）
    wait_for 40 "AIWorker（${WORKER_PORT}）" http_ok "http://127.0.0.1:$WORKER_PORT/openapi.json" \
      || warn "查看日志：tail -50 .run/logs/worker.log"
  fi
fi

# --- 后端 API（.NET 8）---
if want backend; then
  if port_listening "$BACKEND_PORT"; then
    warn "端口 $BACKEND_PORT 已被占用，跳过后端启动"
  elif [ ! -f "$BACKEND_DLL" ]; then
    err "后端产物不存在：$BACKEND_DLL（先执行构建，或去掉 --no-build）"
  else
    info "启动后端 API …"
    RUN_ENV=("ASPNETCORE_ENVIRONMENT=Development" "ASPNETCORE_URLS=http://127.0.0.1:$BACKEND_PORT")
    [ "$BACKEND_IN_CLEAN_ENV" = "1" ] && RUN_PREFIX=("${CLEAN_PREFIX[@]}")
    spawn backend "$BACKEND_DIR" "$DOTNET_BIN" "$BACKEND_DLL_ARG"
    if ! wait_for 60 "后端 API（${BACKEND_PORT}）" http_ok "http://127.0.0.1:$BACKEND_PORT/health"; then
      # 兜底：自动换一种环境模式重试一次
      if [ "$BACKEND_IN_CLEAN_ENV" = "1" ]; then
        warn "改用「继承当前环境变量」重试 …"; RUN_PREFIX=()
      else
        warn "改用「干净环境变量」重试 …"; [ "${#CLEAN_PREFIX[@]}" -eq 0 ] && build_clean_prefix; RUN_PREFIX=("${CLEAN_PREFIX[@]}")
      fi
      RUN_ENV=("ASPNETCORE_ENVIRONMENT=Development" "ASPNETCORE_URLS=http://127.0.0.1:$BACKEND_PORT")
      # --append：保留首次尝试的日志（首次报错就是诊断线索，截断等于销毁证据）
      # ⚠ --append 是 spawn 的首个参数（签名 spawn [--append] <名称> ...）。
      #   曾写成 `spawn backend --append …`：--append 被当成 workdir、工作目录顶替了可执行文件位，
      #   生成的批处理把目录当程序执行（"is not recognized"），且 append 解析失败把首次日志截断销毁。
      spawn --append backend "$BACKEND_DIR" "$DOTNET_BIN" "$BACKEND_DLL_ARG"
      wait_for 60 "后端 API（${BACKEND_PORT}，重试）" http_ok "http://127.0.0.1:$BACKEND_PORT/health" \
        || warn "查看日志：tail -50 .run/logs/backend.log"
    fi
  fi
fi

# --- 前端（Vite）---
if want frontend; then
  if port_listening "$FRONTEND_PORT"; then
    warn "端口 $FRONTEND_PORT 已被占用，跳过前端启动"
  elif [ -z "$NODE_BIN" ]; then
    err "缺少 Node.js，无法启动前端"
  else
    info "启动前端 Vite …"
    if [ -f "$FRONTEND_DIR/node_modules/vite/bin/vite.js" ]; then
      spawn frontend "$FRONTEND_DIR" "$NODE_BIN" "node_modules/vite/bin/vite.js" \
        --host 127.0.0.1 --port "$FRONTEND_PORT"
    else
      RUN_ENV=(); spawn frontend "$FRONTEND_DIR" "$NODE_BIN" "$NPM_CLI_JS" run dev -- --host 127.0.0.1
    fi
    wait_for 90 "前端 Web（${FRONTEND_PORT}）" http_ok "http://127.0.0.1:$FRONTEND_PORT/" \
      || warn "查看日志：tail -50 .run/logs/frontend.log"
  fi
fi

# =============================================================================
step "5/5 启动完成"
# =============================================================================
printf '  %s前端页面%s   http://localhost:%s      %s(账号 admin / Admin@123456)%s\n' "$C_B" "$C_0" "$FRONTEND_PORT" "$C_D" "$C_0"
printf '  %s后端 API%s   http://localhost:%s\n' "$C_B" "$C_0" "$BACKEND_PORT"
printf '  %sSwagger%s    http://localhost:%s/swagger\n' "$C_B" "$C_0" "$BACKEND_PORT"
printf '  %s健康检查%s   http://localhost:%s/health\n' "$C_B" "$C_0" "$BACKEND_PORT"
printf '  %sAIWorker%s   http://127.0.0.1:%s\n' "$C_B" "$C_0" "$WORKER_PORT"
printf '\n  %s日志%s  %s\n' "$C_D" "$C_0" "$LOG_DIR/{backend,frontend,worker}.log"
printf '  %s停止%s  bash scripts/stop-all.sh      %s状态%s  bash scripts/status.sh\n\n' "$C_D" "$C_0" "$C_D" "$C_0"
