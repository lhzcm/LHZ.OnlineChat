<script setup lang="ts">
import { computed } from 'vue'
import { renderMarkdownMessage } from '@/utils/markdown'

/**
 * Markdown 消息体：marked 解析 + DOMPurify 消毒 + highlight.js 着色（见 utils/markdown.ts）。
 *
 * 样式随组件走：整块文档排版（标题/表格/代码块/语法高亮）都在这份 scoped CSS 里。
 * 注意所有后代选择器都必须是 :deep() —— v-html 注入的节点不带 scoped 属性，
 * 写成裸选择器会整段静默失效。面板自身 .msg-markdown 是模板渲染的，不需要 :deep()。
 */
const props = defineProps<{ content: string; highlight?: string }>()

const html = computed(() => renderMarkdownMessage(props.content || '', props.highlight))
</script>

<template>
  <div class="msg-markdown" v-html="html"></div>
</template>

<style scoped>
.msg-markdown {
  width: 100%;
  padding: 14px 18px;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-left: 3px solid var(--border);
  border-radius: 10px;
  font-size: 14px;
  line-height: 1.7;
  word-break: break-word;
}

/* 自己发出的文档：左侧主色条（不给整块染渐变，读起来正式些） */
.msg-row.mine .msg-markdown {
  border-left-color: var(--primary);
}

.msg-markdown :deep(> :first-child) { margin-top: 0; }
.msg-markdown :deep(> :last-child) { margin-bottom: 0; }

.msg-markdown :deep(p) {
  margin: 0 0 9px;
}

.msg-markdown :deep(strong) {
  font-weight: 600;
}

.msg-markdown :deep(del) {
  color: var(--text-secondary);
}

.msg-markdown :deep(h1),
.msg-markdown :deep(h2),
.msg-markdown :deep(h3),
.msg-markdown :deep(h4),
.msg-markdown :deep(h5),
.msg-markdown :deep(h6) {
  margin: 16px 0 8px;
  font-weight: 600;
  line-height: 1.35;
}

/* 文档式层级：h1/h2 带分隔线，而不是把字号粗暴放大 */
.msg-markdown :deep(h1) {
  font-size: 20px;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--border);
}

.msg-markdown :deep(h2) {
  font-size: 17.5px;
  padding-bottom: 4px;
  border-bottom: 1px solid var(--border);
}

.msg-markdown :deep(h3) {
  font-size: 15.5px;
}

.msg-markdown :deep(h4),
.msg-markdown :deep(h5),
.msg-markdown :deep(h6) {
  font-size: 14.5px;
  color: var(--text-secondary);
}

.msg-markdown :deep(ul),
.msg-markdown :deep(ol) {
  margin: 6px 0 10px;
  padding-left: 22px;
}

.msg-markdown :deep(li) {
  margin: 3px 0;
}

.msg-markdown :deep(li)::marker {
  color: var(--text-secondary);
}

.msg-markdown :deep(blockquote) {
  margin: 9px 0;
  padding: 7px 12px;
  border-left: 3px solid var(--primary-light);
  background: var(--bg-hover);
  border-radius: 0 6px 6px 0;
  color: var(--text-secondary);
}

/* 行内代码：浅色小色块，与代码块区分开 */
.msg-markdown :deep(code) {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, 'Courier New', monospace;
  font-size: 12.5px;
  padding: 1.5px 5px;
  border-radius: 5px;
  background: var(--code-inline-bg);
  color: var(--code-inline-fg);
}

/* 代码块：深色代码面板 + 等宽 + 横向滚动（长行不换行、也不撑破面板） */
.msg-markdown :deep(pre) {
  margin: 10px 0;
  padding: 12px 14px;
  background: var(--code-bg);
  border-radius: 10px;
  overflow-x: auto;
  font-size: 12.5px;
  line-height: 1.65;
}

