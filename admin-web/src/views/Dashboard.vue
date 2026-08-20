<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { adminApi, type DashboardOverview } from '@/api/admin'

const overview = ref<DashboardOverview | null>(null)
const loading = ref(true)

onMounted(async () => {
  const res = await adminApi.dashboard()
  if (res.success) overview.value = res.data
  loading.value = false
})

/** 自绘 SVG 柱状图（不引图表库） */
function barChart(points: { date: string; count: number }[], height = 140) {
  if (!points || !points.length) return ''
  const max = Math.max(1, ...points.map(p => p.count))
  const barW = 26
  const gap = 12
  const width = points.length * (barW + gap) + 20
  const padBottom = 24
  const bars = points.map((p, i) => {
    const h = Math.max(2, (p.count / max) * (height - padBottom - 10))
    const x = 10 + i * (barW + gap)
    const y = height - padBottom - h
    return `<rect x="${x}" y="${y}" width="${barW}" height="${h}" rx="4" fill="#5b6cff" opacity="0.85">
      <title>${p.date}: ${p.count}</title></rect>
      <text x="${x + barW / 2}" y="${height - 8}" text-anchor="middle" font-size="10" fill="#8b93a3">${p.date}</text>
      <text x="${x + barW / 2}" y="${y - 5}" text-anchor="middle" font-size="10" fill="#e6e9f0">${p.count}</text>`
  }).join('')
  return `<svg width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" xmlns="http://www.w3.org/2000/svg">${bars}</svg>`
}

function fmt(n: number | undefined): string {
  return (n ?? 0).toLocaleString()
}
</script>

<template>
  <div>
    <h2 class="page-title">仪表盘</h2>
    <p class="empty" v-if="loading">加载中…</p>
    <template v-else-if="overview">
      <div class="card-grid">
        <div class="stat-card">
          <div class="label">在线用户</div>
          <div class="value ok">{{ overview.onlineUsers }}</div>
          <div class="label">WS 连接 {{ overview.wsConnections }}</div>
        </div>
        <div class="stat-card">
          <div class="label">总用户</div>
          <div class="value">{{ fmt(overview.totalUsers) }}</div>
          <div class="label">封禁 {{ overview.bannedUsers }} · 今日注册 {{ overview.todayRegistrations }}</div>
        </div>
        <div class="stat-card">
          <div class="label">总群组</div>
          <div class="value">{{ fmt(overview.totalGroups) }}</div>
          <div class="label">机器人 {{ overview.totalRobots }}</div>
        </div>
        <div class="stat-card">
          <div class="label">消息总数</div>
          <div class="value">{{ fmt(overview.totalMessages) }}</div>
          <div class="label">今日 {{ fmt(overview.todayMessages) }}</div>
        </div>
      </div>
      <div class="table-wrap" style="padding: 16px; margin-bottom: 14px">
        <div style="font-size: 13.5px; color: var(--text-secondary); margin-bottom: 10px">近 7 日注册趋势</div>
        <div v-html="barChart(overview.registerTrend)"></div>
      </div>
      <div class="table-wrap" style="padding: 16px">
        <div style="font-size: 13.5px; color: var(--text-secondary); margin-bottom: 10px">近 7 日消息趋势</div>
        <div v-html="barChart(overview.messageTrend)"></div>
      </div>
    </template>
  </div>
</template>
