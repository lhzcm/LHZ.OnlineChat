<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { adminApi, type AdminInfo } from '@/api/admin'
import { useToast } from '@/composables/useToast'

const router = useRouter()
const { toast } = useToast()
const username = ref('')
const password = ref('')
const loading = ref(false)
const error = ref('')

async function handleLogin() {
  if (!username.value.trim() || !password.value) {
    error.value = '请输入账号和密码'
    return
  }
  loading.value = true
  error.value = ''
  try {
    const res = await adminApi.login(username.value.trim(), password.value)
    if (res.success && res.data) {
      // refreshToken 目前后端未下发（AdminLoginResponse 只有 token），
      // 一旦下发则一并保存，供 request.ts 在 401 时静默刷新
      const data = res.data as { token: string; admin: AdminInfo; refreshToken?: string }
      localStorage.setItem('adminToken', data.token)
      if (data.refreshToken) localStorage.setItem('adminRefreshToken', data.refreshToken)
      localStorage.setItem('adminInfo', JSON.stringify(data.admin))
      toast('登录成功')
      router.push('/dashboard')
    } else {
      error.value = res.message
    }
  } catch (e: any) {
    error.value = e?.message || '登录失败'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-page">
    <div class="login-card">
      <h1>🛡️ OnlineChat 管理后台</h1>
      <p class="sub">管理员登录</p>
      <form @submit.prevent="handleLogin">
        <input v-model="username" class="input" placeholder="管理员账号" autocomplete="username" />
        <input v-model="password" class="input" type="password" placeholder="密码" autocomplete="current-password" />
        <button type="submit" class="btn btn-primary btn-block" :disabled="loading">
          {{ loading ? '登录中…' : '登 录' }}
        </button>
      </form>
      <p class="error-text" v-if="error">{{ error }}</p>
    </div>
  </div>
</template>
