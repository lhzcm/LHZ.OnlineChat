<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { adminApi, type AdminMessageDto } from '@/api/admin'
import { useToast } from '@/composables/useToast'

const { toast } = useToast()
const keyword = ref('')
const userId = ref<number | null>(null)
const page = ref(1)
const pageSize = 20
const total = ref(0)
const messages = ref<AdminMessageDto[]>([])

async function load() {
  const res = await adminApi.searchMessages({
    keyword: keyword.value.trim() || undefined,
    userId: userId.value || undefined,
    page: page.value,
    pageSize
  })
  if (res.success) {
    messages.value = res.data.items
    total.value = res.data.total
  } else {
    toast(res.message)
  }
}

function search() {
  page.value = 1
  load()
}

async function remove(m: AdminMessageDto) {
  if (!confirm(`确定删除这条${m.type === 'group' ? '群' : '私聊'}消息？双方将无法再看到。`)) return
  const res = await adminApi.deleteMessage(m.type, m.id)
  toast(res.message)
  if (res.success) await load()
}

function fmtTime(t: string): string {
  return new Date(t).toLocaleString('zh-CN', { hour12: false })
}

function contentPreview(c: string): string {
  return c.length > 60 ? c.slice(0, 60) + '…' : c
}

onMounted(load)
</script>

<template>
  <div>
    <h2 class="page-title">消息检索</h2>
    <div class="toolbar">
      <input v-model="keyword" class="input" placeholder="关键词" @keyup.enter="search" />
      <input v-model.number="userId" class="input" style="width: 140px" type="number" placeholder="用户 ID（可选）" />
      <button class="btn btn-primary btn-sm" @click="search">搜索</button>
      <div class="spacer"></div>
      <button class="btn btn-sm" @click="load">刷新</button>
    </div>

    <div class="table-wrap">
      <table class="admin-table">
        <thead>
          <tr><th>ID</th><th>类型</th><th>发送者</th><th>内容</th><th>会话</th><th>状态</th><th>时间</th><th>操作</th></tr>
        </thead>
        <tbody>
          <tr v-for="m in messages" :key="m.type + m.id">
            <td>{{ m.id }}</td>
            <td><span class="tag" :class="m.type === 'group' ? 'tag-info' : 'tag-ok'">{{ m.type === 'group' ? '群聊' : '私聊' }}</span></td>
            <td>{{ m.senderName }} (#{{ m.senderId }})</td>
            <td style="max-width: 320px; white-space: normal; word-break: break-all">{{ contentPreview(m.content) }}</td>
            <td>{{ m.type === 'group' ? `群 ${m.sessionId}` : `与 ${m.sessionId}` }}</td>
            <td><span class="tag tag-warn" v-if="m.isDeleted">已删除</span><span v-else>正常</span></td>
            <td>{{ fmtTime(m.sentAt) }}</td>
            <td>
              <button class="btn btn-sm btn-danger" :disabled="m.isDeleted" @click="remove(m)">删除</button>
            </td>
          </tr>
          <tr v-if="!messages.length">
            <td colspan="8"><div class="empty">暂无消息（输入关键词或用户 ID 搜索）</div></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="pagination" v-if="total > pageSize">
      <button class="btn btn-sm" :disabled="page <= 1" @click="page--; load()">上一页</button>
      <span>第 {{ page }} 页 / 共 {{ Math.ceil(total / pageSize) }} 页（{{ total }} 条）</span>
      <button class="btn btn-sm" :disabled="page * pageSize >= total" @click="page++; load()">下一页</button>
    </div>
  </div>
</template>
