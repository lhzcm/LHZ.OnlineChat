<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { adminApi, type AdminGroupDto, type AdminGroupDetailDto } from '@/api/admin'
import { useToast } from '@/composables/useToast'

const { toast } = useToast()
const keyword = ref('')
const page = ref(1)
const pageSize = 20
const total = ref(0)
const groups = ref<AdminGroupDto[]>([])
const loading = ref(false)
const detail = ref<AdminGroupDetailDto | null>(null)
const showDetail = ref(false)
const muteTarget = ref<{ userId: number; nickname: string; mutedUntil: string | null } | null>(null)
const muteMinutes = ref(30)
const transferTarget = ref<number | null>(null)

async function load() {
  loading.value = true
  try {
    const res = await adminApi.listGroups({ keyword: keyword.value.trim() || undefined, page: page.value, pageSize })
    if (res.success) {
      groups.value = res.data.items
      total.value = res.data.total
    } else toast(res.message)
  } finally {
    loading.value = false
  }
}

function search() {
  page.value = 1
  load()
}

async function openDetail(g: AdminGroupDto) {
  const res = await adminApi.groupDetail(g.id)
  if (res.success) {
    detail.value = res.data
    showDetail.value = true
  } else toast(res.message)
}

async function dissolve(g: AdminGroupDto) {
  if (!confirm(`确定解散群「${g.name}」（${g.memberCount} 名成员，${g.messageCount} 条消息将被删除）？`)) return
  const res = await adminApi.dissolveGroup(g.id)
  toast(res.message)
  if (res.success) {
    showDetail.value = false
    await load()
  }
}

async function removeMember(m: { userId: number; nickname: string }) {
  if (!confirm(`确定将「${m.nickname}」移出该群？`)) return
  const res = await adminApi.removeGroupMember(detail.value!.group.id, m.userId)
  toast(res.message)
  if (res.success) await openDetail(detail.value!.group)
}

function openMute(m: { userId: number; nickname: string; mutedUntil: string | null }) {
  muteTarget.value = m
  muteMinutes.value = m.mutedUntil ? 0 : 30
}

async function confirmMute() {
  const m = muteTarget.value
  if (!m || !detail.value) return
  const mutedUntil = muteMinutes.value > 0 ? new Date(Date.now() + muteMinutes.value * 60000).toISOString() : null
  const res = await adminApi.muteGroupMember(detail.value.group.id, m.userId, mutedUntil)
  toast(res.message)
  if (res.success) {
    muteTarget.value = null
    await openDetail(detail.value.group)
  }
}

async function transfer() {
  if (!detail.value || !transferTarget.value) return
  if (!confirm(`确定将群主转让给 #${transferTarget.value}？（原群主降为成员）`)) return
  const res = await adminApi.transferOwner(detail.value.group.id, transferTarget.value)
  toast(res.message)
  if (res.success) {
    transferTarget.value = null
    await openDetail(detail.value.group)
  }
}

function fmtTime(t: string | null | undefined): string {
  if (!t) return '—'
  return new Date(t).toLocaleString('zh-CN', { hour12: false })
}

function fmtMute(t: string | null | undefined): string {
  if (!t) return '—'
  const d = new Date(t)
  return `至 ${d.toLocaleString('zh-CN', { hour12: false })}`
}

onMounted(load)
</script>

