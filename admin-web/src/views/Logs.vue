<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { adminApi, type AdminLog } from '@/api/admin'
import { useToast } from '@/composables/useToast'

const { toast } = useToast()
const logs = ref<AdminLog[]>([])
const page = ref(1)
const pageSize = 20
const total = ref(0)

async function load() {
  const res = await adminApi.listLogs({ page: page.value, pageSize })
  if (res.success) {
    logs.value = res.data.items
    total.value = res.data.total
  } else {
    toast(res.message)
  }
}

function fmtTime(t: string): string {
  return new Date(t).toLocaleString('zh-CN', { hour12: false })
}

onMounted(load)
</script>

<template>
  <div>
    <h2 class="page-title">审计日志</h2>
    <div class="table-wrap">
      <table class="admin-table">
        <thead>
          <tr>
            <th>时间</th>
            <th>管理员</th>
            <th>操作</th>
            <th>目标</th>
            <th>详情</th>
            <th>IP</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="l in logs" :key="l.id">
            <td>{{ fmtTime(l.createdAt) }}</td>
            <td>{{ l.adminName }}</td>
            <td><span class="tag tag-info">{{ l.action }}</span></td>
            <td>{{ l.targetType }}#{{ l.targetId || '—' }}</td>
            <td style="max-width: 320px; white-space: normal; word-break: break-all">{{ l.detail || '—' }}</td>
            <td>{{ l.ip || '—' }}</td>
          </tr>
          <tr v-if="!logs.length">
            <td colspan="6"><div class="empty">暂无审计记录</div></td>
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
