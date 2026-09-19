"""视觉回归的图像比对：纯函数实现，供 app.py 的 /api/visual/* 端点调用。

比对口径（与前端/后端约定一致）：
1. 实际图与基线尺寸不一致时，先把实际图缩放到基线尺寸，并在结果里标记 sizeMismatch
   （尺寸变化本身通常意味着视口/滚动位置变了，需要人工确认，所以不能直接判失败）；
2. 先做轻微高斯模糊再逐像素求差，并设置像素容差，抑制字体抗锯齿/亚像素渲染造成的噪声；
3. diffRatio = 变化像素数 / 总像素数；阈值判定由调用方（后端按用例配置的 VisualThreshold）决定；
4. 输出差异图（原图叠加红色高亮 + 变化区域矩形），便于直接贴到报告里。
"""
from __future__ import annotations

import base64
import io
from typing import Any

from PIL import Image, ImageChops, ImageDraw, ImageFilter

# 逐像素差值小于该值视为渲染噪声（0-255）
DEFAULT_PIXEL_TOLERANCE = 16
# 判定「变化区块」时，块内变化像素占比下限
REGION_BLOCK = 32
REGION_MIN_RATIO = 0.02
# 差异图里高亮颜色的透明度
HIGHLIGHT_ALPHA = 150


class ImageCompareError(Exception):
    """图片无法解码等致命问题。"""


def _load(image_base64: str, label: str) -> Image.Image:
    if not image_base64:
        raise ImageCompareError(f"{label} 图片为空")
    payload = image_base64.split(",", 1)[1] if image_base64.startswith("data:") else image_base64
    try:
        raw = base64.b64decode(payload)
    except Exception as exc:  # noqa: BLE001 - 解码失败统一提示
        raise ImageCompareError(f"{label} 图片 base64 解码失败: {exc}") from exc
    try:
        image = Image.open(io.BytesIO(raw))
        image.load()
        return image.convert("RGB")
    except Exception as exc:  # noqa: BLE001 - 非图片内容/损坏
        raise ImageCompareError(f"{label} 图片无法解析: {exc}") from exc


def _encode(image: Image.Image) -> str:
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return base64.b64encode(buffer.getvalue()).decode("ascii")


def _diff_mask(baseline: Image.Image, actual: Image.Image, tolerance: int) -> Image.Image:
    """返回 L 模式掩码：变化像素为 255，未变化为 0。"""
    left = baseline.filter(ImageFilter.GaussianBlur(1)).convert("L")
    right = actual.filter(ImageFilter.GaussianBlur(1)).convert("L")
    return ImageChops.difference(left, right).point(lambda p: 255 if p > tolerance else 0)


def _touching(a: tuple[int, int, int, int], b: tuple[int, int, int, int], gap: int = 4) -> bool:
    return not (
        a[2] + gap < b[0] or b[2] + gap < a[0] or a[3] + gap < b[1] or b[3] + gap < a[1]
    )


