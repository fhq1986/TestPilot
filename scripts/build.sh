#!/usr/bin/env bash
# 后端构建 / 测试快捷脚本
#
# 背景：WorkBuddy 的 bash 环境缺失 Windows 关键环境变量（APPDATA/LOCALAPPDATA/PROGRAMFILES/...），
# 直接跑 dotnet 会在 NuGet 初始化时报 `Value cannot be null (Parameter 'path1')`；
# ASP.NET 进程在污染环境下还会静默退出。因此这里用「干净环境变量」方式调用 dotnet：
#   - 清掉 PATH（env -u PATH），只给 Path / PATHEXT，避免 MSYS 路径被 dotnet 误解析
#   - 补齐 Windows 必备变量（SystemRoot / APPDATA / PROGRAMFILES / PROGRAMDATA ...）
#
# 用法：
#   scripts/build.sh            构建解决方案
#   scripts/build.sh test       构建并跑全部测试
#   scripts/build.sh unit       只跑单元测试
#   scripts/build.sh migration <名称>   新增 EF 迁移（自动先停服务避免 DLL 被锁）
#   scripts/build.sh db [迁移名]        应用迁移到最新；传迁移名则回滚/前进到那一步
#   scripts/build.sh db-prev            回滚到上一个迁移（验证数据搬迁顺序用）
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BACKEND="$ROOT/backend"
SOLUTION="AI.TestPlatform.sln"

# dotnet/MSBuild 只认 Windows 路径，必须切到 Windows 可识别的目录再用相对路径
cd "$BACKEND" || exit 1

# ------------------------------- 定位 dotnet ---------------------------------
DOTNET_BIN=""
for p in "/c/Program Files/dotnet/dotnet.exe" "$HOME/.dotnet/dotnet.exe"; do
  [ -x "$p" ] && DOTNET_BIN="$p" && break
done
if [ -z "$DOTNET_BIN" ]; then
  echo "找不到 dotnet.exe，请确认已安装 .NET 8 SDK" >&2
  exit 1
fi
DOTNET_WIN_DIR="$(cygpath -w "$(dirname "$DOTNET_BIN")" 2>/dev/null || echo 'C:\Program Files\dotnet')"

WIN_HOME="$(cygpath -w "$HOME" 2>/dev/null || echo 'C:\Users\44894')"
CLEAN_PATH="C:\\Windows\\system32;C:\\Windows;$DOTNET_WIN_DIR"

clean_prefix() {
  env -u PATH \
    "Path=$CLEAN_PATH" \
    "PATHEXT=.COM;.EXE;.BAT;.CMD;.VBS;.JS;.WS" \
    "OS=Windows_NT" \
    "SystemRoot=C:\\Windows" \
    "windir=C:\\Windows" \
    "SystemDrive=C:" \
    "TEMP=$WIN_HOME\\AppData\\Local\\Temp" \
    "TMP=$WIN_HOME\\AppData\\Local\\Temp" \
    "APPDATA=$WIN_HOME\\AppData\\Roaming" \
    "LOCALAPPDATA=$WIN_HOME\\AppData\\Local" \
    "USERPROFILE=$WIN_HOME" \
    "PROGRAMFILES=C:\\Program Files" \
    "ProgramFiles(x86)=C:\\Program Files (x86)" \
    "PROGRAMDATA=C:\\ProgramData" \
    "COMPUTERNAME=${COMPUTERNAME:-DESKTOP}" \
    "DOTNET_CLI_TELEMETRY_OPTOUT=1" \
    "DOTNET_NOLOGO=1" \
    "$@"
}

run_dotnet() { clean_prefix "$DOTNET_BIN" "$@"; }

# 运行中的后端会锁定 bin 下的 DLL，构建前先停服务
# 注意：不能依赖可执行位（-x）判断脚本是否存在——Windows/MSYS 下脚本常常没有 +x
ensure_stopped() {
  [ -f "$ROOT/scripts/status.sh" ] || return 0
  [ -f "$ROOT/scripts/stop-all.sh" ] || return 0

  # 先暂停体检守护：它在后端掉线后 120s 内会把它拉回来，
  # 而构建正需要后端停着 —— 两者天然对立，不暂停就会锁住 bin 下的 DLL。
  # 退出时只清理**自己创建**的那份暂停标记，免得把用户手动暂停的给解了。
  local pause_file="$ROOT/.run/watchdog.pause"
  if [ ! -f "$pause_file" ]; then
    mkdir -p "$ROOT/.run"
    touch "$pause_file"
    trap 'rm -f "$ROOT/.run/watchdog.pause"' EXIT
  fi

  # ⚠ 这里**不能**写成 `bash status.sh | grep -q ...`：
  # 本脚本开了 `set -o pipefail`，而 `grep -q` 一命中就退出，
  # 上游 status.sh 还没写完就收到 SIGPIPE、以 141 退出 —— 管道整体算失败，
  # 于是 if 永远为假，这个守卫**静默失效**（后端没被停 → 构建报 DLL 被锁，查了很久）。
  # 先把输出收进变量、再匹配，就没有上游进程会被 SIGPIPE 打断。
  local st
  st="$(bash "$ROOT/scripts/status.sh" 2>/dev/null || true)"
  if printf '%s\n' "$st" | grep -q "backend.*运行中"; then
    echo "==> 检测到后端在运行，先停止以释放 DLL 占用"
    bash "$ROOT/scripts/stop-all.sh" >/dev/null 2>&1 || true
    sleep 2
  fi
}

