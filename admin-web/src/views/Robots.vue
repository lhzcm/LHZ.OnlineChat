<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { adminApi, type AdminRobotDto } from '@/api/admin'
import { useToast } from '@/composables/useToast'

const { toast } = useToast()
const keyword = ref('')
const page = ref(1)
const pageSize = 20
const total = ref(0)
const robots = ref<AdminRobotDto[]>([])

async function load() {
  const res = await adminApi.listRobots({ keyword: keyword.value.trim() || undefined, page: page.value, pageSize })
  if (res.success) {
    robots.value = res.data.items
    total.value = res.data.total
  } else {
    toast(res.message)
  }
}

function search() {
  page.value = 1
  load()
}

async function toggleEnabled(r: AdminRobotDto) {
  const res = await adminApi.setRobotEnabled(r.id, !r.enabled)
  toast(res.message)
  if (res.success) await load()
}

async function remove(r: AdminRobotDto) {
  if (!confirm(`确定删除机器人「${r.name}」？（将同时清理其账号、好友关系与群成员身份）`)) return
  const res = await adminApi.deleteRobot(r.id)
  toast(res.message)
  if (res.success) await load()
}

function fmtTime(t: string): string {
  return new Date(t).toLocaleString('zh-CN', { hour12: false })
}

onMounted(load)
</script>

<template>
  <div>
    <h2 class="page-title">机器人管理</h2>
    <div class="toolbar">
      <input v-model="keyword" class="input" placeholder="搜索机器人名称/ID" @keyup.enter="search" />
      <button class="btn btn-primary btn-sm" @click="search">搜索</button>
      <div class="spacer"></div>
      <button class="btn btn-sm" @click="load">刷新</button>
    </div>

    <div class="table-wrap">
      <table class="admin-table">
        <thead>
          <tr><th>ID</th><th>名称</th><th>创建者</th><th>Webhook</th><th>状态</th><th>推送/失败</th><th>创建时间</th><th>操作</th></tr>
        </thead>
        <tbody>
          <tr v-for="r in robots" :key="r.id">
            <td>{{ r.id }} ({{ r.userId }})</td>
            <td>{{ r.name }} <span class="tag tag-info">🤖</span></td>
            <td>{{ r.ownerName }} (#{{ r.ownerId }})</td>
            <td style="max-width: 180px; overflow: hidden; text-overflow: ellipsis">{{ r.webhookUrl || '纯推送' }}</td>
            <td><span class="tag" :class="r.enabled ? 'tag-ok' : 'tag-warn'">{{ r.enabled ? '启用' : '停用' }}</span></td>
            <td>{{ r.pushCount }} / {{ r.callbackFailCount }}</td>
            <td>{{ fmtTime(r.createdAt) }}</td>
            <td>
              <button class="btn btn-sm" @click="toggleEnabled(r)">{{ r.enabled ? '停用' : '启用' }}</button>
              <button class="btn btn-sm btn-danger" @click="remove(r)">删除</button>
            </td>
          </tr>
          <tr v-if="!robots.length">
            <td colspan="8"><div class="empty">暂无机器人</div></td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="pagination" v-if="total > pageSize">
      <button class="btn btn-sm" :disabled="page <= 1" @click="page--; load()">上一页</button>
      <span>第 {{ page }} 页 / 共 {{ Math.ceil(total / pageSize) }} 页</span>
      <button class="btn btn-sm" :disabled="page * pageSize >= total" @click="page++; load()">下一页</button>
    </div>
  </div>
</template>
