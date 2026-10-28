import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { messageApi } from '@/api/message'
import FileMessage from '@/components/chat/messages/FileMessage.vue'
import { rememberFileMeta, resetFileMetaCache } from '@/utils/fileMeta'

/**
 * 文件气泡：名字/大小来自元数据缓存；取不到时用 URL 末段兜底；
 * 整行是带 download 的链接（浏览器据此用原始文件名另存）。
 */
const URL_A = '/uploads/files/abc123.pdf'

beforeEach(() => {
  resetFileMetaCache()
  vi.restoreAllMocks()
})

describe('FileMessage', () => {
  it('命中缓存时显示原始文件名与大小，并带 download 属性', () => {
    rememberFileMeta({ url: URL_A, name: '季度报告.pdf', size: 1536 })

    const wrapper = mount(FileMessage, { props: { url: URL_A } })

    const link = wrapper.find('a.msg-file')
    expect(wrapper.find('.file-name').text()).toBe('季度报告.pdf')
    expect(wrapper.find('.file-size').text()).toBe('1.5 KB')
    expect(wrapper.find('.file-ext').text()).toBe('PDF')
    expect(link.attributes('href')).toBe(URL_A)
    expect(link.attributes('download')).toBe('季度报告.pdf')
  })

  it('直接传入名字与大小时不再请求元数据', () => {
    const spy = vi.spyOn(messageApi, 'getFileMeta')

    const wrapper = mount(FileMessage, {
      props: { url: URL_A, name: '直传.pdf', size: 1024 }
    })

    expect(wrapper.find('.file-name').text()).toBe('直传.pdf')
    expect(wrapper.find('.file-size').text()).toBe('1 KB')
    expect(spy).not.toHaveBeenCalled()
  })

  it('元数据取不到时用 URL 末段兜底，不卡在加载态', async () => {
    vi.spyOn(messageApi, 'getFileMeta').mockResolvedValue({ success: true, data: [] } as never)

    const wrapper = mount(FileMessage, { props: { url: URL_A } })
    await new Promise(resolve => setTimeout(resolve, 80))
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.file-name').text()).toBe('abc123.pdf')
    expect(wrapper.find('.file-size').exists()).toBe(false)
    expect(wrapper.find('a.msg-file').classes()).not.toContain('loading')
  })

  it('紧凑模式用于转发明细', () => {
    rememberFileMeta({ url: URL_A, name: '季度报告.pdf', size: 1024 })

    const wrapper = mount(FileMessage, { props: { url: URL_A, compact: true } })

    expect(wrapper.find('a.msg-file').classes()).toContain('compact')
  })

  it('URL 变化时重新解析（列表复用组件实例）', async () => {
    rememberFileMeta({ url: URL_A, name: '旧文件.pdf', size: 1024 })
    const wrapper = mount(FileMessage, { props: { url: URL_A } })
    expect(wrapper.find('.file-name').text()).toBe('旧文件.pdf')

    await wrapper.setProps({ url: '/uploads/files/other.zip' })
    await wrapper.vm.$nextTick()

    // 新 URL 没有缓存：先显示 URL 末段，等元数据回来再替换
    expect(wrapper.find('.file-name').text()).toBe('other.zip')
  })
})
