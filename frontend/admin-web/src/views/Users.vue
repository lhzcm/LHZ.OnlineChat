<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { adminApi, type AdminUser, type AdminUserDetail } from '@/api/admin'
import { useToast } from '@/composables/useToast'

const { toast } = useToast()
const keyword = ref('')
const page = ref(1)
const pageSize = 20
const total = ref(0)
const users = ref<AdminUser[]>([])
const loading = ref(false)
const showDetail = ref(false)
const detail = ref<AdminUserDetail | null>(null)
const banModal = ref<AdminUser | null>(null)
const banReason = ref('')
const pwdModal = ref<AdminUser | null>(null)
const newPwd = ref('')

async function load() {
  loading.value = true
  try {
    const res = await adminApi.listUsers({ keyword: keyword.value.trim() || undefined, page: page.value, pageSize })
    if (res.success) {
      users.value = res.data.items
      total.value = res.data.total
    } else {
      toast(res.message)
    }
  } finally {
    loading.value = false
  }
}

function search() {
  page.value = 1
  load()
}

async function openDetail(u: AdminUser) {
  const res = await adminApi.userDetail(u.id)
  if (res.success) {
    detail.value = res.data
    showDetail.value = true
  } else {
    toast(res.message)
  }
}

function openBan(u: AdminUser) {
  if (u.isBot) {
    toast('机器人账号不支持封禁')
    return
  }
  banModal.value = u
  banReason.value = ''
}

async function confirmBan() {
  const u = banModal.value
  if (!u) return
  const res = await adminApi.banUser(u.id, !u.isBanned, u.isBanned ? undefined : banReason.value)
  toast(res.message)
  if (res.success) {
    banModal.value = null
    await load()
  }
}

async function kick(u: AdminUser) {
  if (!confirm(`确定让用户「${u.nickname}」的所有设备下线？`)) return
  const res = await adminApi.kickUser(u.id)
  toast(res.message)
  if (res.success) await load()
}

function openPwd(u: AdminUser) {
  if (u.isBot) {
    toast('机器人账号无密码')
    return
  }
  pwdModal.value = u
  newPwd.value = ''
}

async function confirmPwd() {
  const u = pwdModal.value
  if (!u) return
  if (newPwd.value.length < 6) {
    toast('新密码至少 6 位')
    return
  }
  const res = await adminApi.resetPassword(u.id, newPwd.value)
  toast(res.message)
  if (res.success) {
    pwdModal.value = null
    await load()
  }
}

function fmtTime(t: string | null | undefined): string {
  if (!t) return '—'
  return new Date(t).toLocaleString('zh-CN', { hour12: false })
}

onMounted(load)
</script>

