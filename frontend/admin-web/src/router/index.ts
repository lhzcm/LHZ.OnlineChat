import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory('/admin/'),
  routes: [
    {
      path: '/login',
      name: 'Login',
      component: () => import('@/views/Login.vue'),
      meta: { guest: true }
    },
    {
      path: '/',
      component: () => import('@/views/Layout.vue'),
      meta: { requiresAuth: true },
      children: [
        { path: '', redirect: '/dashboard' },
        {
          path: 'dashboard',
          name: 'Dashboard',
          component: () => import('@/views/Dashboard.vue')
        },
        {
          path: 'users',
          name: 'Users',
          component: () => import('@/views/Users.vue')
        },
        {
          path: 'groups',
          name: 'Groups',
          component: () => import('@/views/Groups.vue')
        },
        {
          path: 'messages',
          name: 'Messages',
          component: () => import('@/views/Messages.vue')
        },
        {
          path: 'robots',
          name: 'Robots',
          component: () => import('@/views/Robots.vue')
        },
        {
          path: 'admins',
          name: 'Admins',
          component: () => import('@/views/Admins.vue'),
          meta: { superOnly: true }
        },
        {
          path: 'logs',
          name: 'Logs',
          component: () => import('@/views/Logs.vue'),
          meta: { superOnly: true }
        }
      ]
    }
  ]
})

router.beforeEach((to, _from, next) => {
  const token = localStorage.getItem('adminToken')
  if (to.meta.requiresAuth && !token) {
    next('/login')
  } else if (to.meta.guest && token) {
    next('/dashboard')
  } else {
    next()
  }
})

export default router
