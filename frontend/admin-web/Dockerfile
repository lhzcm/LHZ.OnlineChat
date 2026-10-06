# ===== 构建阶段 =====
# 注意:构建上下文是 frontend/ 目录(见 docker-compose.yml 的 context: ./frontend),
# 所以下面的 COPY 路径都相对于 frontend/,而不是仓库根目录。
FROM docker.m.daocloud.io/library/node:22-alpine AS build
WORKDIR /app
COPY admin-web/package.json admin-web/package-lock.json ./
# 静默安装:减少构建输出量,同时提升旧版 Docker 引擎(流式输出解析)的稳定性
RUN npm ci --no-audit --no-fund --silent
COPY admin-web/ ./
RUN npm run build

# ===== 运行阶段:nginx 托管管理后台静态文件 =====
# 同主前端:unprivileged 变体以 nginx 用户(UID 101)运行,配置路径不变,但监听 8080
FROM docker.m.daocloud.io/nginxinc/nginx-unprivileged:1.27-alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY admin-web/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
