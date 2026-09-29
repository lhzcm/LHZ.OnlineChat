<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { adminApi } from '@/api/admin'
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
      localStorage.setItem('adminToken', res.data.token)
      localStorage.setItem('adminInfo', JSON.stringify(res.data.admin))
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