ACTION="${1:-build}"

case "$ACTION" in
  build)
    ensure_stopped
    run_dotnet build "$SOLUTION" -c Debug
    ;;
  test)
    ensure_stopped
    run_dotnet test "$SOLUTION" -c Debug
    ;;
  unit)
    ensure_stopped
    run_dotnet test "tests/AI.TestPlatform.UnitTests" -c Debug
    ;;
  itest)
    # 集成测试。可选第二参为 xUnit 过滤器，如：scripts/build.sh itest OpenApiStructureTests
    #
    # 取库方式（见 tests/AI.TestPlatform.IntegrationTests/TestApiFactory.cs）：
    #   · 默认「外部库」：连本机既有 PostgreSQL（WSL 内容器，5432）的专用测试库，**不启容器**。
    #     原因：本机 docker 29 + Testcontainers 3.9.0 的"容器就绪等待"永不返回——Docker API 的
    #     create/start 均成功、容器内 PG 也已 ready，但客户端等待不结束（抓包停在 POST start 之后）；
    #     离线源又无更新的 Testcontainers 版本，故改走外部库，保证集成测试能真跑。
    #   · 设 ITEST_USE_TESTCONTAINERS=1 可强制回退到 Testcontainers 自起容器（CI / 兼容环境用）。
    # 外部库连接串可用 TEST_PG_CONNECTION 覆盖；默认 TEST_PG_RECREATE=1 → 每次用全新库（更可复现）。
    ensure_stopped
    if [ "${ITEST_USE_TESTCONTAINERS:-0}" = "1" ]; then
      echo "==> 使用 Testcontainers 自起 PG 容器"
      unset TEST_PG_CONNECTION TEST_PG_RECREATE
    else
      export TEST_PG_CONNECTION="${TEST_PG_CONNECTION:-Host=127.0.0.1;Port=5432;Database=ai_test_integration;Username=postgres;Password=postgres}"
      export TEST_PG_RECREATE="${TEST_PG_RECREATE:-1}"
      echo "==> 使用外部 PostgreSQL：${TEST_PG_CONNECTION}（TEST_PG_RECREATE=${TEST_PG_RECREATE}）"
    fi
    if [ -n "${2:-}" ]; then
      run_dotnet test "tests/AI.TestPlatform.IntegrationTests" -c Debug --filter "$2"
    else
      run_dotnet test "tests/AI.TestPlatform.IntegrationTests" -c Debug
    fi
    ;;
  openapi)
    # OpenAPI 结构校验（要求后端在 5210 跑、Development 环境）。
    # 注：集成测试路径（OpenApiStructureTests）现已可通过 `scripts/build.sh itest OpenApiStructureTests`
    # 真跑（改走外部库）；本脚本仍是**不依赖测试宿主**的等效运行时校验入口，见 .run/rt/openapi-check.py。
    WIN_RT="$(cygpath -w "$ROOT/.run/rt/openapi-check.py")"
    /c/Users/44894/.workbuddy/binaries/python/versions/3.13.12/python.exe "$WIN_RT"
    ;;
  migration)
    NAME="${2:-}"
    if [ -z "$NAME" ]; then
      echo "用法：scripts/build.sh migration <迁移名称>" >&2
      exit 1
    fi
    ensure_stopped
    run_dotnet ef migrations add "$NAME" \
      --project "src/AI.TestPlatform.Infrastructure" \
      --startup-project "src/AI.TestPlatform.Api" \
      --output-dir Migrations
    ;;
  db-prev)
    # 回滚到上一个迁移。迁移名靠"按修改时间取倒数第二个"自动解析，
    # 免得手工数迁移名数错（Designer / Snapshot 要排除）。
    PREV="$(ls -t "$ROOT/backend/src/AI.TestPlatform.Infrastructure/Migrations/"*.cs 2>/dev/null \
      | grep -v Designer | grep -v Snapshot \
      | sed -n '2p' | xargs -r -n1 basename | sed 's/\.cs$//')"
    if [ -z "$PREV" ]; then
      echo "找不到上一个迁移" >&2
      exit 1
    fi
    echo "==> 回滚目标：$PREV"
    ensure_stopped
    run_dotnet ef database update "$PREV" \
      --project "src/AI.TestPlatform.Infrastructure" \
      --startup-project "src/AI.TestPlatform.Api"
    ;;
  db)
    # 应用 / 回滚迁移。不传目标 → 应用到最新；传迁移名 → 回滚（或前进）到那一步。
    # 做数据迁移时必须能这样来回验证：回滚到上一个迁移 → 构造旧数据 → 重新应用，
    # 才能证明 Up 里的搬迁顺序是对的（这类坑记在项目备忘里）。
    TARGET="${2:-}"
    ensure_stopped
    if [ -n "$TARGET" ]; then
      run_dotnet ef database update "$TARGET" \
        --project "src/AI.TestPlatform.Infrastructure" \
        --startup-project "src/AI.TestPlatform.Api"
    else
      run_dotnet ef database update \
        --project "src/AI.TestPlatform.Infrastructure" \
        --startup-project "src/AI.TestPlatform.Api"
    fi
    ;;
  *)
    echo "未知动作：$ACTION（可用：build / test / unit / migration <名称>）" >&2
    exit 1
    ;;
esac
