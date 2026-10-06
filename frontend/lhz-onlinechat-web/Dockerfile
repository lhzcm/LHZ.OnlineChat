# ===== 构建阶段 =====
# 注意:构建上下文是 frontend/ 目录(见 docker-compose.yml 的 context: ./frontend),
# 所以下面的 COPY 路径都相对于 frontend/,而不是仓库根目录。
FROM docker.m.daocloud.io/library/node:22-alpine AS build
WORKDIR /app
COPY lhz-onlinechat-web/package.json lhz-onlinechat-web/package-lock.json ./
# 静默安装:减少构建输出量,同时提升旧版 Docker 引擎(流式输出解析)的稳定性
RUN npm ci --no-audit --no-fund --silent
COPY lhz-onlinechat-web/ .
# 生产 WebSocket 地址（如 wss://chat.example.com）；留空则前端自动使用当前站点同域
ARG VITE_WS_URL
ENV VITE_WS_URL=$VITE_WS_URL
RUN npm run build

# ===== 运行阶段:nginx 托管静态文件 + 反代 API/WebSocket =====
# 用 unprivileged 变体(上游 nginxinc/nginx-unprivileged):主进程以 nginx 用户(UID 101)运行,
# 不监听特权端口,容器逃逸/被攻破时拿不到 root。配置路径与官方镜像一致(/etc/nginx/conf.d/),
# 但容器内监听 8080 而不是 80,所以 nginx.conf 与 compose 端口映射都必须是 8080。
FROM docker.m.daocloud.io/nginxinc/nginx-unprivileged:1.27-alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY lhz-onlinechat-web/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
