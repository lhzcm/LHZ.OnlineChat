<template>
  <div class="login-page">
    <div class="login-card">
      <div class="login-logo">🔑</div>
      <h1>重置密码</h1>
      <p class="subtitle">通过注册邮箱验证码重置密码</p>
      <form @submit.prevent="handleReset">
        <input v-model="email" class="input" type="email" placeholder="注册邮箱" required />
        <div class="code-row">
          <button type="button" class="btn code-btn" :disabled="counting > 0 || !email || sending" @click="sendCode">
            {{ sending ? '发送中…' : counting > 0 ? `${counting}s 后重发` : '获取验证码' }}
          </button>
          <span class="code-hint">验证码将发送至该邮箱，5 分钟内有效</span>
        </div>
        <input v-model="code" class="input" placeholder="6 位数字验证码" required inputmode="numeric" maxlength="6" />
        <input v-model="password" class="input" type="password" placeholder="新密码 (至少6位)" required />
        <input v-model="confirmPassword" class="input" type="password" placeholder="确认新密码" required />
        <button type="submit" class="btn btn-primary" :disabled="loading">
          {{ loading ? '提交中...' : '重置密码' }}
        </button>
        <p class="error" v-if="error">{{ error }}</p>
        <p class="info" v-if="info">{{ info }}</p>
        <p class="dev-tip" v-if="devCode">开发模式验证码：<b>{{ devCode }}</b>（未配置 SMTP 时显示）</p>
      </form>
      <p class="link">
        想起来了？<router-link to="/login">返回登录</router-link>
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { authApi } from '@/api/auth'

const router = useRouter()

const email = ref('')
const code = ref('')
const password = ref('')
const confirmPassword = ref('')
const loading = ref(false)
const sending = ref(false)
const counting = ref(0)
const error = ref('')
const info = ref('')
const devCode = ref('')
// 验证码已发送到的邮箱（用于检测用户更换邮箱）
const sentEmail = ref('')
let countTimer: number | null = null

// 邮箱变更后，旧验证码失效：清空验证码输入并提示重新获取
watch(email, (val) => {
  if (sentEmail.value && val.trim().toLowerCase() !== sentEmail.value) {
    code.value = ''
    devCode.value = ''
    sentEmail.value = ''
    error.value = ''
    info.value = '邮箱已变更，请重新获取验证码'
  }
})

onUnmounted(() => {
  if (countTimer) clearInterval(countTimer)
})

function startCountdown() {
  counting.value = 60
  countTimer = window.setInterval(() => {
    counting.value--
    if (counting.value <= 0 && countTimer) {
      clearInterval(countTimer)
      countTimer = null
    }
  }, 1000)
}

async function sendCode() {
  if (!email.value || counting.value > 0 || sending.value) return
  sending.value = true
  error.value = ''
  info.value = ''
  devCode.value = ''
  try {
    // purpose=forgot：服务端校验该邮箱已注册（未注册不发码）
    const res = await authApi.sendCode({ email: email.value.trim(), purpose: 'forgot' })
    if (res.success) {
      sentEmail.value = email.value.trim().toLowerCase()
      info.value = `验证码已发送至 ${email.value.trim()}，5 分钟内有效`
      if (res.data?.devCode) devCode.value = res.data.devCode
      startCountdown()
    } else {
      error.value = res.message
    }
  } catch (e: any) {
    error.value = e?.message || '验证码发送失败'
  } finally {
    sending.value = false
  }
}

async function handleReset() {
  if (password.value !== confirmPassword.value) {
    error.value = '两次输入的密码不一致'
    return
  }
  if (password.value.length < 6) {
    error.value = '密码至少 6 位'
    return
  }
  loading.value = true
  error.value = ''
  try {
    const res = await authApi.forgotPassword({
      email: email.value.trim(),
      code: code.value.trim(),
      newPassword: password.value
    })
    if (res.success) {
      // 重置成功：跳转登录页并预填邮箱
      router.push({ path: '/login', query: { account: email.value.trim() } })
    } else {
      error.value = res.message
    }
  } catch (e: any) {
    error.value = e?.message || '密码重置失败'
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

.code-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 14px;
}

.code-btn {
  width: auto;
  margin: 0;
  padding: 10px 16px;
  font-size: 13px;
  letter-spacing: 0;
  flex-shrink: 0;
  border-radius: 10px;
  background: var(--bg-hover);
  color: var(--primary);
  border: 1px solid var(--border);
}

.code-btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.code-hint {
  font-size: 12px;
  color: var(--text-secondary);
}

.error {
  color: var(--danger);
  text-align: center;
  margin-top: 12px;
  font-size: 13px;
}

.info {
  color: var(--success, #22c55e);
  text-align: center;
  margin-top: 10px;
  font-size: 13px;
}

.dev-tip {
  text-align: center;
  margin-top: 10px;
  font-size: 12px;
  color: var(--warning, #f59e0b);
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

/* 深色模式适配 */
html[data-theme='dark'] .login-card {
  background: rgba(30, 34, 43, 0.96);
}
</style>