.msg-markdown :deep(pre code) {
  display: block;
  padding: 0;
  background: transparent;
  color: var(--code-fg);
  font-size: inherit;
  white-space: pre;
  tab-size: 4;
}

/* ---------- 语法高亮（highlight.js 的 .hljs-* 类） ---------- */
/* 代码面板在浅色/深色两种主题下都是深色，所以 token 配色不跟随主题 */
.msg-markdown :deep(.hljs-comment),
.msg-markdown :deep(.hljs-quote) {
  color: #8b93a3;
  font-style: italic;
}

.msg-markdown :deep(.hljs-keyword),
.msg-markdown :deep(.hljs-selector-tag),
.msg-markdown :deep(.hljs-literal),
.msg-markdown :deep(.hljs-doctag) {
  color: #c792ea;
}

.msg-markdown :deep(.hljs-string),
.msg-markdown :deep(.hljs-regexp),
.msg-markdown :deep(.hljs-addition),
.msg-markdown :deep(.hljs-attribute) {
  color: #a5e075;
}

.msg-markdown :deep(.hljs-number),
.msg-markdown :deep(.hljs-symbol),
.msg-markdown :deep(.hljs-bullet),
.msg-markdown :deep(.hljs-variable),
.msg-markdown :deep(.hljs-template-variable) {
  color: #f78c6c;
}

.msg-markdown :deep(.hljs-title),
.msg-markdown :deep(.hljs-name),
.msg-markdown :deep(.hljs-section),
.msg-markdown :deep(.hljs-selector-id),
.msg-markdown :deep(.hljs-selector-class) {
  color: #82aaff;
}

.msg-markdown :deep(.hljs-built_in),
.msg-markdown :deep(.hljs-type),
.msg-markdown :deep(.hljs-attr),
.msg-markdown :deep(.hljs-params) {
  color: #ffcb6b;
}

.msg-markdown :deep(.hljs-meta),
.msg-markdown :deep(.hljs-link) {
  color: #7f9cd6;
}

.msg-markdown :deep(.hljs-deletion) {
  color: #ff8a80;
}

.msg-markdown :deep(.hljs-emphasis) {
  font-style: italic;
}

.msg-markdown :deep(.hljs-strong) {
  font-weight: 600;
}

/* 深色底上的滚动条别太扎眼 */
.msg-markdown :deep(pre::-webkit-scrollbar) {
  height: 8px;
}

.msg-markdown :deep(pre::-webkit-scrollbar-thumb) {
  background: rgba(255, 255, 255, 0.22);
  border-radius: 4px;
}

.msg-markdown :deep(pre::-webkit-scrollbar-track) {
  background: transparent;
}

.msg-markdown :deep(table) {
  border-collapse: collapse;
  margin: 10px 0;
  font-size: 13px;
}

.msg-markdown :deep(th),
.msg-markdown :deep(td) {
  border: 1px solid var(--border);
  padding: 5px 10px;
}

.msg-markdown :deep(th) {
  background: var(--bg-hover);
  font-weight: 600;
}

.msg-markdown :deep(a) {
  color: var(--primary);
  text-decoration: none;
  border-bottom: 1px solid var(--primary-light);
}

.msg-markdown :deep(a:hover) {
  border-bottom-color: var(--primary);
}

.msg-markdown :deep(hr) {
  border: none;
  border-top: 1px solid var(--border);
  margin: 14px 0;
}

/* 高亮节点（@提及 / 搜索关键词）同样是脚本注入的，必须 :deep() */
.msg-markdown :deep(.mention) {
  color: var(--primary);
  font-weight: 600;
}

.msg-markdown :deep(.search-mark) {
  background: var(--search-mark-bg, #ffd54d);
  color: #333;
  border-radius: 3px;
  padding: 0 2px;
}

html[data-theme='dark'] .msg-markdown :deep(.search-mark) {
  background: #b8860b;
  color: #fff;
}
</style>