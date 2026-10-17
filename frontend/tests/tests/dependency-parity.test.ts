import { readFileSync } from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * 本工程是**独立的 npm 工程**，被测源码来自 ../lhz-onlinechat-web。
 * 这里的依赖只用于解析被测代码（marked/dompurify/highlight.js/vue/pinia/axios），
 * 两边版本范围写歪了就会出现"测试跑的是 marked 18、线上是 19"的假绿 —— 这条把它钉住。
 */
const readJson = (relative: string) =>
  JSON.parse(readFileSync(fileURLToPath(new URL(relative, import.meta.url)), 'utf8'))

const app = readJson('../../lhz-onlinechat-web/package.json')
const tests = readJson('../package.json')

const SHARED = ['vue', 'pinia', 'axios', 'marked', 'dompurify', 'highlight.js']

describe('依赖版本与被测应用一致', () => {
  it.each(SHARED)('%s 的版本范围与应用完全一致', (name) => {
    expect(app.dependencies[name], `${name} 不在应用 dependencies 里`).toBeTruthy()
    expect(tests.devDependencies[name]).toBe(app.dependencies[name])
  })

  it('测试工程不引入应用没用的测试运行时之外的依赖', () => {
    const allowed = new Set([
      '@vitejs/plugin-vue',
      '@vue/test-utils',
      'jsdom',
      'typescript',
      'vitest',
      'vue-tsc',
      '@types/node',
      ...SHARED
    ])

    expect(Object.keys(tests.devDependencies).filter(d => !allowed.has(d))).toEqual([])
  })
})
