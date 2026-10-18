import { describe, expect, it } from 'vitest'
import { escapeHtml, renderTextContent } from '@/utils/messageText'

/**
 * 文本消息渲染（从 MessageBubble 抽出的纯函数）。
 * 三条约束：先转义防 XSS、@提及要成高亮、关键词高亮不能把 mention 标签拆坏。
 */
describe('escapeHtml', () => {
  it('转义尖括号与引号（内容会被 v-html 渲染）', () => {
    expect(escapeHtml('<script>alert("x")</script>'))
      .toBe('&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;')
  })

  it('先转 & 再转其它，避免二次转义', () => {
    expect(escapeHtml('a & b < c')).toBe('a &amp; b &lt; c')
  })
})

describe('renderTextContent', () => {
  it('把 @昵称 变成高亮节点', () => {
    expect(renderTextContent('你好 @张三 在吗'))
      .toContain('<span class="mention">@张三</span>')
  })

  it('转义 HTML，脚本不会成为标签', () => {
    const html = renderTextContent('<img src=x onerror=alert(1)>')

    expect(html).not.toContain('<img')
    expect(html).toContain('&lt;img')
  })

  it('关键词高亮成 <mark>', () => {
    expect(renderTextContent('今天部署上线', '部署'))
      .toContain('<mark class="search-mark">部署</mark>')
  })

  it('关键词与 @提及 重叠时不拆坏 mention 标签', () => {
    const html = renderTextContent('@张三 来了', '张三')

    // mention 必须在，且标签成对（拆坏会变成半截标签被浏览器吞掉）
    expect(html).toContain('<span class="mention">')
    expect(html).toContain('</span>')
    expect((html.match(/<span class="mention">/g) || []).length).toBe(1)
  })

  it('关键词里的正则元字符按字面匹配', () => {
    expect(renderTextContent('这里是 a.*b 的说明', '.*'))
      .toContain('<mark class="search-mark">.*</mark>')
  })

  it('空内容返回空串（不产生空 mark）', () => {
    expect(renderTextContent('')).toBe('')
    expect(renderTextContent('', '关键词')).toBe('')
  })

  it('没有关键词时不做 mark 处理', () => {
    expect(renderTextContent('普通一句话')).toBe('普通一句话')
  })
})
