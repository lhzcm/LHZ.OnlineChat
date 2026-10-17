import { fileURLToPath, URL } from 'node:url'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

/**
 * 前端测试工程。
 *
 * 独立的 npm 工程而不是塞进应用目录：应用只需要「能构建」，测试框架（vitest / jsdom /
 * @vue/test-utils）不该出现在应用依赖里，也不该被镜像构建装一遍。
 *
 * 被测源码用 `@` 别名指到 ../lhz-onlinechat-web/src，与应用自己的 vite 别名一致 ——
 * 否则应用源码内部的 `@/...` 互相引用解析不了。
 */
const appSrc = fileURLToPath(new URL('../lhz-onlinechat-web/src', import.meta.url))

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': appSrc },
    // 应用目录下还有它自己的一份 node_modules（构建用）。不 dedupe 的话，
    // 测试与「被测源码」会各自加载一份 vue/pinia —— 两个 getActivePinia() 互不相认，
    // 表现就是 "getActivePinia() was called but there was no active Pinia"。
    dedupe: ['vue', 'pinia', 'axios', 'marked', 'dompurify', 'highlight.js']
  },
  test: {
    environment: 'jsdom',
    include: ['tests/**/*.test.ts'],
    // 每个用例后恢复 spy/mock，避免用例之间互相污染
    restoreMocks: true
  }
})
