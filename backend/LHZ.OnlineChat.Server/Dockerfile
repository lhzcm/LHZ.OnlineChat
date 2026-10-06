# ===== 构建阶段 =====
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先复制解决方案级配置与全部项目文件，利用层缓存（依赖未变则跳过 restore）
COPY nuget.config Directory.Build.props ./
COPY LHZ.OnlineChat.Domain/LHZ.OnlineChat.Domain.csproj LHZ.OnlineChat.Domain/
COPY LHZ.OnlineChat.Application/LHZ.OnlineChat.Application.csproj LHZ.OnlineChat.Application/
COPY LHZ.OnlineChat.Infrastructure/LHZ.OnlineChat.Infrastructure.csproj LHZ.OnlineChat.Infrastructure/
COPY LHZ.OnlineChat.Server/LHZ.OnlineChat.Server.csproj LHZ.OnlineChat.Server/
RUN dotnet restore LHZ.OnlineChat.Server/LHZ.OnlineChat.Server.csproj

# 再复制源码（四层）
COPY LHZ.OnlineChat.Domain/ LHZ.OnlineChat.Domain/
COPY LHZ.OnlineChat.Application/ LHZ.OnlineChat.Application/
COPY LHZ.OnlineChat.Infrastructure/ LHZ.OnlineChat.Infrastructure/
COPY LHZ.OnlineChat.Server/ LHZ.OnlineChat.Server/
RUN dotnet publish LHZ.OnlineChat.Server/LHZ.OnlineChat.Server.csproj -c Release -o /app/publish --no-restore

# ===== 运行阶段 =====
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

# 以非 root 运行：容器被攻破时无法直接改写镜像文件/安装后门。
# 固定 UID/GID(10001)便于宿主侧挂载卷时对齐权限。
# uploads 目录必须在这里创建并 chown：Docker 初始化具名卷时会继承镜像内该路径的属主，
# 若属主是 root，卷挂载后非 root 进程写头像会直接 Permission denied。
# curl 仅用于 HEALTHCHECK：aspnet 运行镜像(debian-slim)不含任何 HTTP 客户端。
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 appuser \
    && useradd --uid 10001 --gid 10001 --shell /usr/sbin/nologin --create-home appuser \
    && mkdir -p /app/uploads \
    && chown -R appuser:appuser /app

# 存活探针：进程起来即 200(不查数据库)，避免依赖未就绪时被误判为不健康
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://127.0.0.1:5000/health || exit 1

USER appuser
ENTRYPOINT ["dotnet", "LHZ.OnlineChat.Server.dll"]
