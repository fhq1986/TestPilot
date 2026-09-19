# 后端镜像（迭代 D：容器化交付）
#
# 运行阶段不再使用 mcr 的 playwright/dotnet 整体镜像（~2GB，其中 772MB 单层
# 从国内服务器实测 <200KB/s，多次构建均因 Azure blob 连接重置/超时失败——
# 部署时反复踩坑）。改为：
#   ubuntu:24.04（腾讯云内网 mirror 拉取秒级）
#   + apt 装 .NET8 运行时与 Chromium 系统依赖（全走内网镜像源）
#   + chromium 二进制直接 COPY 自执行机缓存的 ms-browsers/（revision 1223，
#     与 Microsoft.Playwright 1.60.0 期望一致；PLAYWRIGHT_BROWSERS_PATH 对齐，
#     .NET 驱动可寻址）
# 注意：不要在构建期切换用户。数据目录在构建时按当前用户创建，
# 运行时挂命名卷会继承同样的属主，权限天然一致；手工切用户反而容易在挂卷后写不进去。

# ---------- 构建阶段 ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 先只拷 csproj 再 restore：源码变动不会让 NuGet 层失效，重建快很多
COPY AI.TestPlatform.sln ./
COPY src/AI.TestPlatform.Domain/AI.TestPlatform.Domain.csproj src/AI.TestPlatform.Domain/
COPY src/AI.TestPlatform.Application/AI.TestPlatform.Application.csproj src/AI.TestPlatform.Application/
COPY src/AI.TestPlatform.Infrastructure/AI.TestPlatform.Infrastructure.csproj src/AI.TestPlatform.Infrastructure/
COPY src/AI.TestPlatform.Api/AI.TestPlatform.Api.csproj src/AI.TestPlatform.Api/
# 只还原 API 项目：sln 还含 tests 项目，而 tests 的 csproj 在后面的 COPY . . 才进上下文，
# sln 级 restore 会因找不到它们而 MSB3202 失败（远程首次构建踩过）。
# API 项目沿 ProjectReference 链把 Domain/Application/Infrastructure 一并还原
RUN dotnet restore src/AI.TestPlatform.Api/AI.TestPlatform.Api.csproj

COPY . .
RUN dotnet publish src/AI.TestPlatform.Api/AI.TestPlatform.Api.csproj \
    -c Release -o /app/publish --no-restore

# ---------- 运行阶段 ----------
FROM ubuntu:24.04 AS runtime
WORKDIR /app

RUN mkdir -p /app/screenshots /app/traces /app/recordings

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_ROOT=/usr/lib/dotnet \
    TZ=Asia/Shanghai \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    Recorder__Enabled=false \
    Screenshots__Path=/app/screenshots \
    Execution__TracePath=/app/traces \
    Recorder__Path=/app/recordings

# apt 源切腾讯云内网（mirrors.cloud.tencent.com 走内网秒级）。
# aspnetcore-runtime-8.0：运行 API；Chromium 系统依赖来自 Ubuntu 官方仓库（等价于
# playwright install-deps 的清单）；中文字体保证页面截图不乱码。
RUN sed -i 's|http://archive.ubuntu.com|http://mirrors.cloud.tencent.com|g; s|http://security.ubuntu.com|http://mirrors.cloud.tencent.com|g; s|http://us.archive.ubuntu.com|http://mirrors.cloud.tencent.com|g' \
      /etc/apt/sources.list.d/ubuntu.sources /etc/apt/sources.list 2>/dev/null; \
    apt-get update && \
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      aspnetcore-runtime-8.0 ca-certificates curl tzdata ffmpeg \
      libasound2t64 libatk-bridge2.0-0 libatk1.0-0 libatspi2.0-0 libcairo2 \
      libcups2 libdbus-1-3 libdrm2 libgbm1 libglib2.0-0t64 libnspr4 libnss3 \
      libpango-1.0-0 libwayland-client0 libx11-6 libxcb1 libxcomposite1 \
      libxdamage1 libxext6 libxfixes3 libxkbcommon0 libxrandr2 xdg-utils \
      fonts-liberation fonts-noto-color-emoji fonts-noto-cjk \
    && rm -rf /var/lib/apt/lists/*

# Chromium / headless shell 二进制（revision 1223 = Microsoft.Playwright 1.60.0 所需）
COPY ms-browsers/ /ms-playwright/

# Playwright 录视频时硬要在 PLAYWRIGHT_BROWSERS_PATH/ffmpeg-<rev>/ffmpeg-linux 找 ffmpeg，
# 本地 ms-browsers/ 只拷了 chromium，ffmpeg 靠 apt 装完后软链到 Playwright 期望路径。
# revision 1011 = Playwright 1.60.0 绑定的 ffmpeg 版本（错误信息里已确认）
RUN mkdir -p /ms-playwright/ffmpeg-1011 && \
    ln -sf $(which ffmpeg) /ms-playwright/ffmpeg-1011/ffmpeg-linux && \
    ffmpeg -version | head -1

EXPOSE 8080

# 健康检查直接用应用自带的 /health（含数据库连通性）
HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
    CMD curl -fsS http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "AI.TestPlatform.Api.dll"]
