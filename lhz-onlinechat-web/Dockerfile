# ===== 构建阶段 =====
# 注意:构建上下文为仓库根目录(见 docker-compose.yml / docker build .)
FROM node:22-alpine AS build
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
FROM nginx:1.27-alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY lhz-onlinechat-web/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
