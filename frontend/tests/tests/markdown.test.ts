import { describe, expect, it } from 'vitest'
import { renderMarkdown, renderMarkdownMessage, stripMarkdown } from '@/utils/markdown'

/**
 * Markdown 渲染链（marked → DOMPurify → highlight.js）的回归测试。
 *
 * 重点不是"渲染得好不好看"，而是三条硬约束：
 *   1. 任何用户/机器人提交的 HTML 与链接都不能成为注入面；
 *   2. Markdown 图片一律剥掉（否则每个收消息的人都会替发送者发一次请求）；
 *   3. 代码块要么正经着色，要么保持纯文本 —— 不许把 @提及/关键词插进代码里。
 */

describe('renderMarkdown：结构', () => {
  it('渲染标题、表格、代码块', () => {
    const html = renderMarkdown('# 标题\n\n| a | b |\n| - | - |\n| 1 | 2 |\n\n```js\nconst a = 1\n```')

    expect(html).toContain('<h1')
    expect(html).toContain('<table')
    expect(html).toContain('<th')
    expect(html).toContain('<pre')
  })

  it('单个换行也换行（breaks: true，与文本消息观感一致）', () => {
    expect(renderMarkdown('第一行\n第二行')).toContain('<br')
  })

  it('空内容与纯空白不抛异常', () => {
    expect(renderMarkdown('')).toBe('')
    expect(renderMarkdown('   \n\n  ').trim()).toBe('')
  })

  it('未闭合的代码围栏也能渲染（marked 按到结尾处理）', () => {
    expect(renderMarkdown('```js\nconst a = 1')).toContain('<pre')
  })

  it('嵌套结构（引用 > 列表 > 粗体）', () => {
    const html = renderMarkdown('> - **要点**\n> - 次要')

    expect(html).toContain('<blockquote')
    expect(html).toContain('<ul')
    expect(html).toContain('<strong>')
  })

  it('超长单行不抛异常且保持完整', () => {
    const long = 'A'.repeat(100_000)

    const html = renderMarkdown(long)

    expect(html.length).toBeGreaterThan(90_000)
  })
})

describe('renderMarkdown：消毒（XSS 回归）', () => {
  it('剥掉 script 标签', () => {
    const html = renderMarkdown('正常文本 <script>alert(1)</script>')

    expect(html).toContain('正常文本')
    expect(html.toLowerCase()).not.toContain('<script')
  })

  it('剥掉事件属性（onerror / onclick）', () => {
    const html = renderMarkdown('<img src=x onerror="alert(1)"><a onclick="alert(2)">x</a>')

    expect(html.toLowerCase()).not.toContain('onerror')
    expect(html.toLowerCase()).not.toContain('onclick')
    expect(html.toLowerCase()).not.toContain('<img')
  })

  it('剥掉 iframe / svg / math', () => {
    const html = renderMarkdown(
      '<iframe src="https://evil.example"></iframe><svg onload="alert(1)"></svg><math><mi>x</mi></math>'
    ).toLowerCase()

    expect(html).not.toContain('<iframe')
    expect(html).not.toContain('<svg')
    expect(html).not.toContain('<math')
  })

  it('剥掉表单类元素（防止伪造输入框骗凭据）', () => {
    const html = renderMarkdown('<form action="/api/login"><input name="password"><button>登录</button></form>').toLowerCase()

    expect(html).not.toContain('<form')
    expect(html).not.toContain('<input')
    expect(html).not.toContain('<button')
  })

  it('剥掉 style 属性（防止用 fixed/z-index 伪造界面）', () => {
    expect(renderMarkdown('<span style="position:fixed;z-index:9999">假界面</span>')).not.toContain('style=')
  })

  it.each([
    ['javascript:', '[点我](javascript:alert(1))'],
    ['vbscript:', '[点我](vbscript:msgbox(1))'],
    ['data:', '[点我](data:text/html,<script>alert(1)</script>)'],
    ['file:', '[点我](file:///etc/passwd)']
  ])('中和 %s 链接', (scheme, source) => {
    expect(renderMarkdown(source).toLowerCase()).not.toContain(scheme)
  })

  it('普通的 http(s) 与 mailto 链接保留', () => {
    const html = renderMarkdown('[a](https://example.com/x) [b](mailto:x@example.com)')

    expect(html).toContain('https://example.com/x')
    expect(html).toContain('mailto:x@example.com')
  })

  it('外链强制新窗口 + noopener', () => {
    const html = renderMarkdown('[文档](https://example.com/doc)')

    expect(html).toContain('target="_blank"')
    expect(html).toContain('rel="noopener noreferrer nofollow"')
  })

  it('Markdown 图片一律剥掉（防跟踪像素 / IP 泄露）', () => {
    const html = renderMarkdown('![x](https://evil.example/track.png)')

    expect(html).not.toContain('<img')
    expect(html).not.toContain('evil.example')
  })

  it('行内 HTML 里的 img 同样剥掉', () => {
    expect(renderMarkdown('<img src="https://evil.example/a.png">')).not.toContain('<img')
  })
})

