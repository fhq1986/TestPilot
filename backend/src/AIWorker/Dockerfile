# AIWorker 镜像：FastAPI + uvicorn，对接 LLM 做用例生成 / 元素定位 / 视觉语义说明
FROM python:3.12-slim AS runtime

WORKDIR /app

# 先装依赖再拷代码：依赖不变时这一层能复用
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY app.py visual.py ./

ENV WORKER_HOST=0.0.0.0 \
    WORKER_PORT=8000 \
    PYTHONUNBUFFERED=1

EXPOSE 8000

# LLM_API_KEY 等凭据必须由运行时环境注入，绝不能写进镜像
# （生产环境不配 Key 时接口会明确报错，而不是静默失败）
CMD ["sh", "-c", "uvicorn app:app --host ${WORKER_HOST} --port ${WORKER_PORT}"]
