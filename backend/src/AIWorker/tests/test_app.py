import json
import os

os.environ.setdefault("LLM_RETRY_DELAY_SECONDS", "0")
# 服务间鉴权（app.py 对 /api/* 强制 X-Worker-Token）：测试与生产用同一个环境变量名
os.environ.setdefault("WORKER_TOKEN", "test-worker-token")

import httpx
import pytest
from fastapi.testclient import TestClient

import app as worker

AUTH_HEADERS = {"X-Worker-Token": "test-worker-token"}


class StubTransport(httpx.AsyncBaseTransport):
    """按需求文本返回预置 LLM 响应的内存 transport；可配置状态码。"""

    def __init__(self, status_code: int = 200, content: str = ""):
        self._status = status_code
        self._content = content
        self.requests: list[dict] = []

    async def handle_async_request(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(json.loads(request.content))
        return httpx.Response(self._status, content=self._content)


GOOD_CASES = [
    {
        "name": "登录成功",
        "priority": "P0",
        "type": "web",
        "steps": [
            {
                "step_order": 0,
                "action_type": "Navigate",
                "description": "打开登录页",
                "url": "https://example.com/login",
                "selector": None,
                "value": None,
            },
            {
                "step_order": 1,
                "action_type": "Fill",
                "description": "输入用户名",
                "url": None,
                "selector": {"type": "css", "value": "#username", "description": "用户名输入框"},
                "value": "admin",
            },
        ],
    },
    {
        "name": "密码错误提示",
        "priority": "P1",
        "type": "web",
        "steps": [
            {
                "step_order": 0,
                "action_type": "AssertText",
                "description": "断言错误提示",
                "url": None,
                "selector": {"type": "css", "value": ".error", "description": "错误提示"},
                "value": "密码错误",
            },
        ],
    },
]


def llm_message(content: str) -> dict:
    return {"choices": [{"message": {"content": content, "reasoning_content": "thinking..."}}]}


class SequencedTransport(httpx.AsyncBaseTransport):
    """按顺序返回预置 LLM 响应，用于验证「解析失败后重试」的行为。"""

    def __init__(self, contents: list[str]):
        self._contents = contents
        self.requests: list[dict] = []

    async def handle_async_request(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(json.loads(request.content))
        idx = min(len(self.requests) - 1, len(self._contents) - 1)
        return httpx.Response(200, content=self._contents[idx])


def make_client(llm_content: str, status_code: int = 200) -> tuple[TestClient, StubTransport]:
    transport = StubTransport(status_code, json.dumps(llm_message(llm_content), ensure_ascii=False))
    worker.app.state.llm_client = httpx.AsyncClient(
        transport=transport, base_url="https://api.deepseek.com",
    )
    return TestClient(worker.app, headers=AUTH_HEADERS), transport


def test_generate_cases_returns_mapped_cases():
    client, transport = make_client(json.dumps(GOOD_CASES, ensure_ascii=False))

    response = client.post("/api/generate-cases", json={"requirement": "用户登录功能，支持用户名密码", "min_cases": 3})

    assert response.status_code == 200
    body = response.json()
    assert len(body["cases"]) == 2
    assert body["cases"][0]["name"] == "登录成功"
    assert body["cases"][0]["steps"][1]["selector"]["value"] == "#username"
    # 请求体带上了需求与 min_cases
    sent = transport.requests[0]
    assert "用户登录功能" in sent["messages"][1]["content"]
    assert sent["max_tokens"] >= 4096


def test_worker_rejects_bad_token():
    """服务间鉴权：错误的 X-Worker-Token 必须被拒，且不触达 LLM。"""
    client, transport = make_client(json.dumps(GOOD_CASES, ensure_ascii=False))

    response = client.post(
        "/api/generate-cases",
        json={"requirement": "用户登录功能，支持用户名密码", "min_cases": 3},
        headers={"X-Worker-Token": "wrong-token"},
    )

    assert response.status_code == 401
    assert transport.requests == []


def test_worker_rejects_missing_token():
    client, _ = make_client(json.dumps(GOOD_CASES, ensure_ascii=False))

    response = client.post(
        "/api/generate-cases",
        json={"requirement": "用户登录功能，支持用户名密码", "min_cases": 3},
        headers={"X-Worker-Token": ""},
    )

    assert response.status_code == 401


def test_openapi_stays_public():
    """存活探针（start-all.sh）走 openapi.json，不受鉴权影响。"""
    client, _ = make_client(json.dumps(GOOD_CASES, ensure_ascii=False))

    response = client.get("/openapi.json")

    assert response.status_code == 200


def test_generate_cases_strips_markdown_fence():
    fenced = "```json\n" + json.dumps(GOOD_CASES, ensure_ascii=False) + "\n```"
    client, _ = make_client(fenced)

    response = client.post("/api/generate-cases", json={"requirement": "用户登录功能，支持用户名密码", "min_cases": 3})

    assert response.status_code == 200
    assert len(response.json()["cases"]) == 2


def test_generate_cases_invalid_llm_json_returns_502():
    client, _ = make_client("抱歉，我无法生成。")

    response = client.post("/api/generate-cases", json={"requirement": "用户登录功能，支持用户名密码", "min_cases": 3})

    assert response.status_code == 502
    assert "JSON" in response.json()["detail"]


def test_generate_cases_llm_error_returns_502():
    client, _ = make_client("upstream error", status_code=500)

    response = client.post("/api/generate-cases", json={"requirement": "用户登录功能，支持用户名密码", "min_cases": 3})

    assert response.status_code == 502


def test_generate_cases_validation():
    client, _ = make_client(json.dumps(GOOD_CASES, ensure_ascii=False))

    short = client.post("/api/generate-cases", json={"requirement": "太短", "min_cases": 3})
    big = client.post("/api/generate-cases", json={"requirement": "x" * 20001, "min_cases": 3})

    assert short.status_code == 422
    assert big.status_code == 422


def test_build_prompt_contains_requirement_and_rules():
    prompt = worker.build_prompt("需求：用户登录", 5)

    assert "需求：用户登录" in prompt
    assert "P0" in prompt
    assert "action_type" in prompt


def test_extract_json_plain_and_fenced_and_invalid():
    assert worker.extract_json("[{\"a\": 1}]") == [{"a": 1}]
    assert worker.extract_json("前缀\n```json\n[{\"a\": 1}]\n```\n后缀") == [{"a": 1}]
    with pytest.raises(ValueError):
        worker.extract_json("没有 JSON")


def test_normalize_cases_skips_garbage_step_order():
    raw = [
        {
            "name": "x",
            "priority": "P1",
            "type": "web",
            "steps": [
                {"step_order": None, "action_type": "Click", "selector": {"type": "css", "value": "#a"}},
                {"step_order": "abc", "action_type": "Fill", "selector": {"type": "css", "value": "#b"}, "value": "v"},
            ],
        }
    ]
    cases = worker.normalize_cases(raw)
    assert len(cases[0]["steps"]) == 2
    assert cases[0]["steps"][0]["step_order"] == 0
    assert cases[0]["steps"][1]["step_order"] == 1


def test_extract_json_repairs_truncated_output():
    truncated = '[{"name": "a", "priority": "P1", "steps": [{"step_order": 0, "action_type": "Click", "selector": {"type": "css", "value": "#a"}}]}, {"name": "b", "priority": "P2", "steps": [{"step_order": 0, "action_type": "Click", "selector": {"type": "css", "value": "#b"}}]'
    result = worker.extract_json(truncated)
    assert isinstance(result, list)
    assert result[0]["name"] == "a"


def test_extract_json_unrepairable_raises():
    with pytest.raises(ValueError):
        worker.extract_json('["a", "b"')


def test_extract_json_object_with_brackets_in_string_value():
    """对象字符串值里出现 [0] 时不能被误判成数组（曾导致定位/断言接口解析失败）。"""
    raw = '{"matched_index": 2, "confidence": 0.9, "reasoning": "清单第[0]项即用户名输入框"}'
    parsed = worker.extract_json(raw)
    assert isinstance(parsed, dict)
    assert parsed["matched_index"] == 2


def test_extract_json_object_with_surrounding_text():
    parsed = worker.extract_json('结果如下：\n{"passed": true, "confidence": 0.8}\n请参考')
    assert isinstance(parsed, dict)
    assert parsed["passed"] is True


ELEMENTS = [
    {"index": 0, "tag": "input", "id": "username", "class": "form-input", "text": "", "aria": None},
    {"index": 1, "tag": "button", "id": None, "class": "btn-primary", "text": "登 录", "aria": None},
    {"index": 2, "tag": "a", "id": None, "class": None, "text": "忘记密码", "aria": None},
]


def test_locate_element_returns_matched_index():
    client, _ = make_client(json.dumps({"matched_index": 1, "confidence": 0.95, "reasoning": "蓝色登录按钮"}))

    response = client.post("/api/locate-element", json={
        "page_url": "https://example.com/login",
        "description": "蓝色的登录按钮",
        "elements": ELEMENTS,
    })

    assert response.status_code == 200
    body = response.json()
    assert body["matched_index"] == 1
    assert body["confidence"] == 0.95


def test_locate_element_invalid_llm_output_returns_502():
    client, _ = make_client("无法确定")

    response = client.post("/api/locate-element", json={
        "page_url": "https://example.com/login",
        "description": "登录按钮",
        "elements": ELEMENTS,
    })

    assert response.status_code == 502


def test_locate_element_retries_once_when_llm_json_invalid():
    """首次返回自然语言、第二次返回合法 JSON 时应重试成功（降低模型抖动导致的 502）。"""
    transport = SequencedTransport([
        json.dumps(llm_message("我无法确定该元素"), ensure_ascii=False),
        json.dumps(llm_message(json.dumps({"matched_index": 1, "confidence": 0.88, "reasoning": "蓝色按钮"})),
                   ensure_ascii=False),
    ])
    worker.app.state.llm_client = httpx.AsyncClient(transport=transport, base_url="https://api.deepseek.com")
    client = TestClient(worker.app, headers=AUTH_HEADERS)

    response = client.post("/api/locate-element", json={
        "page_url": "https://example.com/login",
        "description": "登录按钮",
        "elements": ELEMENTS,
    })

    assert response.status_code == 200
    assert response.json()["matched_index"] == 1
    assert len(transport.requests) == 2  # 确实重试了一次


def test_locate_element_retry_hint_is_appended():
    """重试时应在 prompt 中追加「只输出 JSON」的纠正提示。"""
    transport = SequencedTransport([
        json.dumps(llm_message("无法确定"), ensure_ascii=False),
        json.dumps(llm_message(json.dumps({"matched_index": 0, "confidence": 0.9, "reasoning": "ok"})),
                   ensure_ascii=False),
    ])
    worker.app.state.llm_client = httpx.AsyncClient(transport=transport, base_url="https://api.deepseek.com")
    client = TestClient(worker.app, headers=AUTH_HEADERS)

    client.post("/api/locate-element", json={
        "page_url": "https://example.com/login",
        "description": "用户名输入框",
        "elements": ELEMENTS,
    })

    second_prompt = transport.requests[1]["messages"][-1]["content"]
    assert "只输出" in second_prompt


def test_ai_assert_returns_verdict():
    client, _ = make_client(json.dumps({"passed": True, "reason": "页面包含欢迎信息", "confidence": 0.9}))

    response = client.post("/api/ai-assert", json={
        "expectation": "登录成功后页面显示欢迎信息",
        "evidence": {"url": "https://example.com/home", "text": "欢迎回来，张三"},
    })

    assert response.status_code == 200
    body = response.json()
    assert body["passed"] is True
    assert body["confidence"] == 0.9


def test_ai_assert_string_false_counts_as_false():
    client, _ = make_client(json.dumps({"passed": "false", "reason": "no", "confidence": 0.2}))

    response = client.post("/api/ai-assert", json={
        "expectation": "页面显示欢迎信息",
        "evidence": {"text": "错误页"},
    })

    assert response.status_code == 200
    assert response.json()["passed"] is False


FLOW_CASES = [
    {
        "name": "登录后查询用户信息",
        "priority": "P0",
        "type": "api",
        "steps": [
            {"step_order": 0, "action_type": "Request", "description": "登录获取 token", "url": None,
             "selector": None, "value": None, "method": "POST", "endpoint": "/login",
             "body": '{"username":"admin","password":"123456"}'},
            {"step_order": 1, "action_type": "ExtractVariable", "description": "提取 token", "url": None,
             "selector": None, "value": "token", "method": None, "endpoint": "$.data.token", "body": None},
            {"step_order": 2, "action_type": "Request", "description": "带 token 查询", "url": None,
             "selector": None, "value": None, "method": "GET", "endpoint": "/user",
             "body": '{"headers":{"X-Token":"{token}"}}'},
        ],
    }
]


def test_import_case_steps_normalizes_and_groups():
    """Excel 导入：按 case_code 归组，非法 action_type 被过滤。"""
    client, _ = make_client(json.dumps([
        {"case_code": "TC-1", "steps": [
            {"step_order": 0, "action_type": "Navigate", "url": "/login", "selector": None, "value": None},
            {"step_order": 1, "action_type": "Bogus", "selector": None, "value": None},
            {"step_order": 2, "action_type": "AssertText",
             "selector": {"type": "css", "value": ".error", "description": "错误提示"}, "value": "请输入密码"},
        ]},
        {"case_code": "TC-2", "steps": [
            {"step_order": 0, "action_type": "Click",
             "selector": {"type": "css", "value": "button.login", "description": "登录按钮"}, "value": None},
        ]},
    ], ensure_ascii=False))

    response = client.post("/api/import-case-steps", json={
        "base_url": "http://localhost:3000",
        "cases": [
            {"case_code": "TC-1", "scenario": "空密码登录", "module": "登录认证",
             "source_steps": "输入用户名，密码留空", "expected": "提示请输入密码"},
            {"case_code": "TC-2", "scenario": "正常登录", "module": "登录认证",
             "source_steps": "输入用户名密码后点击登录", "expected": "登录成功"},
        ],
    })

    assert response.status_code == 200
    results = response.json()["results"]
    assert [r["case_code"] for r in results] == ["TC-1", "TC-2"]
    assert len(results[0]["steps"]) == 2
    assert results[0]["steps"][1]["action_type"] == "AssertText"


def test_import_case_steps_prompt_contains_case_and_base_url():
    client, transport = make_client(json.dumps(
        [{"case_code": "TC-9", "steps": [{"step_order": 0, "action_type": "Navigate", "url": "/login"}]}],
        ensure_ascii=False))

    client.post("/api/import-case-steps", json={
        "base_url": "http://localhost:3000",
        "cases": [{"case_code": "TC-9", "scenario": "忘记密码", "source_steps": "点击忘记密码",
                   "expected": "跳转重置页"}],
    })

    prompt = transport.requests[0]["messages"][-1]["content"]
    assert "TC-9" in prompt
    assert "http://localhost:3000" in prompt
    assert "忘记密码" in prompt


def test_import_case_steps_invalid_llm_output_returns_502():
    client, _ = make_client("我无法处理")

    response = client.post("/api/import-case-steps", json={
        "cases": [{"case_code": "TC-1", "scenario": "x", "source_steps": "y", "expected": "z"}],
    })

    assert response.status_code == 502


def test_analyze_api_flow_returns_cases():
    client, _ = make_client(json.dumps(FLOW_CASES))

    response = client.post("/api/analyze-api-flow", json={
        "endpoints": [
            {"method": "POST", "path": "/login", "summary": "登录", "response_codes": [200]},
            {"method": "GET", "path": "/user", "summary": "查询当前用户", "response_codes": [200]},
        ],
    })

    assert response.status_code == 200
    body = response.json()
    assert len(body["cases"]) == 1
    assert body["cases"][0]["steps"][0]["action_type"] == "Request"
    assert body["cases"][0]["steps"][1]["action_type"] == "ExtractVariable"


def test_diagnose_failure_returns_diagnosis():
    client, _ = make_client(json.dumps({
        "category": "断言失败",
        "root_cause": "期望文本不匹配",
        "confidence": 0.85,
        "suggested_fix": "检查期望文本",
        "retry_recommended": False,
    }))

    response = client.post("/api/diagnose-failure", json={
        "test_case": {"name": "登录用例", "type": "web"},
        "failed_steps": [{"step_order": 1, "action_type": "AssertText", "error": "期望文本不匹配"}],
        "similar_cases": [],
    })

    assert response.status_code == 200
    body = response.json()
    assert body["category"] == "断言失败"
    assert body["root_cause"] == "期望文本不匹配"
    assert body["confidence"] == 0.85
    assert body["suggested_fix"] == "检查期望文本"
    assert body["retry_recommended"] is False


def test_diagnose_failure_invalid_output_returns_502():
    client, _ = make_client("无法诊断")

    response = client.post("/api/diagnose-failure", json={
        "test_case": {"name": "登录用例"},
        "failed_steps": [{"step_order": 0, "action_type": "Click", "error": "元素未找到"}],
    })

    assert response.status_code == 502


def test_llm_config_override_sent_to_llm():
    client, transport = make_client(json.dumps(GOOD_CASES, ensure_ascii=False))

    response = client.post("/api/generate-cases", json={
        "requirement": "用户登录功能，支持用户名密码",
        "min_cases": 3,
        "llm_config": {"base_url": "https://api.deepseek.com", "model": "custom-model"},
    })

    assert response.status_code == 200
    assert transport.requests[-1]["model"] == "custom-model"

    response = client.post("/api/generate-cases", json={
        "requirement": "用户登录功能，支持用户名密码",
        "min_cases": 3,
    })

    assert response.status_code == 200
    assert transport.requests[-1]["model"] == worker.LLM_MODEL


def test_ping_returns_ok():
    client, _ = make_client("OK")

    response = client.post("/api/ping", json={})

    assert response.status_code == 200
    body = response.json()
    assert body["ok"] is True
    assert body["latency_ms"] > 0
    assert body["model"] == worker.LLM_MODEL


def test_ping_invalid_reply_returns_502():
    client, _ = make_client("抱歉")

    response = client.post("/api/ping", json={})

    assert response.status_code == 502


def test_build_chat_messages_without_images():
    req = worker.ChatStreamRequest(messages=[worker.ChatMessage(role="user", content="你好")])
    messages = worker.build_chat_messages(req)
    assert messages == [{"role": "user", "content": "你好"}]


def test_build_chat_messages_attaches_images_to_last_user_message():
    req = worker.ChatStreamRequest(
        messages=[
            worker.ChatMessage(role="user", content="第一条"),
            worker.ChatMessage(role="assistant", content="好的"),
            worker.ChatMessage(role="user", content="看看这个截图"),
        ],
        images=["data:image/png;base64,AAA", "BBB"],
    )
    messages = worker.build_chat_messages(req)

    # 前两条保持纯文本
    assert messages[0]["content"] == "第一条"
    assert messages[1]["content"] == "好的"
    # 最后一条 user 变成 content parts
    parts = messages[2]["content"]
    assert parts[0] == {"type": "text", "text": "看看这个截图"}
    assert parts[1]["image_url"]["url"] == "data:image/png;base64,AAA"
    assert parts[2]["image_url"]["url"] == "data:image/png;base64,BBB"   # 纯 base64 自动补 dataURL 前缀


def test_strip_images_degrades_to_text():
    messages = [{"role": "user", "content": [
        {"type": "text", "text": "描述这个截图"},
        {"type": "image_url", "image_url": {"url": "data:image/png;base64,AAA"}},
    ]}]
    assert worker.strip_images(messages) == [{"role": "user", "content": "描述这个截图"}]


def test_sse_frame_format():
    frame = worker.sse({"type": "delta", "content": "你好"})
    assert frame.startswith("data: ")
    assert frame.endswith("\n\n")
    assert json.loads(frame[6:].strip())["content"] == "你好"
