import { describe, expect, it } from 'vitest'
import { renderMarkdown, renderMarkdownMessage, stripMarkdown } from './markdown'

describe('stripMarkdown', () => {
  it('抹掉标题/列表/强调/行内代码/链接语法，只留文字', () => {
    const plain = stripMarkdown('# 标题\n\n- 一\n- 二\n\n**粗体** 与 `代码` 和 [链接](https://example.com)')

    expect(plain).not.toMatch(/[#*`[\]]/)
    expect(plain).toContain('标题')
    expect(plain).toContain('粗体')
    expect(plain).toContain('链接')
    expect(plain).not.toContain('https://example.com')
  })

  it('代码块整块剔除（预览里不该出现整段代码）', () => {
    const plain = stripMarkdown('前文\n```js\nconst a = 1\n```\n后文')

    expect(plain).toContain('前文')
    expect(plain).toContain('后文')
    expect(plain).not.toContain('const')
  })

  it('图片语法剔除（预览不该带出第三方地址）', () => {
    expect(stripMarkdown('看 ![图](https://evil.example/x.png) 这个')).not.toContain('evil.example')
  })
})

describe('renderMarkdown：结构', () => {
  it('渲染标题、表格、代码块', () => {
    const html = renderMarkdown('# 标题\n\n| a | b |\n| - | - |\n| 1 | 2 |\n')

    expect(html).toContain('<h1')
    expect(html).toContain('<table')
    expect(html).toContain('<th')
  })

  it('单个换行也换行（breaks: true，与文本消息观感一致）', () => {
    expect(renderMarkdown('第一行\n第二行')).toContain('<br')
  })
})

describe('renderMarkdown：消毒（XSS 回归）', () => {
  it('剥掉 script 标签', () => {
    const html = renderMarkdown('正常文本 <script>alert(1)</script>')

    expect(html).toContain('正常文本')
    expect(html.toLowerCase()).not.toContain('<script')
  })

  it('剥掉事件属性（onerror 等）', () => {
    const html = renderMarkdown('<img src=x onerror="alert(1)">')

    expect(html.toLowerCase()).not.toContain('onerror')
    expect(html.toLowerCase()).not.toContain('<img')
  })

  it('剥掉 iframe / svg', () => {
    const html = renderMarkdown('<iframe src="https://evil.example"></iframe><svg onload="alert(1)"></svg>')

    expect(html.toLowerCase()).not.toContain('<iframe')
    expect(html.toLowerCase()).not.toContain('<svg')
  })

  it('javascript: 链接被中和', () => {
    expect(renderMarkdown('[点我](javascript:alert(1))').toLowerCase()).not.toContain('javascript:')
  })

  it('Markdown 图片一律剥掉（防跟踪像素 / IP 泄露）', () => {
    const html = renderMarkdown('![x](https://evil.example/track.png)')

    expect(html).not.toContain('<img')
    expect(html).not.toContain('evil.example')
  })

  it('外链强制新窗口 + noopener', () => {
    const html = renderMarkdown('[文档](https://example.com/doc)')

    expect(html).toContain('target="_blank"')
    expect(html).toContain('rel="noopener noreferrer nofollow"')
  })

  it('行内 HTML 里的 style 属性被剥掉（防止用 z-index 伪造界面）', () => {
    expect(renderMarkdown('<span style="position:fixed;z-index:9999">假界面</span>')).not.toContain('style=')
  })
})

describe('renderMarkdownMessage：代码高亮与内联高亮', () => {
  it('指定语言的代码块带 hljs 着色', () => {
    const html = renderMarkdownMessage('```js\nconst a = 1\n```')

    expect(html).toContain('hljs')
    expect(html).toContain('hljs-keyword')
  })

  it('未注册的语言保持纯文本（不硬猜）', () => {
    const html = renderMarkdownMessage('```foobar\nconst a = 1\n```')

    expect(html).not.toContain('hljs')
  })

  it('没写语言的代码块走自动识别', () => {
    const html = renderMarkdownMessage(
      '```\nSELECT id FROM "GroupMessage" WHERE content LIKE \'%x%\' LIMIT 20;\n```'
    )

    expect(html).toContain('hljs')
  })

  it('@提及渲染成 mention 高亮', () => {
    expect(renderMarkdownMessage('你好 @张三 在吗')).toContain('class="mention"')
  })

  it('搜索关键词渲染成 search-mark', () => {
    expect(renderMarkdownMessage('这条提到 部署 两次：部署', '部署')).toContain('class="search-mark"')
  })

  it('代码块里的 @ 与关键词不被高亮破坏', () => {
    const html = renderMarkdownMessage('```js\n// @张三 部署\n```', '部署')

    expect(html).not.toContain('class="mention"')
    expect(html).not.toContain('class="search-mark"')
  })

  it('没有代码块也没有 @/关键词时直接返回消毒后的 HTML（省掉一次 DOM 往返）', () => {
    const html = renderMarkdownMessage('普通一段话')

    expect(html).toBe(renderMarkdown('普通一段话'))
  })
})
