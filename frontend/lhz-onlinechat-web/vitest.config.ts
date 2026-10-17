import { defineConfig } from 'vitest/config'

/**
 * 前端单测配置（只有 utils 这类纯逻辑/可 jsdom 化的部分在范围内）。
 *
 * 为什么不复用 vite.config.ts：那个文件的 configureServer 里挂了本地 API 代理，
 * 单测根本不会起服务器；单独一份配置更省事，也不会为了测试污染构建配置。
 */
export default defineConfig({
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
    reporters: 'default'
  }
})
