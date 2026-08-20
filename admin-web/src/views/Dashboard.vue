<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { adminApi, type DashboardOverview } from '@/api/admin'

const overview = ref<DashboardOverview | null>(null)
const loading = ref(true)
const lastUpdated = ref('')
const refreshCount = ref(0)
let timer: number | null = null

async function load() {
  const res = await adminApi.dashboard()
  if (res.success) {
    overview.value = res.data
    lastUpdated.value = new Date().toLocaleTimeString('zh-CN', { hour12: false })
    refreshCount.value++
  }
  loading.value = false
}

onMounted(async () => {
  await load()
  timer = window.setInterval(load, 30000) // 30s 自动刷新
})
onUnmounted(() => {
  if (timer) clearInterval(timer)
})

// ==================== 图表（纯 SVG 自绘） ====================

/** 柱状图 */
function barChart(points: Array<{ count: number; date?: string; hour?: string }>, height = 150, color = '#5b6cff') {
  if (!points || !points.length) return ''
  const max = Math.max(1, ...points.map(p => p.count))
  const n = points.length
  const padB = 26, padT = 18
  const plotH = height - padB - padT
  const barW = Math.min(26, (800 - 30) / n - 8)
  const gap = barW + 8
  const width = Math.max(400, n * gap + 20)
  const bars = points.map((p, i) => {
    const label = p.date ?? p.hour ?? ''
    const h = Math.max(2, (p.count / max) * plotH)
    const x = 10 + i * gap
    const y = padT + plotH - h
    const isPeak = p.count === max && p.count > 0
    return `<rect x="${x}" y="${y}" width="${barW}" height="${h}" rx="4"
      fill="${isPeak ? '#7c8cff' : color}" opacity="${isPeak ? 1 : 0.75}">
      <title>${label}: ${p.count}</title></rect>
      <text x="${x + barW / 2}" y="${padT + plotH + 14}" text-anchor="middle" font-size="9.5" fill="#8b93a3">${label}</text>
      <text x="${x + barW / 2}" y="${y - 5}" text-anchor="middle" font-size="9.5" fill="#e6e9f0">${p.count || ''}</text>`
  }).join('')
  // 网格线
  const grid = [0, 0.25, 0.5, 0.75, 1].map(f => {
    const y = padT + plotH - plotH * f
    return `<line x1="10" y1="${y}" x2="${width - 10}" y2="${y}" stroke="#262c3a" stroke-width="1" stroke-dasharray="3 3"/>`
  }).join('')
  return `<svg width="100%" height="${height}" viewBox="0 0 ${width} ${height}" preserveAspectRatio="xMidYMid meet" xmlns="http://www.w3.org/2000/svg">${grid}${bars}</svg>`
}

/** 折线图（带面积渐变） */
function lineChart(points: { date: string; count: number }[], height = 150, color = '#34c759') {
  if (!points || !points.length) return ''
  const max = Math.max(1, ...points.map(p => p.count))
  const n = points.length
  const padL = 34, padR = 10, padB = 26, padT = 14
  const plotW = 460 - padL - padR
  const plotH = height - padB - padT
  const stepX = plotW / Math.max(1, n - 1)
  const xy = points.map((p, i) => {
    const x = padL + i * stepX
    const y = padT + plotH - (p.count / max) * plotH
    return { x, y, label: p.date, v: p.count }
  })
  const line = xy.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' ')
  const area = `${line} L${xy[xy.length - 1].x.toFixed(1)},${padT + plotH} L${xy[0].x.toFixed(1)},${padT + plotH} Z`
  const dots = xy.map(p =>
    `<circle cx="${p.x}" cy="${p.y}" r="3.2" fill="${color}"><title>${p.label}: ${p.v}</title></circle>
     <text x="${p.x}" y="${padT + plotH + 14}" text-anchor="middle" font-size="9.5" fill="#8b93a3">${p.label}</text>`
  ).join('')
  const yTicks = [0, 0.5, 1].map(f => {
    const y = padT + plotH - plotH * f
    const v = Math.round(max * f)
    return `<text x="${padL - 6}" y="${y + 3}" text-anchor="end" font-size="9" fill="#8b93a3">${v}</text>
      <line x1="${padL}" y1="${y}" x2="${padL + plotW}" y2="${y}" stroke="#262c3a" stroke-width="1" stroke-dasharray="3 3"/>`
  }).join('')
  return `<svg width="100%" height="${height}" viewBox="0 0 460 ${height}" preserveAspectRatio="xMidYMid meet" xmlns="http://www.w3.org/2000/svg">
    <defs><linearGradient id="linegrad" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="${color}" stop-opacity="0.28"/><stop offset="100%" stop-color="${color}" stop-opacity="0.02"/>
    </linearGradient></defs>
    ${yTicks}
    <path d="${area}" fill="url(#linegrad)"/>
    <path d="${line}" fill="none" stroke="${color}" stroke-width="2.2" stroke-linejoin="round"/>
    ${dots}
  </svg>`
}