def _union(a: tuple[int, int, int, int], b: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    return (min(a[0], b[0]), min(a[1], b[1]), max(a[2], b[2]), max(a[3], b[3]))


def _changed_regions(mask: Image.Image, max_regions: int) -> list[dict[str, Any]]:
    """把变化像素聚成不超过 max_regions 个矩形（分块 → 相邻块合并）。"""
    width, height = mask.size
    cols = max(1, width // REGION_BLOCK)
    rows = max(1, height // REGION_BLOCK)

    blocks: list[tuple[tuple[int, int, int, int], int]] = []
    for row in range(rows):
        for col in range(cols):
            box = (col * REGION_BLOCK, row * REGION_BLOCK,
                   min(width, (col + 1) * REGION_BLOCK), min(height, (row + 1) * REGION_BLOCK))
            block = mask.crop(box)
            changed = block.histogram()[255]
            area = max(1, block.size[0] * block.size[1])
            if changed / area >= REGION_MIN_RATIO:
                blocks.append((box, changed))

    if not blocks:
        return []

    merged: list[tuple[int, int, int, int]] = []
    merged_changed: list[int] = []
    for box, changed in blocks:
        for index, existing in enumerate(merged):
            if _touching(existing, box):
                merged[index] = _union(existing, box)
                merged_changed[index] += changed
                break
        else:
            merged.append(box)
            merged_changed.append(changed)

    # 合并后可能又相邻了，再扫一轮收敛
    changed_again = True
    while changed_again:
        changed_again = False
        for i in range(len(merged)):
            for j in range(i + 1, len(merged)):
                if _touching(merged[i], merged[j]):
                    merged_changed[i] += merged_changed[j]
                    merged[i] = _union(merged[i], merged[j])
                    del merged[j]
                    del merged_changed[j]
                    changed_again = True
                    break
            if changed_again:
                break

    order = sorted(range(len(merged)), key=lambda i: -merged_changed[i])[:max_regions]
    total_pixels = width * height
    return [
        {
            "x": merged[i][0], "y": merged[i][1],
            "width": merged[i][2] - merged[i][0], "height": merged[i][3] - merged[i][1],
            "changedPixels": merged_changed[i],
            "ratio": round(merged_changed[i] / total_pixels, 6),
        }
        for i in order
    ]


def _render_diff(actual: Image.Image, mask: Image.Image,
                 regions: list[dict[str, Any]]) -> Image.Image:
    """差异图：原图 + 红色高亮变化像素 + 变化区域边框。"""
    base = actual.convert("RGBA")
    highlight = Image.new("RGBA", base.size, (255, 0, 0, HIGHLIGHT_ALPHA))
    composed = Image.alpha_composite(base, Image.composite(highlight, Image.new("RGBA", base.size, (0, 0, 0, 0)), mask))
    draw = ImageDraw.Draw(composed)
    for region in regions:
        box = (region["x"] - 2, region["y"] - 2,
               region["x"] + region["width"] + 2, region["y"] + region["height"] + 2)
        draw.rectangle(box, outline=(255, 64, 64, 255), width=2)
    return composed.convert("RGB")


def _severity(diff_ratio: float, threshold: float) -> str:
    """差异严重度：用于报告与 AI 说明的直观分级。"""
    if threshold <= 0:
        threshold = 0.001
    multiple = diff_ratio / threshold
    if multiple >= 10:
        return "high"
    if multiple >= 3:
        return "medium"
    return "low"



def _blank_ignored(mask: Image.Image, size: tuple[int, int],
                   regions: list[dict[str, Any]]) -> None:
    """把忽略区域对应的掩码像素清零（差异统计与高亮一并忽略）。

    坐标是百分比（0~100）——视口尺寸变化时按比例缩放，比像素坐标更抗截图尺寸漂移。
    越界值按边界裁剪；非法区域（宽高 <= 0）直接跳过而不是报错：
    屏蔽失败只是"多算一点差异"，不值得让整次比对失败。
    """
    draw = ImageDraw.Draw(mask)
    width, height = size
    for region in regions or []:
        try:
            x = max(0.0, min(100.0, float(region.get("x", 0)))) / 100.0
            y = max(0.0, min(100.0, float(region.get("y", 0)))) / 100.0
            w = max(0.0, min(100.0, float(region.get("w", 0)))) / 100.0
            h = max(0.0, min(100.0, float(region.get("h", 0)))) / 100.0
        except (TypeError, ValueError):
            continue
        if w <= 0 or h <= 0:
            continue
        box = (int(x * width), int(y * height),
               min(width, int((x + w) * width)), min(height, int((y + h) * height)))
        if box[2] > box[0] and box[3] > box[1]:
            draw.rectangle(box, fill=0)


def compare(
    baseline_base64: str,
    actual_base64: str,
    threshold: float = 0.01,
    pixel_tolerance: int = DEFAULT_PIXEL_TOLERANCE,
    max_regions: int = 5,
    ignore_regions: list[dict[str, Any]] | None = None,
) -> dict[str, Any]:
    """比对两张截图，返回差异比例、变化区域与差异图（base64）。

    ignore_regions：百分比忽略区域（0~100，相对基线图尺寸）——
    时间戳、广告位、头像这类每次渲染必然不同的区域，比对时直接屏蔽。
    只屏蔽差异掩码（不改图像本身），差异图里也看不见这些区域的变化。
    """
    baseline = _load(baseline_base64, "基线")
    actual = _load(actual_base64, "实际")
    baseline_size = baseline.size
    actual_size = actual.size
    size_mismatch = baseline_size != actual_size
    if size_mismatch:
        actual = actual.resize(baseline_size, Image.LANCZOS)

    tolerance = max(0, min(255, int(pixel_tolerance)))
    mask = _diff_mask(baseline, actual, tolerance)
    if ignore_regions:
        _blank_ignored(mask, baseline_size, ignore_regions)
    total_pixels = baseline_size[0] * baseline_size[1]
    # 掩码为 L 模式，值 255 即变化像素，histogram 的 255 号桶就是变化像素个数
    diff_pixels = int(mask.histogram()[255])
    diff_ratio = round(diff_pixels / total_pixels, 6) if total_pixels else 0.0

    regions = _changed_regions(mask, max(1, max_regions))
    diff_image = _encode(_render_diff(actual, mask, regions)) if diff_pixels > 0 else None

    return {
        "ok": True,
        "baselineWidth": baseline_size[0],
        "baselineHeight": baseline_size[1],
        "actualWidth": actual_size[0],
        "actualHeight": actual_size[1],
        "sizeMismatch": size_mismatch,
        "totalPixels": total_pixels,
        "diffPixels": diff_pixels,
        "diffRatio": diff_ratio,
        "threshold": threshold,
        "passed": diff_ratio <= threshold,
        "severity": _severity(diff_ratio, threshold),
        "regions": regions,
        "diffImageBase64": diff_image,
    }


def build_describe_prompt(case_name: str, step_description: str, diff_ratio: float,
                          regions: list[dict[str, Any]]) -> str:
    """构造「语义化差异说明」的提示词（配合三张图：基线 / 实际 / 差异图）。"""
    region_text = "、".join(
        f"({r['x']},{r['y']}) {r['width']}×{r['height']}"
        for r in (regions or [])[:5]
    ) or "未定位到明显区块"
    return f"""你是资深前端测试工程师。下面三张图依次是：**基线截图**、**本次实际截图**、**差异图（红色为差异像素）**。

用例：{case_name}
步骤：{step_description or '（未填写步骤描述）'}
像素差异比例：{diff_ratio:.2%}
变化区域（坐标 x,y 与宽×高）：{region_text}

请用简体中文回答，只输出 JSON（不要 Markdown 代码块），格式：
{{"summary": "一句话说明这次 UI 变化是什么（例如：登录按钮由蓝色变为灰色、下方多出错误提示行、整体布局下移约 20px）", "risk": "high|medium|low", "suggestion": "该变化是否可能是缺陷，建议如何确认"}}

要求：
- summary 不超过 80 字，聚焦「哪里变了、变成什么」，不要复述差异比例；
- 若差异只是渲染噪声（极小的像素抖动、抗锯齿差异），risk 给 low 并说明可忽略；
- 若差异像是样式错位、元素缺失、文案变化、布局塌陷，risk 给 high 或 medium。"""


def parse_describe_result(text: str) -> dict[str, Any]:
    """解析 LLM 输出（容错：截取首个 JSON 对象）。"""
    import json
    import re

    if not text:
        return {"summary": "", "risk": "unknown", "suggestion": ""}
    cleaned = text.strip()
    if cleaned.startswith("```"):
        cleaned = re.sub(r"^```[a-zA-Z]*\s*|\s*```$", "", cleaned)
    start, end = cleaned.find("{"), cleaned.rfind("}")
    if start == -1 or end == -1 or end <= start:
        return {"summary": cleaned[:200], "risk": "unknown", "suggestion": ""}
    try:
        data = json.loads(cleaned[start:end + 1])
    except ValueError:
        return {"summary": cleaned[:200], "risk": "unknown", "suggestion": ""}
    return {
        "summary": str(data.get("summary", ""))[:400],
        "risk": str(data.get("risk", "unknown")).lower(),
        "suggestion": str(data.get("suggestion", ""))[:400],
    }
