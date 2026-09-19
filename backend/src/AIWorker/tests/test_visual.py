"""视觉回归比对（纯函数）测试：覆盖噪声抑制、尺寸不一致、区域聚合与提示词/结果解析。"""
import base64
import io
import json

from PIL import Image, ImageDraw

import visual


def make_image(width: int = 200, height: int = 120, color=(255, 255, 255)) -> str:
    image = Image.new("RGB", (width, height), color)
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    return base64.b64encode(buffer.getvalue()).decode("ascii")


def with_box(width: int = 200, height: int = 120, box=(20, 20, 80, 60), color=(255, 0, 0)) -> str:
    image = Image.new("RGB", (width, height), (255, 255, 255))
    ImageDraw.Draw(image).rectangle(box, fill=color)
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    return base64.b64encode(buffer.getvalue()).decode("ascii")


def test_完全一致的图片差异为零():
    same = make_image()
    result = visual.compare(same, same, threshold=0.01)

    assert result["ok"] is True
    assert result["diffPixels"] == 0
    assert result["diffRatio"] == 0
    assert result["passed"] is True
    assert result["regions"] == []
    assert result["diffImageBase64"] is None


def test_明显变化判定为不通过并给出区域与差异图():
    baseline = make_image()
    actual = with_box()
    result = visual.compare(baseline, actual, threshold=0.01)

    assert result["passed"] is False
    assert result["diffRatio"] > 0.01
    assert result["diffPixels"] > 0
    assert result["diffImageBase64"]
    assert len(result["regions"]) >= 1
    region = result["regions"][0]
    # 变化块应覆盖被绘制的矩形（20,20)-(80,60) 附近
    assert region["x"] <= 20 and region["y"] <= 20
    assert region["x"] + region["width"] >= 80
    assert region["y"] + region["height"] >= 60


def test_微小差异在阈值内视为通过():
    baseline = make_image()
    # 只画一个 2x2 的小点，占比 4/24000 = 0.017%，低于 1% 阈值
    actual = with_box(box=(10, 10, 12, 12))
    result = visual.compare(baseline, actual, threshold=0.01)

    assert result["passed"] is True
    assert 0 < result["diffRatio"] < 0.01


def test_尺寸不一致会被标记并归一化后比对():
    baseline = make_image(200, 120)
    actual = make_image(400, 240)  # 同色不同尺寸
    result = visual.compare(baseline, actual, threshold=0.01)

    assert result["sizeMismatch"] is True
    assert result["baselineWidth"] == 200 and result["actualWidth"] == 400
    # 纯色同图缩放到基线尺寸后应完全一致
    assert result["diffRatio"] == 0


def test_像素容差可以吃掉渲染噪声():
    baseline = make_image(color=(128, 128, 128))
    actual = make_image(color=(136, 136, 136))  # 差值 8

    noisy = visual.compare(baseline, actual, threshold=0.01, pixel_tolerance=2)
    quiet = visual.compare(baseline, actual, threshold=0.01, pixel_tolerance=16)

    assert noisy["diffRatio"] > 0
    assert quiet["diffRatio"] == 0