<template>
  <div>
    <h2 class="page-title">群组管理</h2>
    <div class="toolbar">
      <input v-model="keyword" class="input" placeholder="搜索群名称" @keyup.enter="search" />
      <button class="btn btn-primary btn-sm" @click="search">搜索</button>
      <div class="spacer"></div>
      <button class="btn btn-sm" @click="load">刷新</button>
    </div>

    <div class="table-wrap">
      <table class="admin-table">
        <thead>
          <tr><th>群 ID</th><th>名称</th><th>群主</th><th>成员</th><th>消息</th><th>创建时间</th><th>操作</th></tr>
        </thead>
        <tbody>
          <tr v-for="g in groups" :key="g.id">
            <td>{{ g.id }}</td>
            <td><a style="color: var(--primary); cursor: pointer" @click="openDetail(g)">{{ g.name }}</a></td>
            <td>{{ g.ownerName }} (#{{ g.ownerId }})</td>
            <td>{{ g.memberCount }}</td>
            <td>{{ g.messageCount }}</td>
            <td>{{ fmtTime(g.createdAt) }}</td>
            <td>
              <button class="btn btn-sm" @click="openDetail(g)">详情</button>
              <button class="btn btn-sm btn-danger" @click="dissolve(g)">解散</button>
            </td>
          </tr>
          <tr v-if="!groups.length && !loading">
            <td colspan="7"><div class="empty">暂无群组</div></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="pagination" v-if="total > pageSize">
      <button class="btn btn-sm" :disabled="page <= 1" @click="page--; load()">上一页</button>
      <span>第 {{ page }} 页 / 共 {{ Math.ceil(total / pageSize) }} 页</span>
      <button class="btn btn-sm" :disabled="page * pageSize >= total" @click="page++; load()">下一页</button>
    </div>

    <!-- 群详情 -->
    <div class="modal-overlay" v-if="showDetail && detail" @click.self="showDetail = false">
      <div class="modal" style="width: 560px">
        <h3>群详情：{{ detail.group.name }}（#{{ detail.group.id }}）</h3>
        <div class="row">
          <label>群主 {{ detail.group.ownerName }} · 成员 {{ detail.group.memberCount }} · 消息 {{ detail.group.messageCount }}</label>
          <label v-if="detail.group.announcement">公告：{{ detail.group.announcement }}</label>
        </div>
        <div class="row">
          <label>转让群主给</label>
          <div style="display: flex; gap: 8px">
            <select v-model.number="transferTarget" class="input" style="width: 180px">
              <option :value="0">选择成员…</option>
              <option v-for="m in detail.members.filter(x => !x.isBot)" :key="m.userId" :value="m.userId">{{ m.nickname }} (#{{ m.userId }})</option>
            </select>
            <button class="btn btn-sm" :disabled="!transferTarget" @click="transfer">转让</button>
          </div>
        </div>
        <div class="row">
          <label>成员列表（{{ detail.members.length }}）</label>
          <div style="max-height: 240px; overflow-y: auto">
            <div v-for="m in detail.members" :key="m.userId" style="display: flex; align-items: center; gap: 8px; padding: 5px 0; font-size: 13px">
              <span :class="['tag', m.isOnline ? 'tag-ok' : '']">{{ m.isOnline ? '在线' : '离线' }}</span>
              <span v-if="m.role === 0" class="tag tag-warn">群主</span>
              <span v-else-if="m.role === 1" class="tag tag-info">管理员</span>
              <span v-if="m.isBot" class="tag tag-info">🤖</span>
              <span style="flex: 1">{{ m.nickname }} (#{{ m.userId }})</span>
              <span v-if="m.mutedUntil" class="tag tag-danger">{{ fmtMute(m.mutedUntil) }}</span>
              <button v-if="m.role !== 0" class="btn btn-sm" @click="openMute(m)">{{ m.mutedUntil ? '解除禁言' : '禁言' }}</button>
              <button v-if="m.role !== 0" class="btn btn-sm btn-danger" @click="removeMember(m)">移除</button>
            </div>
          </div>
        </div>
        <div class="actions">
          <button class="btn btn-sm btn-danger" @click="dissolve(detail.group)">解散该群</button>
          <button class="btn" @click="showDetail = false">关闭</button>
        </div>
      </div>
    </div>

    <!-- 禁言弹窗 -->
    <div class="modal-overlay" v-if="muteTarget" @click.self="muteTarget = null">
      <div class="modal">
        <h3>{{ muteTarget.mutedUntil ? '解除禁言' : '禁言成员' }}：{{ muteTarget.nickname }}</h3>
        <div v-if="!muteTarget.mutedUntil" class="row">
          <label>禁言时长（分钟）</label>
          <input v-model.number="muteMinutes" class="input" type="number" min="1" max="43200" />
        </div>
        <div class="actions">
          <button class="btn" @click="muteTarget = null">取消</button>
          <button class="btn btn-primary" @click="confirmMute">{{ muteTarget.mutedUntil ? '确认解除' : '确认禁言' }}</button>
        </div>
      </div>
    </div>
  </div>
</template>
