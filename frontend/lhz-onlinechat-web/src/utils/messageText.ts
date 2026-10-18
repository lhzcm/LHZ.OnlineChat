/**
 * 文本消息的渲染（纯函数）：转义 HTML → 搜索关键词高亮 → @提及高亮。
 *
 * 从 MessageBubble 里抽出来是为了能单测：这段逻辑同时服务"气泡文本"与"转发明细"
 * 两条渲染路径，写错一次就是 XSS 或高亮错位，而组件里很难断言。
 */

/** 转义 HTML（防 XSS）：结果会被 v-html 渲染，必须先转义 */
export function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/**
 * 渲染文本消息：转义 → 关键词高亮（`<mark>`）→ @提及高亮（`<span class="mention">`）。
 *
 * 实现要点：先用占位符把 @提及 片段换出来，再做关键词的 split/join，
 * 否则关键词恰好出现在 `@昵称` 里时会把 mention 标签拆坏（标签只剩半截，浏览器直接吞掉）。
 */
export function renderTextContent(content: string, keyword?: string): string {
  const escaped = escapeHtml(content || '')

  // 1. 提取 @提及 片段（占位符替换，稍后还原）
  const mentions: string[] = []
  const withoutMention = escaped.replace(/@([^\s@，。！？!?,]+)/g, (m) => {
    mentions.push(m)
    return `\u0000${mentions.length - 1}\u0000`
  })

  // 2. 搜索关键词高亮（split/join 而非正则，避免关键词里的正则元字符）
  const kw = escapeHtml((keyword || '').trim())
  let html = kw
    ? withoutMention.split(kw).join(`<mark class="search-mark">${kw}</mark>`)
    : withoutMention

  // 3. 还原 @提及（若关键词与提及重叠，mark 已包住原文，还原后嵌套正确）
  html = html.replace(/\u0000(\d+)\u0000/g, (_, i) => `<span class="mention">${mentions[Number(i)]}</span>`)
  return html
}
