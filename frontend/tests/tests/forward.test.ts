import { describe, expect, it } from 'vitest'
import {
  FORWARD_BUNDLE_VERSION,
  MAX_FORWARD_ITEM_LENGTH,
  MAX_FORWARD_MESSAGES,
  buildBatchCopyText,
  buildForwardBundle,
  forwardItemPreview,
  messagePreview,
  parseForwardBundle,
  type ForwardedItem
} from '@/utils/forward'
import type { ChatMessage } from '@/stores/chat'

/**
 * 「聊天记录」转发卡片（messageType = 4）的载荷逻辑。
 *
 * 一次转发 = 一条消息 = 一个 JSON 快照（items 里最多 20 条只读明细）。
 * 这里钉住：打包/解析的对称性、类型收敛、上限、以及各处的单行预览。
 * 上限常量必须与服务端 ForwardBundle 对齐（写死数字是故意的：改一边就该红）。
 */
function msg(over: Partial<ChatMessage> = {}): ChatMessage {
  return {
    type: 'private_message',
    from: '10002',
    to: '10001',
    content: '原文内容',
    timestamp: 1_700_000_000_000,
    messageId: 'm1',
    messageType: 0,
    senderName: '张三',
    senderAvatar: null,
    ...over
  } as ChatMessage
}

describe('buildForwardBundle', () => {
  it('打包成可解析的 JSON 快照，类型收敛为 0/1/3', () => {
    const json = buildForwardBundle([
      msg({ messageId: 'a', content: '文字', messageType: 0 }),
      msg({ messageId: 'b', content: '/uploads/x.png', messageType: 1 }),
      msg({ messageId: 'c', content: '# 标题', messageType: 3 }),
      // 其它类型（文件/嵌套转发）在卡片里退化成文字
      msg({ messageId: 'd', content: '未知类型', messageType: 2 })
    ], 1_800_000_000_000)

    const parsed = JSON.parse(json)

    expect(parsed.v).toBe(FORWARD_BUNDLE_VERSION)
    expect(parsed.items).toHaveLength(4)
    expect(parsed.items.map((i: ForwardedItem) => i.t)).toEqual([0, 1, 3, 0])
    expect(parsed.items[0].c).toBe('文字')
    expect(parsed.items[0].n).toBe('张三')
  })

  it('撤回的消息在快照里写成占位文案，不复制原文', () => {
    const json = buildForwardBundle([msg({ content: '机密原文', isDeleted: true })])

    expect(json).toContain('（消息已撤回）')
    expect(json).not.toContain('机密原文')
  })

  it('超过条数上限时只取前面的（与选择上限一致）', () => {
    const many = Array.from({ length: MAX_FORWARD_MESSAGES + 5 }, (_, i) =>
      msg({ messageId: `m${i}`, content: `第${i}条` }))

    const parsed = JSON.parse(buildForwardBundle(many))

    expect(parsed.items).toHaveLength(MAX_FORWARD_MESSAGES)
  })

  it('单条内容超长时截断到上限（服务端会拒收更长内容）', () => {
    const parsed = JSON.parse(buildForwardBundle([msg({ content: 'A'.repeat(MAX_FORWARD_ITEM_LENGTH + 100) })]))

    expect(parsed.items[0].c).toHaveLength(MAX_FORWARD_ITEM_LENGTH)
  })

  it('昵称去空白并截到 64 字', () => {
    const parsed = JSON.parse(buildForwardBundle([msg({ senderName: `  ${'名'.repeat(80)}  ` })]))

    expect(parsed.items[0].n).toHaveLength(64)
    expect(parsed.items[0].n.startsWith('名')).toBe(true)
  })
})

describe('parseForwardBundle', () => {
  it('解析打包结果（round-trip）', () => {
    const json = buildForwardBundle([msg({ content: '你好', messageType: 3 })], 1_800_000_000_000)

    const items = parseForwardBundle(json)

    expect(items).toHaveLength(1)
    expect(items![0]).toMatchObject({ n: '张三', c: '你好', t: 3, ts: 1_700_000_000_000 })
  })

  it.each([
    ['空串', ''],
    ['非 JSON', 'not json'],
    ['不是对象', '"str"'],
    ['没有 items', '{"v":1}'],
    ['items 不是数组', '{"items":{}}'],
    ['items 为空', '{"items":[]}']
  ])('载荷损坏时返回 null（%s）', (_name, content) => {
    expect(parseForwardBundle(content)).toBeNull()
  })

  it('字段类型不对时按缺省值收敛，不让界面崩', () => {
    const items = parseForwardBundle('{"items":[{"t":9,"n":123,"c":null}]}')

    expect(items).toHaveLength(1)
    expect(items![0]).toEqual({ n: '', c: '', t: 0, ts: 0 })
  })
})

describe('单行预览', () => {
  it('聊天记录在会话列表 / 搜索结果里显示为占位文案', () => {
    expect(messagePreview('{"items":[{"c":"x"}]}', 4)).toBe('[聊天记录]')
  })

  it('Markdown 抹掉记号，普通文字原样返回', () => {
    expect(messagePreview('# 标题 **粗**', 3)).not.toContain('#')
    expect(messagePreview('普通文字', 0)).toBe('普通文字')
  })

  it('卡片行摘要：图片显示占位、Markdown 抹记号、超长截断', () => {
    expect(forwardItemPreview({ n: '张三', c: '/uploads/a.png', t: 1, ts: 0 })).toBe('[图片]')
    expect(forwardItemPreview({ n: '张三', c: '# 标题', t: 3, ts: 0 })).toBe('标题')
    expect(forwardItemPreview({ n: '张三', c: 'A'.repeat(80), t: 0, ts: 0 }, 10)).toBe('AAAAAAAAAA…')
  })
})

describe('批量复制', () => {
  it('每条一行，带发送者标注', () => {
    const text = buildBatchCopyText([
      msg({ senderName: '张三', content: '第一句' }),
      msg({ senderName: '我', content: '第二句' })
    ])

    expect(text).toBe('[张三] 第一句\n[我] 第二句')
  })

  it('撤回的消息标注出来，不复制原文', () => {
    const text = buildBatchCopyText([msg({ content: '机密原文', isDeleted: true })])

    expect(text).toContain('（消息已撤回）')
    expect(text).not.toContain('机密原文')
  })
})

describe('上限常量与服务端对齐', () => {
  it('条数与单条长度就是服务端 ForwardBundle 的值', () => {
    // 改这里必须同时改 backend/.../ForwardBundle.cs（服务端会拒收超限载荷）
    expect(MAX_FORWARD_MESSAGES).toBe(20)
    expect(MAX_FORWARD_ITEM_LENGTH).toBe(2000)
  })
})
