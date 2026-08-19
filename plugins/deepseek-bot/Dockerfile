FROM node:22-alpine

WORKDIR /app

# 插件零依赖，只需脚本本身
COPY bot.mjs ./
COPY .env.example ./

# 容器内运行：配置一律通过环境变量注入（docker-compose），
# 存在 .env 文件时也会被 bot.mjs 读取（本地直接 node 运行时使用）
ENV NODE_ENV=production
EXPOSE 9311

CMD ["node", "bot.mjs"]
