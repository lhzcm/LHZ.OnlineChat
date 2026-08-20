<script setup lang="ts">
import { useRouter, useRoute } from 'vue-router'
import { computed, onMounted, ref } from 'vue'
import { useToast } from '@/composables/useToast'

const router = useRouter()
const route = useRoute()
const { toastMsg, toast } = useToast()

const admin = ref<{ id: number; username: string; role: number } | null>(null)
const isSuper = computed(() => admin.value?.role === 0)

onMounted(() => {
  try {
    admin.value = JSON.parse(localStorage.getItem('adminInfo') || 'null')
  } catch { admin.value = null }
})

function logout() {
  localStorage.removeItem('adminToken')
  localStorage.removeItem('adminInfo')
  router.push('/login')
}

const navs = computed(() => {
  const items = [
    { path: '/dashboard', label: '📊 仪表盘' },
    { path: '/users', label: '👥 用户管理' }
  ]
  if (isSuper.value) {
    items.push({ path: '/admins', label: '🛡️ 管理员' })
    items.push({ path: '/logs', label: '📜 审计日志' })
  }
  return items
})
</script>

<template>
  <div class="admin-layout">
    <aside class="sidebar">
      <div class="sidebar-logo">🛡️ OnlineChat 管理</div>
      <nav class="sidebar-nav">
        <router-link v-for="n in navs" :key="n.path" :to="n.path" class="nav-item"
          :class="{ active: route.path === n.path }">
          {{ n.label }}
        </router-link>
      </nav>
      <div class="sidebar-footer">
        <div>{{ admin?.username || '管理员' }}<span v-if="isSuper">（超管）</span></div>
        <button class="btn btn-sm" style="margin-top: 8px" @click="logout">退出登录</button>
      </div>
    </aside>
    <main class="main-area">
      <router-view />
    </main>
    <transition name="toast-fade">
      <div class="toast" v-if="toastMsg">{{ toastMsg }}</div>
    </transition>
  </div>
</template>

<style>
.toast-fade-enter-active, .toast-fade-leave-active { transition: all 0.25s; }
.toast-fade-enter-from, .toast-fade-leave-to { opacity: 0; transform: translateX(-50%) translateY(-10px); }
</style>
