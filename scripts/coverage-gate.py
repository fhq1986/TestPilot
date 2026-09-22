#!/usr/bin/env python3
"""覆盖率门禁（迭代 E·④）。

解析 coverlet 产出的 cobertura XML，按**程序集前缀**聚合行覆盖率，低于各自下限即非零退出。
聚合口径用每个 <class> 的 <line hits> 子节点（标准 cobertura，可靠）：
  行覆盖率 = hits>0 的行数 / 总行数。

为什么是**每个程序集各自的下限**而不是一个统一值：Api 程序集里大量是端点编排/Playwright/
Worker，单测天然覆盖不到（由集成测试兜），而 Application 是纯逻辑、应当高覆盖。用一个统一阈值
要么把 Application 放水、要么把 Api 直接判死——分档才是有效信号。

用法：
  python scripts/coverage-gate.py \
      --report ".run/coverage/**/coverage.cobertura.xml" \
      --thresholds "AI.TestPlatform.Api=10,AI.TestPlatform.Application=68"
"""
import argparse
import glob
import os
import sys
import xml.etree.ElementTree as ET


def parse_thresholds(spec: str) -> list[tuple[str, float]]:
    out: list[tuple[str, float]] = []
    for item in spec.split(","):
        item = item.strip()
        if not item:
            continue
        prefix, _, value = item.partition("=")
        out.append((prefix.strip(), float(value)))
    # 前缀长的优先匹配（避免 "AI.TestPlatform.Api" 与更短前缀混淆）
    out.sort(key=lambda kv: len(kv[0]), reverse=True)
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--report", required=True, help="cobertura xml 路径或 glob")
    ap.add_argument("--thresholds", default="", help="逗号分隔的 程序集前缀=下限(百分比)")
    ap.add_argument("--min-line", type=float, default=0.0, help="未匹配到专档时的兜底下限")
    args = ap.parse_args()

    thresholds = parse_thresholds(args.thresholds)
    if not thresholds:
        print("[coverage-gate] 未提供 --thresholds", file=sys.stderr)
        return 2

    paths = glob.glob(args.report, recursive=True)
    if not paths:
        print(f"[coverage-gate] 未找到覆盖率报告：{args.report}", file=sys.stderr)
        return 2
    paths.sort(key=os.path.getmtime)
    report = paths[-1]

    root = ET.parse(report).getroot()
    total: dict[str, int] = {}
    covered: dict[str, int] = {}

    prefixes = [p for p, _ in thresholds]
    for cls in root.iter("class"):
        fq = cls.get("name", "")
        asm = next((p for p in prefixes if fq.startswith(p)), None)
        if asm is None:
            continue
        for line in cls.findall("./lines/line"):
            total[asm] = total.get(asm, 0) + 1
            if int(line.get("hits", "0")) > 0:
                covered[asm] = covered.get(asm, 0) + 1

    if not total:
        print(f"[coverage-gate] 报告中无可统计行（检查前缀）：{report}", file=sys.stderr)
        return 2

    threshold_of = dict(thresholds)
    print(f"[coverage-gate] 报告：{report}")
    failed = []
    for asm in sorted(total):
        lines = total[asm]
        cov = covered.get(asm, 0)
        rate = (cov / lines * 100) if lines else 0.0
        floor = threshold_of.get(asm, args.min_line)
        ok = rate >= floor
        print(f"  [{'OK  ' if ok else 'FAIL'}] {asm:<32} {rate:6.2f}%  (下限 {floor:.1f}%, {cov}/{lines})")
        if not ok:
            failed.append(asm)

    if failed:
        print(f"[coverage-gate] 未达标：{', '.join(failed)}（覆盖率应上升，请补测试而不是下调阈值）",
              file=sys.stderr)
        return 1
    print("[coverage-gate] 全部门禁通过")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
