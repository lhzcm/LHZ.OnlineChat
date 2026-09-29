<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { authApi } from '@/api/auth'
import type { SessionInfoDto } from '@/types'

const emit = defineEmits<{ close: [] }>()

const sessions = ref<SessionInfoDto[]>([])
const loading = ref(true)
const error = ref('')
const success = ref('')

/** 当前设备（后端标记 isCurrent） */
const currentSessionId = ref('')

onMounted(async () => {
  await load()
})

async function load() {
  loading.value = true
  error.value = ''
  try {
    const res = await authApi.getSessions()
    if (res.success) {
      sessions.value = res.data || []
      currentSessionId.value = sessions.value.find(s => s.isCurrent)?.sessionId || ''
    } else {
      error.value = res.message
    }
  } catch (e: any) {
    error.value = e?.message || '加载失败'
  } finally {
    loading.value = false
  }
}

function formatTime(ts: number): string {
  if (!ts) return '—'
  const d = new Date(ts)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}

async function kick(s: SessionInfoDto) {
  if (s.isCurrent) {
    // 退出当前设备 = 退出登录
    if (!window.confirm('确定退出当前设备？将退出登录')) return
    localStorage.removeItem('token')
    localStorage.removeItem('refreshToken')
    window.location.href = '/login'
    return
  }
  if (!window.confirm(`确定让「${s.deviceName}」下线？`)) return
  error.value = ''
  success.value = ''
  try {
    const res = await authApi.kickSession(s.sessionId)
    if (res.success) {
      success.value = `已让「${s.deviceName}」下线`
      await load()
    } else {
      error.value = res.message
    }
  } catch (e: any) {
    error.value = e?.message || '操作失败'
  }
}

async function logoutOthers() {
  const others = sessions.value.filter(s => !s.isCurrent)
  if (others.length === 0) {
    error.value = '没有其他在线设备'
    return
  }
  if (!window.confirm(`确定退出其他 ${others.length} 台设备？`)) return
  error.value = ''
  success.value = ''
  try {
    const res = await authApi.logoutOthers()
    if (res.success) {
      success.value = res.message
      await load()
    } else {
      error.value = res.message
    }
  } catch (e: any) {
    error.value = e?.message || '操作失败'
  }
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>登录设备</h3>
      <p class="session-desc">同一账号可多台设备同时在线，此处可管理各设备的登录状态</p>

      <div class="session-list" v-if="!loading">
        <div v-for="s in sessions" :key="s.sessionId" class="session-item">
          <div class="session-info">
            <div class="session-name">
              {{ s.deviceName }}
              <span class="session-tag" v-if="s.isCurrent">当前设备</span>
            </div>
            <div class="session-meta">
              <span v-if="s.ip">IP {{ s.ip }} · </span>
              登录于 {{ formatTime(s.createdAt) }}<template v-if="s.lastActiveAt"> · 最近活跃 {{ formatTime(s.lastActiveAt) }}</template>
            </div>
          </div>
          <button class="btn btn-sm session-kick" :class="{ danger: s.isCurrent }" @click="kick(s)">
            {{ s.isCurrent ? '退出登录' : '退出' }}
          </button>
        </div>
        <p class="empty" v-if="sessions.length === 0">暂无会话</p>
      </div>
      <p class="empty" v-else>{{ loading ? '加载中…' : '' }}</p>

      <button class="btn btn-ghost btn-block" :disabled="loading" @click="logoutOthers">退出其他所有设备</button>

      <p class="modal-error" v-if="error">{{ error }}</p>
      <p class="modal-success" v-if="success">{{ success }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>

<style scoped>
.session-desc {
  font-size: 12.5px;
  color: var(--text-secondary);
  margin-bottom: 12px;
}

.session-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: 300px;
  overflow-y: auto;
  margin-bottom: 12px;
}

.session-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 10px;
  background: var(--bg-white);
}

.session-info {
  flex: 1;
  min-width: 0;
}

.session-name {
  font-size: 14px;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 8px;
}

.session-tag {
  font-size: 11px;
  color: var(--primary);
  background: var(--active-bg);
  border: 1px solid var(--primary-light);
  padding: 1px 8px;
  border-radius: 20px;
  flex-shrink: 0;
}

.session-meta {
  font-size: 12px;
  color: var(--text-secondary);
  margin-top: 2px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.session-kick {
  flex-shrink: 0;
  border: 1px solid var(--border);
  background: var(--bg-hover);
  color: var(--text-secondary);
}

.session-kick:hover {
  border-color: var(--danger);
  color: var(--danger);
}

.session-kick.danger {
  color: var(--danger);
  border-color: var(--danger-light, var(--danger));
}

.btn-block {
  width: 100%;
  margin-bottom: 10px;
}

.empty {
  text-align: center;
  color: var(--text-secondary);
  font-size: 13px;
  padding: 12px 0;
}
</style>
