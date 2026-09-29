<template>
  <div class="login-page">
    <div class="login-card">
      <div class="login-logo">💬</div>
      <h1>OnlineChat</h1>
      <p class="subtitle">使用账号 ID 或邮箱登录</p>
      <form @submit.prevent="handleLogin">
        <input v-model="account" class="input" type="text" placeholder="账号 ID 或邮箱" required />
        <input v-model="password" class="input" type="password" placeholder="密码" required />
        <button type="submit" class="btn btn-primary" :disabled="loading">
          {{ loading ? '登录中...' : '登 录' }}
        </button>
        <p class="error" v-if="error">{{ error }}</p>
      </form>
      <p class="link">
        还没有账号？<router-link to="/register">立即注册</router-link>
        <span class="link-sep">·</span>
        <router-link to="/forgot-password">忘记密码？</router-link>
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useWebSocketStore } from '@/stores/websocket'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const ws = useWebSocketStore()

const account = ref('')
const password = ref('')
const loading = ref(false)
const error = ref('')

onMounted(() => {
  // 注册成功后跳转携带的账号预填
  const q = route.query.account
  if (q && typeof q === 'string') account.value = q
})

async function handleLogin() {
  const acc = account.value.trim()
  if (!acc) {
    error.value = '请输入账号 ID 或邮箱'
    return
  }
  loading.value = true
  error.value = ''
  try {
    const res = await auth.login({ account: acc, password: password.value })
    if (res.success) {
      ws.connect(auth.token)
      router.push('/chat')
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

<style scoped>
.login-page {
  height: 100vh;
  height: 100dvh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #5b6cff 0%, #764ba2 100%);
  position: relative;
  overflow: hidden;
}

.login-page::before,
.login-page::after {
  content: '';
  position: absolute;
  border-radius: 50%;
}

.login-page::before {
  width: 440px;
  height: 440px;
  background: rgba(255, 255, 255, 0.08);
  top: -140px;
  right: -120px;
}

.login-page::after {
  width: 320px;
  height: 320px;
  background: rgba(255, 255, 255, 0.06);
  bottom: -110px;
  left: -90px;
}

.login-card {
  position: relative;
  z-index: 1;
  background: rgba(255, 255, 255, 0.96);
  backdrop-filter: blur(10px);
  padding: 40px 36px;
  border-radius: 20px;
  box-shadow: 0 24px 64px rgba(31, 35, 41, 0.18);
  width: 380px;
  max-width: calc(100vw - 40px);
  animation: modal-in 0.3s;
}

.login-logo {
  width: 64px;
  height: 64px;
  margin: 0 auto 16px;
  border-radius: 18px;
  background: linear-gradient(135deg, #5b6cff, #7c5cff);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 32px;
  box-shadow: 0 10px 24px rgba(91, 108, 255, 0.4);
}

.login-card h1 {
  text-align: center;
  font-size: 28px;
  background: linear-gradient(135deg, #5b6cff, #7c5cff);
  -webkit-background-clip: text;
  background-clip: text;
  color: transparent;
}

.subtitle {
  text-align: center;
  color: var(--text-secondary);
  margin-bottom: 24px;
  font-size: 13px;
}

.input {
  margin-bottom: 14px;
}

.btn {
  width: 100%;
  margin-top: 8px;
  padding: 12px;
  border-radius: 12px;
  background: linear-gradient(135deg, #5b6cff, #7c5cff);
  letter-spacing: 2px;
}

.btn:hover {
  filter: brightness(1.06);
}

.error {
  color: var(--danger);
  text-align: center;
  margin-top: 12px;
  font-size: 13px;
}

.link {
  text-align: center;
  margin-top: 20px;
  font-size: 14px;
  color: var(--text-secondary);
}

.link a {
  color: var(--primary);
  text-decoration: none;
  font-weight: 500;
}

.link-sep {
  margin: 0 6px;
  color: var(--border);
}

/* 深色模式适配 */
html[data-theme='dark'] .login-card {
  background: rgba(30, 34, 43, 0.96);
}
</style>
