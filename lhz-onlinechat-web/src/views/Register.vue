<template>
  <div class="login-page">
    <div class="login-card">
      <div class="login-logo">✨</div>
      <h1>创建账号</h1>
      <p class="subtitle">注册成功后将自动分配账号 ID</p>
      <form @submit.prevent="handleRegister">
        <input v-model="nickname" class="input" placeholder="昵称（可重复）" required />
        <input v-model="email" class="input" type="email" placeholder="邮箱地址" required />
        <div class="code-row">
          <button type="button" class="btn code-btn" :disabled="counting > 0 || !email || sending" @click="sendCode">
            {{ sending ? '发送中…' : counting > 0 ? `${counting}s 后重发` : '获取验证码' }}
          </button>
          <span class="code-hint">验证码将发送至该邮箱，5 分钟内有效</span>
        </div>
        <input v-model="code" class="input" placeholder="6 位数字验证码" required inputmode="numeric" maxlength="6" />
        <input v-model="password" class="input" type="password" placeholder="密码 (至少6位)" required />
        <input v-model="confirmPassword" class="input" type="password" placeholder="确认密码" required />
        <button type="submit" class="btn btn-primary" :disabled="loading">
          {{ loading ? '注册中...' : '注 册' }}
        </button>
        <p class="error" v-if="error">{{ error }}</p>
        <p class="info" v-if="info">{{ info }}</p>
        <p class="dev-tip" v-if="devCode">开发模式验证码：<b>{{ devCode }}</b>（未配置 SMTP 时显示）</p>
      </form>
      <p class="link">
        已有账号？<router-link to="/login">立即登录</router-link>
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { authApi } from '@/api/auth'

const router = useRouter()

const nickname = ref('')
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
    const res = await authApi.sendCode({ email: email.value.trim() })
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
    const res = await authApi.register({
      nickname: nickname.value.trim(),
      email: email.value.trim(),
      code: code.value.trim(),
      password: password.value
    })
    if (res.success) {
      // 注册成功：提示账号 ID 并跳转登录（预填账号）
      alert(`🎉 注册成功！\n\n你的账号是：${res.data?.accountId}\n请牢记账号，登录时使用。`)
      router.push({ path: '/login', query: { account: String(res.data?.accountId ?? '') } })
    } else {
      error.value = res.message
    }
  } catch (e: any) {
    error.value = e?.message || '注册失败'
  } finally {
    loading.value = false
  }
}

onUnmounted(() => {
  if (countTimer) clearInterval(countTimer)
})
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
  padding: 32px 36px;
  border-radius: 20px;
  box-shadow: 0 24px 64px rgba(31, 35, 41, 0.18);
  width: 400px;
  max-width: calc(100vw - 40px);
  max-height: calc(100dvh - 40px);
  overflow-y: auto;
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
  margin-bottom: 20px;
  font-size: 13px;
}

.code-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-bottom: 14px;
}

.code-btn {
  width: 100%;
  padding: 10px;
  border-radius: 10px;
  background: var(--bg-hover);
  color: var(--primary);
  border: 1px solid var(--border);
  font-size: 14px;
  transition: all 0.2s;
}

.code-btn:hover:not(:disabled) {
  background: #eef1ff;
  border-color: var(--primary-light);
}

.code-btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.code-hint {
  font-size: 12px;
  color: var(--text-secondary);
  text-align: center;
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

.info {
  color: var(--success);
  text-align: center;
  margin-top: 10px;
  font-size: 12.5px;
}

.dev-tip {
  text-align: center;
  margin-top: 10px;
  font-size: 12.5px;
  color: var(--text-secondary);
  background: #f0f4ff;
  border: 1px dashed var(--primary-light);
  border-radius: 8px;
  padding: 6px 8px;
}

.dev-tip b {
  color: var(--primary);
  font-size: 15px;
  letter-spacing: 2px;
}

.link {
  text-align: center;
  margin-top: 16px;
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