/** 环形图（占比） */
function donutChart(parts: { label: string; value: number; color: string }[], size = 150) {
  const total = parts.reduce((a, p) => a + p.value, 0)
  if (!total) {
    return `<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}" xmlns="http://www.w3.org/2000/svg">
      <circle cx="${size / 2}" cy="${size / 2}" r="${size / 2 - 10}" fill="none" stroke="#262c3a" stroke-width="16"/>
      <text x="${size / 2}" y="${size / 2 + 5}" text-anchor="middle" font-size="12" fill="#8b93a3">暂无数据</text></svg>`
  }
  const r = size / 2 - 10
  const c = 2 * Math.PI * r
  let offset = 0
  const segs = parts.map(p => {
    const frac = p.value / total
    const dash = frac * c
    const seg = `<circle cx="${size / 2}" cy="${size / 2}" r="${r}" fill="none" stroke="${p.color}" stroke-width="16"
      stroke-dasharray="${dash} ${c - dash}" stroke-dashoffset="${-offset}" transform="rotate(-90 ${size / 2} ${size / 2})"
      opacity="0.9"><title>${p.label}: ${p.value}</title></circle>`
    offset += dash
    return seg
  }).join('')
  return `<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}" xmlns="http://www.w3.org/2000/svg">
    ${segs}
    <text x="${size / 2}" y="${size / 2 - 2}" text-anchor="middle" font-size="17" font-weight="700" fill="#e6e9f0">${total.toLocaleString()}</text>
    <text x="${size / 2}" y="${size / 2 + 16}" text-anchor="middle" font-size="9.5" fill="#8b93a3">消息总数</text>
  </svg>`
}

// ==================== 派生数据 ====================

const statCards = computed(() => {
  const o = overview.value
  if (!o) return []
  return [
    { icon: '🟢', label: '在线用户', value: o.onlineUsers.toLocaleString(), sub: `WS 连接 ${o.wsConnections}`, color: 'var(--success)' },
    { icon: '👥', label: '总用户', value: o.totalUsers.toLocaleString(), sub: `今日注册 +${o.todayRegistrations}`, color: 'var(--primary)' },
    { icon: '📝', label: '今日消息', value: o.todayMessages.toLocaleString(), sub: `私聊 ${o.todayPrivateMessages} · 群聊 ${o.todayGroupMessages}`, color: 'var(--warning)' },
    { icon: '⚡', label: '今日活跃用户', value: o.todayActiveUsers.toLocaleString(), sub: '今日发过消息的用户', color: 'var(--primary-deep)' },
    { icon: '👪', label: '群组', value: o.totalGroups.toLocaleString(), sub: `今日新建 +${o.todayNewGroups}`, color: 'var(--primary)' },
    { icon: '🤖', label: '机器人', value: o.totalRobots.toLocaleString(), sub: 'Webhook 机器人', color: 'var(--primary)' },
    { icon: '📊', label: '消息总量', value: o.totalMessages.toLocaleString(), sub: `私聊 ${o.privateMessageTotal.toLocaleString()} · 群聊 ${o.groupMessageTotal.toLocaleString()}`, color: 'var(--primary)' },
    { icon: '🚫', label: '封禁用户', value: o.bannedUsers.toLocaleString(), sub: '当前被封禁', color: 'var(--danger)' }
  ]
})

const donutParts = computed(() => {
  const o = overview.value
  if (!o) return []
  return [
    { label: '私聊', value: o.privateMessageTotal, color: '#5b6cff' },
    { label: '群聊', value: o.groupMessageTotal, color: '#7c8cff' }
  ]
})

function fmt(n: number | undefined): string {
  return (n ?? 0).toLocaleString()
}

function avatarBg(name: string): string {
  const colors = ['#5b6cff', '#00c6fb', '#43e97b', '#f59e0b', '#ef4444', '#a855f7']
  let h = 0
  for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) % colors.length
  return colors[h]
}
</script>