<template>
  <div>
    <h2 class="page-title">用户管理</h2>
    <div class="toolbar">
      <input v-model="keyword" class="input" placeholder="搜索：账号 ID / 昵称 / 邮箱" @keyup.enter="search" />
      <button class="btn btn-primary btn-sm" @click="search">搜索</button>
      <div class="spacer"></div>
      <button class="btn btn-sm" @click="load">刷新</button>
    </div>

    <div class="table-wrap">
      <table class="admin-table">
        <thead>
          <tr>
            <th>账号 ID</th>
            <th>昵称</th>
            <th>邮箱</th>
            <th>类型</th>
            <th>状态</th>
            <th>好友/群</th>
            <th>消息</th>
            <th>注册时间</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="u in users" :key="u.id">
            <td>{{ u.id }}</td>
            <td>
              <a style="color: var(--primary); cursor: pointer" @click="openDetail(u)">{{ u.nickname }}</a>
            </td>
            <td>{{ u.email || '—' }}</td>
            <td><span class="tag tag-info" v-if="u.isBot">🤖 机器人</span><span v-else>用户</span></td>
            <td>
              <span class="tag tag-ok" v-if="u.isOnline">在线</span>
              <span class="tag tag-warn" v-if="u.isBanned">已封禁</span>
              <span v-if="!u.isOnline && !u.isBanned" style="color: var(--text-secondary)">离线</span>
            </td>
            <td>{{ u.friendCount }} / {{ u.groupCount }}</td>
            <td>{{ u.messageCount }}</td>
            <td>{{ fmtTime(u.createdAt) }}</td>
            <td>
              <button class="btn btn-sm" @click="openDetail(u)">详情</button>
              <button class="btn btn-sm" @click="kick(u)">踢下线</button>
              <button class="btn btn-sm" @click="openPwd(u)">重置密码</button>
              <button class="btn btn-sm" :class="u.isBanned ? '' : 'btn-danger'" @click="openBan(u)">
                {{ u.isBanned ? '解封' : '封禁' }}
              </button>
            </td>
          </tr>
          <tr v-if="!users.length && !loading">
            <td colspan="9"><div class="empty">暂无用户</div></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="pagination" v-if="total > pageSize">
      <button class="btn btn-sm" :disabled="page <= 1" @click="page--; load()">上一页</button>
      <span>第 {{ page }} 页 / 共 {{ Math.ceil(total / pageSize) }} 页（{{ total }} 人）</span>
      <button class="btn btn-sm" :disabled="page * pageSize >= total" @click="page++; load()">下一页</button>
    </div>

    <!-- 详情弹窗 -->
    <div class="modal-overlay" v-if="showDetail" @click.self="showDetail = false">
      <div class="modal" style="width: 460px">
        <h3>用户详情 #{{ detail?.user.id }} {{ detail?.user.nickname }}</h3>
        <div class="row" v-if="detail">
          <label>邮箱：{{ detail.user.email || '—' }} · 注册：{{ fmtTime(detail.user.createdAt) }}</label>
          <label>封禁：{{ detail.user.isBanned ? `已封禁（${detail.user.banReason || '无原因'}）` : '否' }} · 消息 {{ detail.user.messageCount }} 条</label>
        </div>
        <div class="row">
          <label>登录设备（{{ detail?.sessions.length || 0 }} 台）</label>
          <div v-for="s in detail?.sessions" :key="s.sessionId" style="font-size: 12.5px; padding: 3px 0; color: var(--text-secondary)">
            {{ s.deviceName }} · IP {{ s.ip || '—' }} · 最近活跃 {{ fmtTime(new Date(s.lastActiveAt).toISOString()) }}
          </div>
          <div v-if="!detail?.sessions.length" style="font-size: 12.5px; color: var(--text-secondary)">无在线设备</div>
        </div>
        <div class="actions">
          <button class="btn" @click="showDetail = false">关闭</button>
        </div>
      </div>
    </div>

    <!-- 封禁弹窗 -->
    <div class="modal-overlay" v-if="banModal" @click.self="banModal = null">
      <div class="modal">
        <h3>{{ banModal.isBanned ? '解封用户' : '封禁用户' }} #{{ banModal.id }} {{ banModal.nickname }}</h3>
        <template v-if="!banModal.isBanned">
          <div class="row">
            <label>封禁原因（用户登录时会看到）</label>
            <input v-model="banReason" class="input" placeholder="如：发布违规内容" maxlength="200" />
          </div>
        </template>
        <p style="font-size: 12.5px; color: var(--text-secondary)">封禁后该用户所有设备立即下线，且无法再登录。</p>
        <div class="actions">
          <button class="btn" @click="banModal = null">取消</button>
          <button class="btn" :class="banModal.isBanned ? 'btn-primary' : 'btn-danger'" @click="confirmBan">
            {{ banModal.isBanned ? '确认解封' : '确认封禁' }}
          </button>
        </div>
      </div>
    </div>

    <!-- 重置密码弹窗 -->
    <div class="modal-overlay" v-if="pwdModal" @click.self="pwdModal = null">
      <div class="modal">
        <h3>重置密码 #{{ pwdModal.id }} {{ pwdModal.nickname }}</h3>
        <div class="row">
          <label>新密码（至少 6 位，无需邮箱验证码）</label>
          <input v-model="newPwd" class="input" type="text" placeholder="输入新密码" />
        </div>
        <p style="font-size: 12.5px; color: var(--text-secondary)">重置后该用户所有设备下线，需用新密码重新登录。</p>
        <div class="actions">
          <button class="btn" @click="pwdModal = null">取消</button>
          <button class="btn btn-primary" @click="confirmPwd">确认重置</button>
        </div>
      </div>
    </div>
  </div>
</template>
