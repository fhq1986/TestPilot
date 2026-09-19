"""临时验证用：接收通知 Webhook 的回显服务（打印请求体，返回企微/钉钉/飞书风格的成功响应）

用法：python scripts/echo-webhook.py [port]
"""
import json
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 9099
LOG = "D:/temp/AITest/.run/tmp/echo-webhook.log"


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):  # noqa: N802
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length).decode("utf-8", "replace")
        with open(LOG, "a", encoding="utf-8") as fh:
            fh.write(f"PATH={self.path}\nBODY={body}\n---\n")
        payload = json.dumps({"errcode": 0, "errmsg": "ok", "StatusCode": 0, "code": 0}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def log_message(self, fmt, *args):
        pass


if __name__ == "__main__":
    with open(LOG, "w", encoding="utf-8") as fh:
        fh.write("")
    print(f"echo webhook listening on {PORT}", flush=True)
    HTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
