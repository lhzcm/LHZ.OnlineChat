# 前端测试工程

被测源码：`../lhz-onlinechat-web/src`（通过 vitest 别名 `@` 引用，与应用自身的 `@/...` 一致）。
本工程**不参与**应用构建与镜像：`lhz-onlinechat-web` 的 `package.json` 里没有任何测试依赖，
Dockerfile 里也不会装 vitest。

```bash
cd frontend/tests
npm ci
npm run test        # vitest run（jsdom 环境）
npm run typecheck   # vue-tsc --noEmit（含被测源码与 .vue 单文件组件）
```

## 覆盖范围

| 文件 | 覆盖什么 |
|---|---|
| `tests/markdown.test.ts` | Markdown 渲染与**消毒**（XSS 反例、图片/外链策略）、代码高亮、`stripMarkdown` 预览剥离 |
| `tests/chat-store.test.ts` | 会话 key、消息去重/回填状态、跨会话标失败、撤回、未读与标题角标、LRU 上限 |
| `tests/format.test.ts` | 消息时间格式化的三个分支 |
| `tests/message-bubble.test.ts` | 气泡的四个分支（文本/图片/Markdown/已撤回）+ 搜索高亮 + 端到端消毒 |
| `tests/dependency-parity.test.ts` | 本工程与被测应用的依赖版本范围完全一致（防漂移） |

不测什么：模板外观、样式、`axios` 拦截器的网络时序 —— 这些靠人眼与集成环境，塞进单测只会变成
"断言自己写的假实现"。

## 依赖版本为什么要和被测应用对齐

`marked` / `dompurify` / `highlight.js` / `vue` / `pinia` 在这里是**解析被测源码用的**，不是本工程
自己的运行时。两边范围写歪了，就会出现"测试跑的是 marked 18、线上跑的是 marked 19"这种假绿。
`dependency-parity.test.ts` 就是钉这一点的：改应用依赖版本时它会直接失败，提醒你同步本工程。

## 为什么 `tsconfig.json` 里有一串 `paths` 映射

TypeScript 解析裸模块名（`vue` / `axios` / `marked` …）时，是从**引用方文件所在目录**往上找
`node_modules`。被测源码在 `../lhz-onlinechat-web/src`，所以本地只要应用装过依赖就能解析 ——
而 CI 只安装本工程这一份（应用目录没有 `node_modules`），同一个 `vue-tsc` 就会报一片
`TS2307: Cannot find module 'vue'`。

`tsconfig.json` 把这几条显式映射到 `./node_modules/*`，让类型检查**不依赖应用是否装过依赖**。
`vitest` 侧对应的是 `resolve.dedupe`（否则测试与被测源码会各自加载一份 `pinia`，报
`getActivePinia() was called but there was no active Pinia`）。

改动这里前先想清楚：删掉任意一条，`npm ci && npm run typecheck` 在干净环境里就会挂。
