/// <reference types="vite/client" />

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<{}, {}, any>
  export default component
}

/**
 * highlight.js 只给 core/common 提供了类型声明，`lib/languages/*` 没有 ——
 * 按需引入语言包时会报 TS7016，这里补一条环境声明（形状与官方 common.d.ts 一致）。
 */
declare module 'highlight.js/lib/languages/*' {
  import type { LanguageFn } from 'highlight.js'
  const language: LanguageFn
  export default language
}
