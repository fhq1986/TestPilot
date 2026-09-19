#!/usr/bin/env python3
"""项目 PostgreSQL 直连查询小工具（用于开发期验证数据状态）

背景：WorkBuddy 的 bash 环境里没有 psql 可用（wsl.exe 受安全策略限制），
因此用 pg8000（纯 Python 实现，无需编译）直连 Windows 侧 5432 端口。

用法：
  python scripts/db.py "SELECT count(*) FROM \"Executions\""       # 执行查询并打印结果
  python scripts/db.py --exec "UPDATE ..."                         # 执行写操作
  python scripts/db.py --exec "..." --query "SELECT ..."           # 先写后查

连接参数可通过环境变量覆盖：DB_HOST / DB_PORT / DB_NAME / DB_USER / DB_PASSWORD
"""
import os
import sys

try:
    import pg8000.native
except ImportError:  # pragma: no cover
    print("缺少依赖 pg8000，请先安装：python -m pip install pg8000", file=sys.stderr)
    sys.exit(2)

HOST = os.environ.get("DB_HOST", "localhost")
PORT = int(os.environ.get("DB_PORT", "5432"))
NAME = os.environ.get("DB_NAME", "ai_test")
USER = os.environ.get("DB_USER", "postgres")
PASSWORD = os.environ.get("DB_PASSWORD", "postgres")


def connect():
    return pg8000.native.Connection(user=USER, password=PASSWORD, host=HOST, port=PORT, database=NAME)


def run(conn, sql, label):
    print(f"--- {label} ---")
    rows = conn.run(sql) or []
    if not rows:
        print("(no rows)")
        return
    # pg8000.native 返回每行的 list；列名通过 conn.columns 获取
    try:
        names = [c["name"] for c in conn.columns]
        print(" | ".join(names))
    except Exception:
        pass
    for row in rows:
        print(" | ".join("" if v is None else str(v) for v in row))


def main(argv):
    args = argv[1:]
    if not args:
        print(__doc__)
        return 1

    mode = "query"
    pending = []
    for arg in args:
        if arg in ("--exec", "--query"):
            mode = arg[2:]
            pending.append((mode, []))
        else:
            if not pending:
                pending.append(("query", []))
            pending[-1][1].append(arg)

    conn = connect()
    try:
        for m, statements in pending:
            for sql in statements:
                if m == "exec":
                    conn.run(sql)
                    print("--- exec ok ---")
                    print(sql.strip()[:200])
                else:
                    run(conn, sql, "query")
    finally:
        conn.close()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
