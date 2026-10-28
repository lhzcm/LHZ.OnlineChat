import { messageApi } from '@/api/message'
import type { FileMeta } from '@/types'

/**
 * 文件元数据（原始文件名 + 大小）解析器。
 *
 * 消息 content 只存 URL（与图片/语音一致），文件名/大小在服务端元数据表里。
 * 一屏可能有多个文件气泡，如果每个气泡各发一次请求就是 N+1 ——
 * 这里做成「同一 tick 内收集 → 合并成一次批量请求」，并带进程内缓存。
 *
 * 取不到（历史数据/记录缺失）时返回 null，由组件用 URL 兜底展示，不阻塞渲染。
 */
const cache = new Map<string, FileMeta>()
const pending = new Set<string>()
const waiters = new Map<string, Array<(meta: FileMeta | null) => void>>()
let flushTimer: number | null = null

/** 单次批量请求上限（与服务端 GetFileMetaQuery.MaxUrls 一致） */
const BATCH_SIZE = 50

const LOCAL_FILE = /^\/uploads\/files\/[A-Za-z0-9._-]+$/

function isLocalFileUrl(url: string): boolean {
  return LOCAL_FILE.test(url)
}

function resolveWaiters(url: string, meta: FileMeta | null) {
  const list = waiters.get(url)
  if (!list) return
  waiters.delete(url)
  for (const resolve of list) resolve(meta)
}

async function flush() {
  flushTimer = null

  const urls = [...pending].filter(isLocalFileUrl)
  pending.clear()

  if (urls.length === 0) return

  // 超过上限分批（正常一屏远小于 50，这里只是兜底）
  for (let i = 0; i < urls.length; i += BATCH_SIZE) {
    const batch = urls.slice(i, i + BATCH_SIZE)
    try {
      const res = await messageApi.getFileMeta(batch)
      const found = new Map<string, FileMeta>()
      if (res.success && res.data) {
        for (const item of res.data) found.set(item.url, item)
      }
      for (const url of batch) {
        const meta = found.get(url) ?? null
        if (meta) cache.set(url, meta)
        resolveWaiters(url, meta)
      }
    } catch {
      // 网络失败：按"取不到"处理，组件会用 URL 兜底，不把错误抛给渲染层
      for (const url of batch) resolveWaiters(url, null)
    }
  }
}

/** 取单个文件的元数据（命中缓存直接返回；否则合并到下一批请求） */
export function resolveFileMeta(url: string): Promise<FileMeta | null> {
  const cached = cache.get(url)
  if (cached) return Promise.resolve(cached)
  if (!isLocalFileUrl(url)) return Promise.resolve(null)

  return new Promise<FileMeta | null>(resolve => {
    const list = waiters.get(url)
    if (list) list.push(resolve)
    else waiters.set(url, [resolve])

    pending.add(url)
    if (flushTimer === null) {
      // 50ms 足够收集同一屏渲染出来的多个气泡，又不会让用户察觉延迟
      flushTimer = window.setTimeout(() => { void flush() }, 50)
    }
  })
}

/** 已经拿到的元数据（同步读；组件初始化时先用它渲染，避免闪一下占位） */
export function cachedFileMeta(url: string): FileMeta | null {
  return cache.get(url) ?? null
}

/** 把上传响应里的名字/大小直接写入缓存（发送方自己刚上传完，没必要再问一次） */
export function rememberFileMeta(meta: FileMeta): void {
  cache.set(meta.url, meta)
}

/** 测试用：清空缓存与在途队列 */
export function resetFileMetaCache(): void {
  cache.clear()
  pending.clear()
  waiters.clear()
  if (flushTimer !== null) {
    clearTimeout(flushTimer)
    flushTimer = null
  }
}
