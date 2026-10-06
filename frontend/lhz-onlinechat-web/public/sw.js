/* OnlineChat Service Worker：运行时缓存（可安装 PWA + 基础离线能力） */
// 缓存版本：改动缓存策略或需要强制丢弃旧缓存时必须递增。
// activate 阶段会按前缀删除旧版本缓存，因此旧 SW 不会"静默"继续用过期资源。
const VERSION = 'v2'
const CACHE_NAME = `onlinechat-${VERSION}`
// 早期版本直接以版本号（如 'v1'）作为缓存名，升级时需要一并清掉
const LEGACY_CACHE_NAMES = ['v1']

// 安装即接管（配合 skipWaiting 让新版本立即生效）
self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const keys = await caches.keys()
      // 只清理本应用的缓存（前缀 + 已知旧名），避免误删同源下其他应用的缓存
      await Promise.all(
        keys
          .filter(k => (k.startsWith('onlinechat-') || LEGACY_CACHE_NAMES.includes(k)) && k !== CACHE_NAME)
          .map(k => caches.delete(k))
      )
      await self.clients.claim()
    })()
  )
})

/** 静态资源（带内容哈希，可放心缓存优先） */
function isStaticAsset(pathname) {
  return pathname.startsWith('/assets/') ||
    pathname.startsWith('/icons/') ||
    pathname.endsWith('/manifest.webmanifest')
}

self.addEventListener('fetch', (event) => {
  const req = event.request
  if (req.method !== 'GET') return
  const url = new URL(req.url)
  if (url.origin !== self.location.origin) return

  // 接口与上传文件不缓存：/api 是动态数据，/uploads 体积大且内容可能被替换
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/uploads/')) return

  // 页面导航：网络优先，离线回退缓存的首页
  if (req.mode === 'navigate') {
    event.respondWith(
      (async () => {
        try {
          const res = await fetch(req)
          // 只缓存成功的首页响应：错误响应（502/维护页等）若被写成 index.html，
          // 会污染离线缓存，导致之后所有离线导航都返回错误页。
          // /admin/ 是同源下的另一个应用，它的 HTML 同样不能写成 index.html。
          const isAdminApp = url.pathname === '/admin' || url.pathname.startsWith('/admin/')
          if (res.ok && !isAdminApp) {
            const cache = await caches.open(CACHE_NAME)
            await cache.put('/index.html', res.clone())
          }
          return res
        } catch {
          // 离线：优先回退缓存的首页；没有缓存时返回一个最简单的离线提示页
          const cached = await caches.match('/index.html')
          if (cached) return cached
          return new Response(
            '<!doctype html><meta charset="utf-8"><title>离线</title>' +
            '<p style="font-family:sans-serif;padding:24px">当前处于离线状态，请检查网络后重试。</p>',
            { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8' } }
          )
        }
      })()
    )
    return
  }

  // 静态资源：缓存优先 + 后台更新（stale-while-revalidate）
  if (isStaticAsset(url.pathname)) {
    event.respondWith(
      (async () => {
        const cache = await caches.open(CACHE_NAME)
        const cached = await cache.match(req)
        if (cached) {
          // 命中缓存立即返回，同时后台刷新，保证下次打开拿到新版本
          event.waitUntil(
            fetch(req)
              .then(res => { if (res.ok) return cache.put(req, res.clone()) })
              .catch(() => { /* 离线时保持旧缓存 */ })
          )
          return cached
        }
        try {
          const res = await fetch(req)
          // 同样只缓存成功响应，避免把 404/500 当成资源长期缓存
          if (res.ok) await cache.put(req, res.clone())
          return res
        } catch {
          // 离线且无缓存：返回错误响应而不是 undefined（respondWith 收到 undefined 会抛错）
          return Response.error()
        }
      })()
    )
    return
  }

  // 其余（/ws 等）不缓存，直接走网络
})