describe('renderMarkdownMessage：代码高亮', () => {
  it('指定语言的代码块带 hljs 着色', () => {
    const html = renderMarkdownMessage('```js\nconst a = 1\n```')

    expect(html).toContain('hljs')
    expect(html).toContain('hljs-keyword')
  })

  it('未注册的语言保持纯文本（不硬猜）', () => {
    expect(renderMarkdownMessage('```foobar\nconst a = 1\n```')).not.toContain('hljs')
  })

  it('没写语言的代码块走自动识别', () => {
    const html = renderMarkdownMessage(
      '```\nSELECT id FROM "GroupMessage" WHERE content LIKE \'%x%\' LIMIT 20;\n```'
    )

    expect(html).toContain('hljs')
  })

  it('代码块里的 HTML 仍然被转义（高亮不引入注入面）', () => {
    const html = renderMarkdownMessage('```html\n<script>alert(1)</script>\n```')

    // hljs 会把标签名拆进 <span>，所以不会出现连续的 `&lt;script`；关键是尖括号被转义了
    expect(html).not.toContain('<script')
    expect(html).toContain('&lt;')
    expect(html).toContain('&gt;')
  })
})

describe('renderMarkdownMessage：@提及与关键词高亮', () => {
  it('@提及渲染成 mention 高亮', () => {
    expect(renderMarkdownMessage('你好 @张三 在吗')).toContain('class="mention"')
  })

  it('搜索关键词渲染成 search-mark', () => {
    expect(renderMarkdownMessage('这条提到 部署 两次：部署', '部署')).toContain('class="search-mark"')
  })

  it('关键词里的正则元字符按字面匹配', () => {
    const html = renderMarkdownMessage('这里是 a.*b 的说明', '.*')

    expect(html).toContain('class="search-mark"')
  })

  it('代码块里的 @ 与关键词不被高亮破坏（hljs 着色后父节点是 span 的坑）', () => {
    const html = renderMarkdownMessage('```js\n// @张三 部署\n```', '部署')

    expect(html).not.toContain('class="mention"')
    expect(html).not.toContain('class="search-mark"')
  })

  it('链接文字里的 @ 也不被高亮（否则会把链接拆成两半）', () => {
    const html = renderMarkdownMessage('[找 @张三 看](https://example.com)')

    expect(html).not.toContain('class="mention"')
  })

  it('没有代码块也没有 @/关键词时直接返回消毒结果（省一次 DOM 往返）', () => {
    const source = '普通一段话'

    expect(renderMarkdownMessage(source)).toBe(renderMarkdown(source))
  })
})

describe('stripMarkdown：会话列表/搜索结果的纯文本预览', () => {
  it('抹掉标题/列表/强调/行内代码/链接语法，只留文字', () => {
    const plain = stripMarkdown('# 标题\n\n- 一\n- 二\n\n**粗体** 与 `代码` 和 [链接](https://example.com)')

    expect(plain).not.toMatch(/[#*`[\]]/)
    expect(plain).toContain('标题')
    expect(plain).toContain('粗体')
    expect(plain).toContain('链接')
    expect(plain).not.toContain('https://example.com')
  })

  it('代码块整块剔除', () => {
    const plain = stripMarkdown('前文\n```js\nconst a = 1\n```\n后文')

    expect(plain).toContain('前文')
    expect(plain).toContain('后文')
    expect(plain).not.toContain('const')
  })

  it('图片语法剔除（预览不该带出第三方地址）', () => {
    expect(stripMarkdown('看 ![图](https://evil.example/x.png) 这个')).not.toContain('evil.example')
  })

  it('空串与纯空白返回空串', () => {
    expect(stripMarkdown('')).toBe('')
    expect(stripMarkdown('  \n\t ')).toBe('')
  })

  it('把多行折叠成一行（会话列表只有一行高）', () => {
    expect(stripMarkdown('第一段\n\n第二段')).toBe('第一段 第二段')
  })
})
