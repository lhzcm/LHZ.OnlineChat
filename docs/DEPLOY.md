# OnlineChat 线上部署手册

第一版功能完整,项目已内置 Docker 容器化方案(PostgreSQL + Redis + 后端 + 前端 nginx),本手册覆盖从服务器准备到 HTTPS 上线的完整流程。

---

## 一、前置准备

### 1. 服务器要求

- **系统**:Debian 12 / Ubuntu 22.04+ / CentOS 9(以下命令以 Debian/Ubuntu 为例)
- **配置建议**:2 核 2GB 起(生产建议 2 核 4GB),20GB 磁盘
- **开放端口**:`80`(HTTP)、`443`(HTTPS,如配域名);`8080` 仅在测试期需要,上线后可不开放
  - PostgreSQL(`55432`)与 Redis(`56379`)的映射只绑定在 `127.0.0.1`,天然无法从外部访问,无需在安全组放行
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

拉取github在线代码到本地

```bash
cd /opt
git clone https://github.com/lhzcm/LHZ.OnlineChat.git onlinechat
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

> **这一步是强制的**:`docker-compose.yml` 里的敏感变量都写成 `${VAR:?错误提示}` 形式,缺少任何一个,`docker compose` 会在解析配置时立刻报错退出(fail fast),不会用内置默认口令把服务跑起来。若看到
> `error while interpolating services.xxx: required variable XXX is missing a value: XXX 未设置:请先 cp .env.example .env 并填写`,说明 `.env` 没建好。

**必填项**(缺一不可):

| 配置 | 说明 |
|---|---|
| `POSTGRES_PASSWORD` | 数据库密码,**务必改成强随机密码**:`openssl rand -base64 24` |
| `REDIS_PASSWORD` | Redis 密码(同时用于 `redis-server --requirepass` 和后端连接串):`openssl rand -base64 24` |
| `JWT_SECRET` | JWT 签名密钥,至少 32 字符随机串:`openssl rand -base64 48` |
| `ROBOT_TOKEN_KEY` | 机器人令牌加密密钥,随机串:`openssl rand -base64 32`;留空会退回内置开发密钥,机器人令牌可被伪造 |

> 密码会被拼进连接串(`Host=...;Password=...`、`redis:6379,password=...`),所以**不要包含 `;` 和 `,`**;用 `openssl rand -base64` 生成即可满足。
> `.env` 已被 `.gitignore` 忽略,切勿提交或外发。


**邮件(SMTP)**(注册验证码必需,建议配置):

```ini
SMTP_HOST=smtp.163.com        # 你已验证的 SMTP 服务器
SMTP_PORT=465
SMTP_ENABLE_SSL=true
SMTP_USER=xxxxx@163.com
SMTP_PASSWORD=你的授权码
SMTP_FROM=xxxxx@163.com
```

> SMTP 留空时验证码会打印到后端日志并随接口返回(仅限测试)。

**其他可选项**:

```ini
VITE_WS_URL=          # 留空即可:前端自动使用当前站点同域 /ws(https 下自动 wss)
CORS_ORIGINS=*        # 同域部署默认即可;若前端与 API 分离再收紧
WEB_PORT=8080         # 前端入口端口(配 HTTPS 后由 80/443 反代)
PG_PORT=55432         # 仅绑定 127.0.0.1,供本机 psql 管理
REDIS_PORT=56379      # 仅绑定 127.0.0.1,供本机 redis-cli 管理
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
docker compose ps          # 见下方说明
docker compose logs -f backend   # 看到 "Application started" 即成功
curl http://localhost:8080       # 返回前端页面
curl http://localhost:8080/api/auth/me   # 401(认证保护正常=链路通)
```

`docker compose ps` 的期望状态:

| 服务 | 状态 |
|---|---|
| postgres / redis / backend / frontend | `Up ... (healthy)` |
| frontend-admin | `Up`(仅供主前端 nginx 反代 `/admin`,不单独暴露端口) |

> backend 的探针打的是匿名存活接口 `GET /health`(只表示进程活着,不查库);`backend` 会等 postgres、redis 变成 healthy 后再启动,所以首次启动时 backend 可能短暂显示 `Created`。

启动时后端自动:创建数据库 → 同步表结构 → 账号 ID 迁移(从 10000 起)。

### 容器以非 root 运行

- **backend**:镜像内创建了固定 UID/GID `10001` 的 `appuser`,`/app` 与 `/app/uploads` 都归它所有;具名卷 `uploaddata` 首次创建时会继承该属主,头像上传才能写入。
- **frontend / frontend-admin**:使用 `nginxinc/nginx-unprivileged` 镜像,nginx 以 `nginx` 用户(UID 101)运行,容器内监听 `8080`(普通用户无法绑定 80)。因此对外的 `WEB_PORT=8080` 映射的是容器内的 8080,`docker compose` 端口映射请勿改回 `:80`。

> **从旧版本升级**:旧的 `uploaddata` 卷是 root 属主,换成非 root 后上传会报 `Permission denied`。执行一次修复(不会删除数据):
>
> ```bash
> docker compose run --rm --no-deps --user root --entrypoint sh backend -c "chown -R 10001:10001 /app/uploads"
> docker compose up -d
> ```
>
> 全新部署无需此步骤。

### 部署拓扑:目前只支持单实例

**backend 不能横向扩容(不要 `--scale backend=N`),也不需要扩容。**

原因是 WebSocket 连接表在 backend 进程的内存里(`WsConnectionManager`),
消息推送直接遍历本进程的连接。多开实例会出现:

- 用户 A 连在实例 1、用户 B 连在实例 2 时,**B 收不到 A 的消息**(只在同实例内广播);
- 在线状态键被某个实例清掉,其他实例上还在线的用户会显示为离线;
- 踢下线只对连接在同一实例上的会话生效。

docker-compose 里 backend **没有发布端口**,只能经主前端 nginx 反代访问 ——
这也正是 `X-Forwarded-For` 可以被信任、限流能按真实客户端 IP 分区的前提。
若将来要直接暴露后端端口,必须把 `Program.cs` 里清空信任代理列表的那段改成显式白名单。

要真正支持多实例,需要引入 Redis pub/sub 作为跨实例广播背板(连接表迁移到 Redis +
各实例订阅广播频道)。这属于架构改造,当前版本未包含。

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

### 从旧版本升级时的三处行为变化

1. **多了一个必填变量 `REDIS_PASSWORD`**。compose 现在对
   `POSTGRES_PASSWORD` / `JWT_SECRET` / `ROBOT_TOKEN_KEY` / `REDIS_PASSWORD`
   都是「缺失即拒绝启动」,并且 Redis 开启 `requirepass`。
   升级前先补齐 `.env`,否则 `docker compose` 会直接报错(这是有意为之:
   宁可启动失败,也不要带着仓库里的默认密钥跑起来)。

2. **群组默认改为「仅限邀请加入」**。启动时会自动给 `Group_` 补一列
   `JoinPolicy`(默认 0 = 仅限邀请),**存量群一并收紧** ——
   在此之前群 ID 是连续自增且任何人可自行加入,等于所有群的历史消息都可被枚举读取。
   需要保留「知道群 ID 就能进」的群,由群主/管理员调
   `PUT /api/groups/{id}/join-policy` (`{"openToJoin": true}`) 显式开放。

3. **会创建一批唯一索引**。若存量数据里已存在重复(重复好友关系、重复黑名单、
   两个账号同邮箱),对应的唯一索引会创建失败:服务照常启动,但**只记一条警告**,
   该约束在清理重复数据前不生效(并发下仍可能产生重复行)。
   升级后请检查启动日志里的「唯一索引…创建失败」告警,按提示清理重复行后重启。

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

- [ ] `.env` 已由 `.env.example` 创建,且 `POSTGRES_PASSWORD`、`REDIS_PASSWORD`、`JWT_SECRET`、`ROBOT_TOKEN_KEY` 都是 `openssl rand` 生成的强随机值
- [ ] `docker compose config --quiet` 能通过(缺变量会在这里就报错)
- [ ] SMTP 已配置,注册验证码能真实收到邮件
- [ ] 云安全组只放行 80/443(不放行 8080/55432/56379);`55432`/`56379` 仅绑定 127.0.0.1
- [ ] HTTPS 已生效,`https://域名` 正常,WS 自动 wss
- [ ] `docker compose ps` 里 postgres/redis/backend/frontend 均为 healthy
- [ ] `docker compose logs backend` 无报错
- [ ] 已确认容器内进程非 root(backend 为 `appuser` UID 10001,nginx 为 UID 101)
- [ ] 已配置每日备份

---

## 常见问题

| 问题 | 处理 |
|---|---|
| `required variable XXX is missing a value`(compose 直接退出) | `.env` 缺失或漏填:`cp .env.example .env` 后补齐缺的必填项 |
| 构建时 `failed size validation`(旧版 Docker 缓存损坏) | `docker builder prune -a -f` 后重建;或 `DOCKER_BUILDKIT=0 docker compose build` |
| 502 Bad Gateway | 后端未就绪:`docker compose logs backend` 等 "Application started" |
| 头像/文件上传报 `Permission denied` | 旧卷属主是 root,按「容器以非 root 运行」一节的命令 chown 一次 |
| 前端容器 `unhealthy` 或服务起不来,日志说 `bind() to 0.0.0.0:80 failed` | nginx 配置里 `listen` 被改回了 80;非特权镜像只能用 8080 |
| 收不到验证码 | 查 `docker compose logs backend` 的 `[MAIL]` 行;检查 SMTP 授权码/端口 |
| 换服务器后数据迁移 | 备份卷 → 新服务器恢复卷 → `docker compose up -d` |
| 磁盘占用 | `docker system prune` 清理无用镜像/构建缓存(不影响数据卷) |