def test_变化区域数量受上限约束():
    image = Image.new("RGB", (400, 400), (255, 255, 255))
    draw = ImageDraw.Draw(image)
    # 画 6 个互不相邻的方块
    for index in range(6):
        x = 20 + (index % 3) * 120
        y = 20 + (index // 3) * 200
        draw.rectangle((x, y, x + 40, y + 40), fill=(0, 0, 0))
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    actual = base64.b64encode(buffer.getvalue()).decode("ascii")

    result = visual.compare(make_image(400, 400), actual, threshold=0.001, max_regions=3)
    assert len(result["regions"]) == 3


def test_差异严重度分级():
    baseline = make_image()
    tiny = visual.compare(baseline, make_image(), 0.01)  # 完全一致
    assert tiny["severity"] == "low"

    big = visual.compare(baseline, with_box(box=(0, 0, 200, 120)), 0.001)
    assert big["severity"] == "high"


def test_非法图片给出可读错误():
    try:
        visual.compare("bm90LWFuLWltYWdl", make_image())
        raise AssertionError("应当抛出 ImageCompareError")
    except visual.ImageCompareError as ex:
        assert "无法解析" in str(ex) or "解码失败" in str(ex)


def test_提示词包含关键上下文():
    prompt = visual.build_describe_prompt(
        "登录用例", "点击登录按钮", 0.032,
        [{"x": 10, "y": 20, "width": 30, "height": 40, "ratio": 0.01}])

    assert "登录用例" in prompt
    assert "点击登录按钮" in prompt
    assert "3.20%" in prompt
    assert "10,20" in prompt
    assert "JSON" in prompt


def test_解析模型输出_带代码块与多余文字():
    text = '好的，结果如下：\n```json\n' + json.dumps(
        {"summary": "登录按钮由蓝色变为灰色", "risk": "HIGH", "suggestion": "确认配色是否改版"}) + "\n```\n"
    parsed = visual.parse_describe_result(text)

    assert parsed["summary"] == "登录按钮由蓝色变为灰色"
    assert parsed["risk"] == "high"
    assert parsed["suggestion"] == "确认配色是否改版"


def test_解析模型输出_非JSON时降级为纯文本():
    parsed = visual.parse_describe_result("界面好像变了")

    assert parsed["summary"] == "界面好像变了"
    assert parsed["risk"] == "unknown"


def test_解析模型输出_空文本安全():
    parsed = visual.parse_describe_result("")
    assert parsed == {"summary": "", "risk": "unknown", "suggestion": ""}


def test_忽略区域把差异从结果中剔除():
    # 基线全白；实际在 (20,20,80,60) 有一块红。该块占 60*40 / (200*120) = 10%
    baseline = make_image()
    actual = with_box()

    without_ignore = visual.compare(baseline, actual, threshold=0.01)
    assert without_ignore["diffRatio"] > 0.05
    assert without_ignore["passed"] is False

    # 忽略区域恰好覆盖红块：x=10%,y=16.7% 附近——直接给覆盖整个红块的区域
    ignored = visual.compare(
        baseline, actual, threshold=0.01,
        ignore_regions=[{"x": 5, "y": 8, "w": 50, "h": 45}])
    assert ignored["diffRatio"] == 0.0
    assert ignored["passed"] is True
    assert ignored["diffPixels"] == 0


def test_忽略区域外的差异仍然被检出():
    baseline = make_image()
    # 两块红：一块在忽略区域内，一块在外
    image = Image.new("RGB", (200, 120), (255, 255, 255))
    ImageDraw.Draw(image).rectangle((20, 20, 80, 60), fill=(255, 0, 0))    # 忽略内
    ImageDraw.Draw(image).rectangle((120, 60, 180, 100), fill=(255, 0, 0))  # 忽略外
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    actual = base64.b64encode(buffer.getvalue()).decode("ascii")

    result = visual.compare(baseline, actual, threshold=0.01,
                            ignore_regions=[{"x": 5, "y": 8, "w": 50, "h": 45}])
    # 剩下的差异是第二块：61*41 / 24000 ≈ 10%
    assert result["diffRatio"] > 0.05
    assert result["passed"] is False
    # 变化区域只应包含忽略外的那块（regions 按 32px 网格对齐，容忍一个网格的边界）
    assert all(r["x"] >= 90 for r in result["regions"])


def test_非法忽略区域被跳过而不是报错():
    baseline = make_image()
    actual = with_box()
    # 宽高为 0 / 非数值 → 跳过；越界大区域 → 裁剪到全图（合法：忽略一切）
    result = visual.compare(baseline, actual, threshold=0.01, ignore_regions=[
        {"x": 5, "y": 5, "w": 0, "h": 40},
        {"x": "abc", "y": 5, "w": 40, "h": 40},
    ])
    assert result["ok"] is True and result["diffRatio"] > 0.05
    clipped = visual.compare(baseline, actual, threshold=0.01, ignore_regions=[
        {"x": -50, "y": -50, "w": 500, "h": 500}])
    assert clipped["ok"] is True and clipped["diffRatio"] == 0.0


def test_忽略区域坐标是百分比而非像素():
    # 同样的忽略配置，在 400x240 的图上应覆盖双倍像素区域——按比例缩放
    baseline = make_image(width=400, height=240)
    image = Image.new("RGB", (400, 240), (255, 255, 255))
    ImageDraw.Draw(image).rectangle((40, 40, 160, 120), fill=(255, 0, 0))
    buffer = io.BytesIO()
    image.save(buffer, format="PNG")
    actual = base64.b64encode(buffer.getvalue()).decode("ascii")

    # 忽略前 10% 差异；忽略区域(5,10,45,40)% 恰好覆盖该红块
    result = visual.compare(baseline, actual, threshold=0.01,
                            ignore_regions=[{"x": 5, "y": 8, "w": 50, "h": 45}])
    assert result["diffRatio"] == 0.0