<template>
  <div>
    <div style="display: flex; align-items: center; gap: 10px; margin-bottom: 16px">
      <h2 class="page-title" style="margin: 0">📊 仪表盘</h2>
      <div class="spacer" style="flex: 1"></div>
      <span style="font-size: 12px; color: var(--text-secondary)">最后刷新 {{ lastUpdated || '—' }} · 每 30s 自动刷新</span>
      <button class="btn btn-sm" @click="load">刷新</button>
    </div>

    <p class="empty" v-if="loading">加载中…</p>
    <template v-else-if="overview">
      <!-- 统计卡片 -->
      <div class="card-grid">
        <div v-for="c in statCards" :key="c.label" class="stat-card">
          <div class="stat-card-icon" :style="{ background: c.color + '22', color: c.color }">{{ c.icon }}</div>
          <div class="stat-card-body">
            <div class="label">{{ c.label }}</div>
            <div class="value" :style="{ color: c.color }">{{ c.value }}</div>
            <div class="sub">{{ c.sub }}</div>
          </div>
        </div>
      </div>

      <!-- 24h 分布 + 占比 -->
      <div class="chart-row">
        <div class="panel">
          <div class="panel-title">近 24 小时消息分布</div>
          <div v-html="barChart(overview.messageHourTrend, 160)"></div>
        </div>
        <div class="panel donut-panel">
          <div class="panel-title">私聊 / 群聊消息占比</div>
          <div style="display: flex; align-items: center; gap: 18px">
            <div v-html="donutChart(donutParts, 150)"></div>
            <div>
              <div v-for="p in donutParts" :key="p.label" style="margin-bottom: 10px">
                <div style="display: flex; align-items: center; gap: 6px; font-size: 13px">
                  <span style="width: 10px; height: 10px; border-radius: 2px; display: inline-block; background: {{ p.color }}"></span>
                  {{ p.label }}
                  <b style="margin-left: auto; color: var(--text)">{{ fmt(p.value) }}</b>
                </div>
                <div style="font-size: 11px; color: var(--text-secondary); margin-top: 2px">
                  {{ overview.totalMessages ? Math.round(p.value / overview.totalMessages * 100) : 0 }}%
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 7 日趋势 -->
      <div class="chart-row">
        <div class="panel">
          <div class="panel-title">近 7 日注册趋势</div>
          <div v-html="lineChart(overview.registerTrend, 160, '#5b6cff')"></div>
        </div>
        <div class="panel">
          <div class="panel-title">近 7 日消息趋势</div>
          <div v-html="lineChart(overview.messageTrend, 160, '#34c759')"></div>
        </div>
      </div>

      <!-- TOP 排行 -->
      <div class="chart-row">
        <div class="panel">
          <div class="panel-title">🏆 最活跃用户 TOP10（消息发送量）</div>
          <div v-if="overview.topUsers.length">
            <div v-for="(u, i) in overview.topUsers" :key="u.userId" class="rank-row">
              <span class="rank-no" :class="{ top: i < 3 }">{{ i + 1 }}</span>
              <span class="rank-avatar" :style="{ background: avatarBg(u.nickname) }">
                <img v-if="u.avatar" :src="u.avatar" alt="" />
                <span v-else>{{ u.nickname.slice(0, 1) }}</span>
              </span>
              <span class="rank-name">{{ u.nickname }}</span>
              <span class="rank-meta">#{{ u.userId }}</span>
              <span class="rank-count">{{ fmt(u.count) }} 条</span>
            </div>
          </div>
          <div v-else class="empty">暂无数据</div>
        </div>
        <div class="panel">
          <div class="panel-title">🏆 最活跃群 TOP10（群消息量）</div>
          <div v-if="overview.topGroups.length">
            <div v-for="(g, i) in overview.topGroups" :key="g.groupId" class="rank-row">
              <span class="rank-no" :class="{ top: i < 3 }">{{ i + 1 }}</span>
              <span class="rank-avatar" :style="{ background: avatarBg(g.name) }">{{ g.name.slice(0, 1) }}</span>
              <span class="rank-name">{{ g.name }}</span>
              <span class="rank-meta">群 #{{ g.groupId }}</span>
              <span class="rank-count">{{ fmt(g.count) }} 条</span>
            </div>
          </div>
          <div v-else class="empty">暂无数据</div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
  gap: 14px;
  margin-bottom: 16px;
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 12px;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 14px 16px;
  transition: border-color 0.2s;
}

.stat-card:hover { border-color: var(--primary); }

.stat-card-icon {
  width: 42px;
  height: 42px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 20px;
  flex-shrink: 0;
}

.stat-card-body { min-width: 0; }

.stat-card .label { font-size: 12px; color: var(--text-secondary); }

.stat-card .value {
  font-size: 22px;
  font-weight: 700;
  line-height: 1.25;
  font-variant-numeric: tabular-nums;
}

.stat-card .sub { font-size: 11px; color: var(--text-secondary); margin-top: 2px; white-space: nowrap; }

.chart-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px;
  margin-bottom: 14px;
}

@media (max-width: 1100px) { .chart-row { grid-template-columns: 1fr; } }

.panel {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 14px 16px;
  overflow: hidden;
}

.panel-title {
  font-size: 13px;
  color: var(--text-secondary);
  margin-bottom: 12px;
  font-weight: 500;
}

.donut-panel { display: flex; flex-direction: column; }

/* 排行 */
.rank-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 4px;
  border-bottom: 1px solid rgba(38, 44, 58, 0.5);
  font-size: 13px;
}

.rank-row:last-child { border-bottom: none; }

.rank-no {
  width: 20px;
  text-align: center;
  font-weight: 600;
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
}

.rank-no.top { color: var(--warning); }

.rank-avatar {
  width: 26px;
  height: 26px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  color: #fff;
  flex-shrink: 0;
  overflow: hidden;
}

.rank-avatar img { width: 100%; height: 100%; object-fit: cover; }

.rank-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.rank-meta { font-size: 11.5px; color: var(--text-secondary); }

.rank-count {
  font-size: 12.5px;
  font-weight: 600;
  color: var(--primary);
  font-variant-numeric: tabular-nums;
}
</style>
