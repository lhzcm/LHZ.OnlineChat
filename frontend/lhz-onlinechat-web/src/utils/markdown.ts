import DOMPurify from 'dompurify'
import hljs from 'highlight.js/lib/core'
import bash from 'highlight.js/lib/languages/bash'
import csharp from 'highlight.js/lib/languages/csharp'
import css from 'highlight.js/lib/languages/css'
import diff from 'highlight.js/lib/languages/diff'
import javascript from 'highlight.js/lib/languages/javascript'
import json from 'highlight.js/lib/languages/json'
import markdown from 'highlight.js/lib/languages/markdown'
import python from 'highlight.js/lib/languages/python'
import sql from 'highlight.js/lib/languages/sql'
import typescript from 'highlight.js/lib/languages/typescript'
import xml from 'highlight.js/lib/languages/xml'
import yaml from 'highlight.js/lib/languages/yaml'
import { marked } from 'marked'

/**
 * Markdown 消息（messageType = 3）的渲染与消毒。
 *
 * 安全口径（§6：v-html 之前必须转义）：
 *   1. marked 只负责 Markdown → HTML，**它不负责安全**；
 *   2. 一律过 DOMPurify 再交给 v-html，标签/属性走白名单；
 *   3. 外链强制 target=_blank + rel=noopener noreferrer nofollow（防钓鱼页反向控制 window.opener）；
 *   4. 图片一律剥掉：`![](第三方地址)` 会让每个收消息的人向发送者指定的地址各发一次请求
 *      （IP、在线时段泄露 + 跟踪像素）。要发图走图片消息（上传接口），不要走 Markdown。
 */
const FORBID_TAGS = ['img', 'style', 'form', 'input', 'button', 'iframe', 'video', 'audio', 'svg', 'math']
const FORBID_ATTR = ['style', 'srcset', 'formaction', 'xlink:href']

/** 外链统一加固；只处理消毒后的 <a>，不解释任何用户 HTML */
DOMPurify.addHook('afterSanitizeAttributes', (node) => {
  if (node.tagName === 'A') {
    node.setAttribute('target', '_blank')
    node.setAttribute('rel', 'noopener noreferrer nofollow')
  }
})

function sanitize(html: string): string {
  return DOMPurify.sanitize(html, { FORBID_TAGS, FORBID_ATTR })
}

// ---------------------------------------------------------------------------
// 语法高亮：按需注册常用语言（每种 2~5KB），不引整包 highlight.js —— 那会把
// 首屏体积抬高一个量级。没注册的语言保持纯文本，不做"猜一个差不多的"。
// ---------------------------------------------------------------------------
hljs.registerLanguage('bash', bash)
hljs.registerLanguage('csharp', csharp)
hljs.registerLanguage('css', css)
hljs.registerLanguage('diff', diff)
hljs.registerLanguage('javascript', javascript)
hljs.registerLanguage('json', json)
hljs.registerLanguage('markdown', markdown)
hljs.registerLanguage('python', python)
hljs.registerLanguage('sql', sql)
hljs.registerLanguage('typescript', typescript)
hljs.registerLanguage('xml', xml)
hljs.registerLanguage('yaml', yaml)

/** 自动识别的候选语言：限定在已注册集合内 */
const AUTO_LANGUAGES = [
  'bash', 'csharp', 'css', 'diff', 'javascript', 'json', 'markdown',
  'python', 'sql', 'typescript', 'xml', 'yaml'
]

