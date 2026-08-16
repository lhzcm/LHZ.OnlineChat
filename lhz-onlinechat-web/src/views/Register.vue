<template>
  <div class="login-page">
    <div class="login-card">
      <h1>创建账号</h1>
      <p class="subtitle">加入 OnlineChat</p>
      <form @submit.prevent="handleRegister">
        <input v-model="nickname" class="input" placeholder="昵称" required />
        <input v-model="username" class="input" placeholder="用户名" required />
        <input v-model="password" class="input" type="password" placeholder="密码 (至少6位)" required />
        <input v-model="confirmPassword" class="input" type="password" placeholder="确认密码" required />
        <button type="submit" class="btn btn-primary" :disabled="loading">
          {{ loading ? '注册中...' : '注册' }}
        </button>
        <p class="error" v-if="error">{{ error }}</p>
      </form>
      <p class="link">
        已有账号？<router-link to="/login">立即登录</router-link>
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const auth = useAuthStore()

const username = ref('')
const password = ref('')
const confirmPassword = ref('')
const nickname = ref('')
const loading = ref(false)
const error = ref('')

async function handleRegister() {
  if (password.value !== confirmPassword.value) {
    error.value = '两次密码不一致'
    return
  }
  if (password.value.length < 6) {
    error.value = '密码至少6位'
    return
  }
  loading.value = true
  error.value = ''
  try {
    const res = await auth.register({
      username: username.value,
      password: password.value,
      nickname: nickname.value
    })
    if (res.success) {
      router.push('/login')
    } else {
      error.value = res.message
    }
  } catch (e: any) {
    error.value = e?.message || '注册失败'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-page {
  height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
}
.login-card {
  background: white;
  padding: 40px;
  border-radius: 16px;
  box-shadow: 0 20px 60px rgba(0,0,0,0.15);
  width: 380px;
}
.login-card h1 { text-align: center; color: var(--primary); font-size: 28px; }
.subtitle { text-align: center; color: var(--text-secondary); margin-bottom: 24px; }
.input { margin-bottom: 14px; }
.btn { width: 100%; margin-top: 8px; padding: 12px; }
.error { color: var(--danger); text-align: center; margin-top: 12px; font-size: 13px; }
.link { text-align: center; margin-top: 20px; font-size: 14px; color: var(--text-secondary); }
.link a { color: var(--primary); text-decoration: none; }
</style>
