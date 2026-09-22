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

# ---------- k6 阶段（迭代 F·P2-9：压测执行器）----------
# 只借用官方镜像里的 k6 二进制，而不是在构建期下载 release 包：
# 镜像走已配置的国内 mirror，构建期不必访问 GitHub Releases（部署机实测极慢/超时）。
# 与 ms-browsers/ 同一套思路——能从镜像拿的就别在构建期下。
#
# 用 :latest 而不是钉版本：mirror 上按版本号 tag 经常没有（0.54.0 就拉不到），
# 钉一个拉不动的 tag 会直接把构建搞挂。代价是构建不可复现——若将来 mirror 支持按
# digest 拉，建议改成 `grafana/k6@sha256:...` 兼顾两者。
FROM docker.1ms.run/grafana/k6:latest AS k6
# 用 `command -v` 定位而不是写死 /usr/bin/k6：官方镜像换过基础发行版（alpine/debian），
# 二进制位置可能变。定死路径的话，一旦上游挪位置，构建会以「找不到文件」失败，
# 而失败信息不会告诉你「其实是路径变了」。这里先归一到一个固定位置再 COPY。
#
# 暂存到 /tmp 而不是 /：官方 k6 镜像以非 root 用户运行，写根目录会
# 「cp: can't create '/k6': Permission denied」直接把构建打挂（远程实测踩过）。
RUN cp "$(command -v k6)" /tmp/k6 && /tmp/k6 version

# ---------- 运行阶段 ----------
FROM ubuntu:24.04 AS runtime
WORKDIR /app

RUN mkdir -p /app/screenshots /app/traces /app/recordings /app/loadtests

COPY --from=build /app/publish ./

# 压测执行器：k6 二进制（见上面的 k6 阶段，已归一为 /tmp/k6）
COPY --from=k6 /tmp/k6 /usr/local/bin/k6

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_ROOT=/usr/lib/dotnet \
    TZ=Asia/Shanghai \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    Recorder__Enabled=false \
    Screenshots__Path=/app/screenshots \
    Execution__TracePath=/app/traces \
    Recorder__Path=/app/recordings \
    LoadTest__K6Path=/usr/local/bin/k6 \
    LoadTest__WorkPath=/app/loadtests

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