/** Markdown 源文本 → 可安全 v-html 的 HTML */
export function renderMarkdown(source: string): string {
  // breaks: 聊天里单个换行就该换行（与文本消息的观感一致）
  const html = marked.parse(source, { async: false, gfm: true, breaks: true })
  return sanitize(typeof html === 'string' ? html : '')
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/** @提及的识别与文本消息保持同一口径（MessageBubble.renderContent） */
const MENTION_PATTERN = '@[^\\s@，。！？!?,]+'

/**
 * 在**已消毒的 DOM** 上做 @提及与搜索关键词高亮。
 *
 * 为什么在 DOM 上做而不是拼 HTML 字符串：这里只创建文本节点与 <mark>/<span>，
 * 不解析任何来自消息的标记，因此不会给消毒后的结果重新开一个注入面。
 * pre/code/a 内的文本跳过 —— 免得把代码和链接拆坏。
 */
function highlightInPlace(root: HTMLElement, keyword: string): void {
  const hasKeyword = keyword.length > 0
  // 关键词为空时不能保留一个空的分组 —— `()` 会在每个字符位置匹配空串，
  // 结果是把整段文字插满空 <mark>（这就是为什么有/无关键词要构造不同的正则）。
  const source = hasKeyword
    ? `(${escapeRegExp(keyword)})|(${MENTION_PATTERN})`
    : `(${MENTION_PATTERN})`
  const pattern = new RegExp(source, 'g')

  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode(node: Node) {
      // 必须看**祖先链**而不是直接父节点：代码块被 hljs 着色后，代码文本的父节点是
      // <span class="hljs-*">，只判 parent.tagName 会让 @提及/关键词高亮插进代码里，
      // 既破坏代码原样，也会把注释里的 @名字 染成链接色。
      if (node.parentElement?.closest('pre, code, a')) return NodeFilter.FILTER_REJECT
      return NodeFilter.FILTER_ACCEPT
    }
  })

  const texts: Text[] = []
  while (walker.nextNode()) texts.push(walker.currentNode as Text)

  for (const node of texts) {
    const text = node.nodeValue ?? ''
    pattern.lastIndex = 0
    if (!pattern.test(text)) continue
    pattern.lastIndex = 0

    const fragment = document.createDocumentFragment()
    let cursor = 0
    let match: RegExpExecArray | null
    while ((match = pattern.exec(text)) !== null) {
      if (match.index > cursor) fragment.appendChild(document.createTextNode(text.slice(cursor, match.index)))
      // 有关键词时 match[1] = 关键词、match[2] = @提及；没有关键词时 match[1] 才是 @提及
      const isKeyword = hasKeyword && match[1] !== undefined
      const node2 = document.createElement(isKeyword ? 'mark' : 'span')
      node2.className = isKeyword ? 'search-mark' : 'mention'
      node2.textContent = match[0]
      fragment.appendChild(node2)
      cursor = match.index + match[0].length
      if (match[0].length === 0) pattern.lastIndex++
    }
    if (cursor < text.length) fragment.appendChild(document.createTextNode(text.slice(cursor)))
    node.parentNode?.replaceChild(fragment, node)
  }
}

/**
 * 代码块语法高亮。
 *
 * 在**已消毒的 DOM** 上做：先由 DOMPurify 把关，再让 highlight.js 只根据
 * textContent 重新生成 `<span class="hljs-*">` —— 全程不解析用户的 HTML。
 * 语言名先正则过滤、再要求是已注册语言；没写语言时自动识别，但相关性太低
 * 就保持纯文本（错色比无色更难看）。
 */
function highlightCodeBlocks(root: HTMLElement): void {
  for (const el of Array.from(root.querySelectorAll('pre > code'))) {
    const code = el as HTMLElement
    const source = code.textContent ?? ''
    const matched = /language-([\w+#.-]+)/.exec(code.className)
    const lang = matched ? matched[1].toLowerCase() : ''

    let highlighted: string | null = null
    if (lang) {
      if (!hljs.getLanguage(lang)) continue
      highlighted = hljs.highlight(source, { language: lang }).value
    } else {
      // 阈值取 2：JSON/YAML 这类语法的 relevance 天然偏低（官方示例常用 5，会把
      // 它们判成"没认出来"），这里宁可着色得朴素一点，也别整块白板。
      const auto = hljs.highlightAuto(source, AUTO_LANGUAGES)
      if (auto.relevance < 2) continue
      highlighted = auto.value
    }

    code.innerHTML = highlighted
    code.classList.add('hljs')
  }
}

/** Markdown 消息的完整渲染入口：解析 → 消毒 → 代码高亮 → 提及/关键词高亮 */
export function renderMarkdownMessage(source: string, keyword?: string): string {
  const html = renderMarkdown(source)
  const kw = (keyword ?? '').trim()
  // 没有代码块、没有关键词、也没有 @ 时不必过 DOM（聊天消息里这是多数）
  if (!html.includes('<pre') && !kw && !html.includes('@')) return html

  const container = document.createElement('div')
  container.innerHTML = html
  highlightCodeBlocks(container)
  if (kw || html.includes('@')) highlightInPlace(container, kw)
  return container.innerHTML
}

/**
 * 会话列表 / 搜索结果里的纯文本预览：抹掉 Markdown 标记。
 * 只用于展示，不接受渲染 —— 因此不需要（也不该）有 HTML 语义。
 */
export function stripMarkdown(source: string): string {
  return source
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`([^`]*)`/g, '$1')
    .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/^\s{0,3}#{1,6}\s+/gm, '')
    .replace(/^\s{0,3}>\s?/gm, '')
    .replace(/^\s{0,3}(?:[-*+]|\d+\.)\s+/gm, '')
    .replace(/(\*\*|__)(.*?)\1/g, '$2')
    .replace(/(\*|_)(.*?)\1/g, '$2')
    .replace(/\s+/g, ' ')
    .trim()
}
