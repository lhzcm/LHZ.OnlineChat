import { beforeEach, describe, expect, it, vi } from 'vitest'
import { messageApi } from '@/api/message'
import {
  cachedFileMeta,
  rememberFileMeta,
  resetFileMetaCache,
  resolveFileMeta
} from '@/utils/fileMeta'

/**
 * 文件元数据的批量解析。
 * 关键点：一屏多个文件气泡只发**一次**请求（否则就是 N+1）、命中缓存不再请求、
 * 失败时返回 null 让组件用 URL 兜底而不是把错误抛给渲染层。
 */
const A = '/uploads/files/a.pdf'
const B = '/uploads/files/b.zip'

const meta = (url: string, name: string) => ({ url, name, size: 1024 })

beforeEach(() => {
  resetFileMetaCache()
  vi.restoreAllMocks()
})

describe('resolveFileMeta', () => {
  it('同一轮多个 URL 合并成一次批量请求', async () => {
    const spy = vi.spyOn(messageApi, 'getFileMeta').mockResolvedValue({
      success: true,
      data: [meta(A, 'a.pdf'), meta(B, 'b.zip')]
    } as never)

    const [a, b] = await Promise.all([resolveFileMeta(A), resolveFileMeta(B)])

    expect(a?.name).toBe('a.pdf')
    expect(b?.name).toBe('b.zip')
    expect(spy).toHaveBeenCalledTimes(1)
    expect(spy.mock.calls[0][0].sort()).toEqual([A, B])
  })

  it('命中缓存不再请求', async () => {
    rememberFileMeta(meta(A, '缓存的.pdf'))
    const spy = vi.spyOn(messageApi, 'getFileMeta')

    const result = await resolveFileMeta(A)

    expect(result?.name).toBe('缓存的.pdf')
    expect(spy).not.toHaveBeenCalled()
    expect(cachedFileMeta(A)?.name).toBe('缓存的.pdf')
  })

  it('服务端没返回记录时解析为 null（组件用 URL 兜底）', async () => {
    vi.spyOn(messageApi, 'getFileMeta').mockResolvedValue({ success: true, data: [] } as never)

    await expect(resolveFileMeta(A)).resolves.toBeNull()
    expect(cachedFileMeta(A)).toBeNull()
  })

  it('请求异常时不抛错，按取不到处理', async () => {
    vi.spyOn(messageApi, 'getFileMeta').mockRejectedValue(new Error('network'))

    await expect(resolveFileMeta(A)).resolves.toBeNull()
  })

  it('非站内文件地址直接返回 null，不发请求', async () => {
    const spy = vi.spyOn(messageApi, 'getFileMeta')

    await expect(resolveFileMeta('https://evil.example/a.pdf')).resolves.toBeNull()
    expect(spy).not.toHaveBeenCalled()
  })

  it('超过单批上限时分批请求', async () => {
    const urls = Array.from({ length: 55 }, (_, i) => `/uploads/files/f${i}.pdf`)
    const spy = vi.spyOn(messageApi, 'getFileMeta').mockResolvedValue({ success: true, data: [] } as never)

    await Promise.all(urls.map(url => resolveFileMeta(url)))

    expect(spy).toHaveBeenCalledTimes(2)
    expect((spy.mock.calls[0][0] as string[]).length).toBe(50)
    expect((spy.mock.calls[1][0] as string[]).length).toBe(5)
  })
})
