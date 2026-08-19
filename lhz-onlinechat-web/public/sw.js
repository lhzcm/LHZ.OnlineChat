/* OnlineChat Service Worker：运行时缓存（可安装 PWA + 基础离线能力） */
const VERSION = 'v1'

// 安装即接管（配合 skipWaiting 让新版本立即生效）
self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const keys = await caches.keys()
      await Promise.all(keys.filter(k => k !== VERSION).map(k => caches.delete(k)))
      await self.clients.claim()
    })()
  )
})

self.addEventListener('fetch', (event) => {
  const req = event.request
  if (req.method !== 'GET') return
  const url = new URL(req.url)
  if (url.origin !== self.location.origin) return

  // 页面导航：网络优先，离线回退缓存的首页（保证打开可用）
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req)
        .then(res => {
          const copy = res.clone()
          caches.open(VERSION).then(c => c.put('/index.html', copy))
          return res
        })
        .catch(() => caches.match('/index.html'))
    )
    return
  }

  // 静态资源（/assets/* 与图标）：缓存优先（stale-while-revalidate）
  if (url.pathname.startsWith('/assets/') || url.pathname.startsWith('/icons/') || url.pathname.endsWith('/manifest.webmanifest')) {
    event.respondWith(
      caches.match(req).then(cached => {
        const network = fetch(req).then(res => {
          if (res.ok) {
            const copy = res.clone()
            caches.open(VERSION).then(c => c.put(req, copy))
          }
          return res
        }).catch(() => cached)
        return cached || network
      })
    )
    return
  }

  // 其余（/api、/uploads、/ws 等）不缓存，直接走网络
})
