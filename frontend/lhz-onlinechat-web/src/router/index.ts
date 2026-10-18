import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'Login',
      component: () => import('@/views/Login.vue'),
      meta: { guest: true }
    },
    {
      path: '/register',
      name: 'Register',
      component: () => import('@/views/Register.vue'),
      meta: { guest: true }
    },
    {
      path: '/forgot-password',
      name: 'ForgotPassword',
      component: () => import('@/views/ForgotPassword.vue'),
      meta: { guest: true }
    },
    {
      path: '/chat',
      name: 'Chat',
      component: () => import('@/views/ChatLayout.vue'),
      meta: { requiresAuth: true }
    },
    {
      // 群邀请链接：必须登录后才能看到群资料（匿名接口会把群信息暴露给任何拿到链接的人）
      path: '/join/:token',
      name: 'JoinGroup',
      component: () => import('@/views/JoinGroup.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/',
      redirect: '/chat'
    }
  ]
})

/**
 * 只接受站内相对路径的 redirect 参数：直接信任 `?redirect=` 会被拿来做开放重定向
 * （`/login?redirect=https://evil.example`）。
 */
export function safeRedirect(value: unknown): string | null {
  if (typeof value !== 'string') return null
  if (!value.startsWith('/') || value.startsWith('//')) return null
  return value
}

// 路由守卫
router.beforeEach((to, _from, next) => {
  const token = localStorage.getItem('token')
  if (to.meta.requiresAuth && !token) {
    // 带上原目标：扫码进来的人登录完成后要回到邀请页，而不是聊天页
    next({ path: '/login', query: { redirect: to.fullPath } })
  } else if (to.meta.guest && token) {
    next('/chat')
  } else {
    next()
  }
})

export default router
