# 前端镜像：Node 构建静态资源 → nginx 托管
FROM node:22-alpine AS build
WORKDIR /app

# 先装依赖再拷源码，利用层缓存
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# ---------- 运行阶段 ----------
FROM nginx:1.27-alpine AS runtime

# 自定义配置：SPA history 回退 + 反向代理 /api 到后端
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
