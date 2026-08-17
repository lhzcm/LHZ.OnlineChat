# OnlineChat 线上部署手册

第一版功能完整,项目已内置 Docker 容器化方案(PostgreSQL + Redis + 后端 + 前端 nginx),本手册覆盖从服务器准备到 HTTPS 上线的完整流程。

---

## 一、前置准备

### 1. 服务器要求

- **系统**:Debian 12 / Ubuntu 22.04+ / CentOS 9(以下命令以 Debian/Ubuntu 为例)
- **配置建议**:2 核 2GB 起(生产建议 2 核 4GB),20GB 磁盘
- **开放端口**:`80`(HTTP)、`443`(HTTPS,如配域名);`8080` 仅在测试期需要,上线后可不开放
- **域名**(可选但推荐):解析 A 记录到服务器 IP,如 `chat.example.com`

### 2. 安装 Docker 与 Compose 插件

```bash
curl -fsSL https://get.docker.com | sh
systemctl enable --now docker
docker compose version   # 应显示 v2.x
```

> 国内服务器如拉镜像慢,可配置镜像加速(如 163/阿里云 registry-mirrors)。

### 3. 获取代码

**方式 A:git(推荐,方便后续更新)**

在 GitHub/Gitee 创建私有仓库,把本地代码推上去:

```bash
# 本地执行一次
git remote add origin https://github.com/<你>/LHZ.OnlineChat.git
git push -u origin master
```

服务器上:

```bash
cd /opt
git clone https://github.com/<你>/LHZ.OnlineChat.git onlinechat
cd onlinechat
```

**方式 B:直接上传**

用 scp/宝塔面板 等把整个项目目录(排除 `node_modules`、`bin`、`obj`、`.git`)上传到服务器 `/opt/onlinechat`。

---

## 二、配置环境变量

```bash
cd /opt/onlinechat
cp .env.example .env
vim .env
```

**必改项**:

| 配置 | 说明 |
|---|---|
| `POSTGRES_PASSWORD` | 数据库密码,**务必改成强随机密码**(如 `openssl rand -base64 24`) |
| `JWT_SECRET` | 至少 32 字符随机串:`openssl rand -base64 48` |

**邮件(SMTP)**(注册验证码必需,建议配置):

```ini
SMTP_HOST=smtp.163.com        # 你已验证的 SMTP 服务器
SMTP_PORT=465
SMTP_ENABLE_SSL=true
SMTP_USER=fastemail2026@163.com
SMTP_PASSWORD=你的授权码
SMTP_FROM=fastemail2026@163.com
```

> SMTP 留空时验证码会打印到后端日志并随接口返回(仅限测试)。

**其他可选项**:

```ini
VITE_WS_URL=          # 留空即可:前端自动使用当前站点同域 /ws(https 下自动 wss)
CORS_ORIGINS=*        # 同域部署默认即可;若前端与 API 分离再收紧
WEB_PORT=8080         # 前端入口端口(配 HTTPS 后由 80/443 反代)
```

---

## 三、构建并启动

```bash
cd /opt/onlinechat
docker compose up -d --build
```

首次构建约 5-10 分钟(拉取镜像 + dotnet publish + 前端构建)。

验证:

```bash
docker compose ps          # 四个服务都应为 Up/Healthy
docker compose logs -f backend   # 看到 "Application started" 即成功
curl http://localhost:8080       # 返回前端页面
curl http://localhost:8080/api/auth/me   # 401(认证保护正常=链路通)
```

启动时后端自动:创建数据库 → 同步表结构 → 账号 ID 迁移(从 10000 起)。

---

## 四、配置 HTTPS(推荐方案:域名 + Caddy 自动证书)

Caddy 自动申请/续期 Let's Encrypt 证书,一条命令完成 HTTPS + wss:

```bash
apt install -y caddy
cat > /etc/caddy/Caddyfile <<'EOF'
chat.example.com {
    reverse_proxy 127.0.0.1:8080 {
        # 透传 WebSocket 升级
        header_up Upgrade {http.request.header.Upgrade}
        header_up Connection {http.request.header.Connection}
    }
}
EOF
systemctl enable --now caddy
```

访问 `https://chat.example.com` 即为 HTTPS,前端 WS 自动走 `wss://chat.example.com/ws`(nginx 已配好 `/ws` 反代),无需改任何配置。

> **注意**:8080 端口最好只监听本机(把 `.env` 里 `WEB_PORT` 保持 8080 即可,云安全组不要放行 8080,只放行 80/443),避免绕过 HTTPS 直接访问明文。

### 备选方案:nginx + certbot

```bash
apt install -y nginx certbot python3-certbot-nginx
cat > /etc/nginx/sites-available/onlinechat <<'EOF'
map $http_upgrade $connection_upgrade { default upgrade; '' close; }
server {
    listen 80;
    server_name chat.example.com;
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
EOF
ln -s /etc/nginx/sites-available/onlinechat /etc/nginx/sites-enabled/
nginx -t && systemctl reload nginx
certbot --nginx -d chat.example.com    # 自动申请证书并配置 443
```

---

## 五、日常运维

### 查看状态与日志

```bash
docker compose ps                 # 服务状态
docker compose logs -f backend    # 后端日志(含邮件发送/WS 连接)
docker compose logs -f frontend   # nginx 日志
```

### 更新版本(代码有改动时)

```bash
cd /opt/onlinechat
git pull                          # 或重新上传代码
docker compose up -d --build      # 重建变更的镜像并滚动重启
```

### 数据备份

数据全部在 Docker 卷中(`pgdata`/`redisdata`/`uploaddata`),备份:

```bash
# 全量备份(推荐 cron 每日执行)
docker compose stop postgres redis
tar czf onlinechat-data-$(date +%F).tar.gz \
  /var/lib/docker/volumes/onlinechat_pgdata \
  /var/lib/docker/volumes/onlinechat_uploaddata
docker compose start postgres redis

# 或在线备份 PostgreSQL
docker exec onlinechat-postgres-1 pg_dump -U postgres OnlineChat > onlinechat-$(date +%F).sql
```

> 卷名以 `docker volume ls` 实际输出为准。

### 恢复

```bash
docker compose down
# 用备份的 pgdata 目录替换对应卷目录后
docker compose up -d
```

---

## 六、上线检查清单

- [ ] `POSTGRES_PASSWORD`、`JWT_SECRET` 已改为强随机值
- [ ] SMTP 已配置,注册验证码能真实收到邮件
- [ ] 云安全组只放行 80/443(不放行 8080/55432/56379)
- [ ] HTTPS 已生效,`https://域名` 正常,WS 自动 wss
- [ ] `docker compose logs backend` 无报错
- [ ] 已配置每日备份

---

## 常见问题

| 问题 | 处理 |
|---|---|
| 构建时 `failed size validation`(旧版 Docker 缓存损坏) | `docker builder prune -a -f` 后重建;或 `DOCKER_BUILDKIT=0 docker compose build` |
| 502 Bad Gateway | 后端未就绪:`docker compose logs backend` 等 "Application started" |
| 收不到验证码 | 查 `docker compose logs backend` 的 `[MAIL]` 行;检查 SMTP 授权码/端口 |
| 换服务器后数据迁移 | 备份卷 → 新服务器恢复卷 → `docker compose up -d` |
| 磁盘占用 | `docker system prune` 清理无用镜像/构建缓存(不影响数据卷) |
