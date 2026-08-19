<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { useAuthStore } from '@/stores/auth'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'
import { useChatStore } from '@/stores/chat'
import { messageApi } from '@/api/message'
import { formatMsgTime, pad } from '@/utils/format'
import type { FriendInfo, SessionInfo, ChatType, MessageSearchResult } from '@/types'

const props = defineProps<{
  activeTab: 'sessions' | 'friends' | 'groups'
  currentChat: { type: ChatType; id: number; name: string } | null
  mobileChatOpen: boolean
}>()
const emit = defineEmits<{
  'update:activeTab': [tab: 'sessions' | 'friends' | 'groups']
  'select-private': [friend: FriendInfo]
  'select-group': [group: { id: number; name: string }]
  'select-session': [s: SessionInfo]
  'session-setting': [s: SessionInfo]
  'friend-setting': [f: FriendInfo]
  add: []
  requests: []
  robot: []
  profile: []
  logout: []
  'open-result': [r: MessageSearchResult]
}>()

const auth = useAuthStore()
const friendStore = useFriendStore()
const groupStore = useGroupStore()
const chatStore = useChatStore()

// ==================== 账号 ID 复制 ====================
const copyTip = ref('')
let copyTipTimer: number | null = null
onMounted(() => {
  copyTip.value = auth.user ? `ID: ${auth.user.id}` : ''
})
onUnmounted(() => {
  if (copyTipTimer) clearTimeout(copyTipTimer)
})
async function copyAccountId() {
  if (!auth.user) return
  try {
    await navigator.clipboard.writeText(String(auth.user.id))
    copyTip.value = '已复制 ✅'
  } catch {
    copyTip.value = 'ID: ' + auth.user.id
  }
  if (copyTipTimer) clearTimeout(copyTipTimer)
  copyTipTimer = window.setTimeout(() => {
    copyTip.value = auth.user ? `ID: ${auth.user.id}` : ''
  }, 2000)
}

// ==================== 主题 ====================
const isDark = ref(localStorage.getItem('theme') === 'dark')
function applyTheme(dark: boolean) {
  isDark.value = dark
  document.documentElement.dataset.theme = dark ? 'dark' : 'light'
  localStorage.setItem('theme', dark ? 'dark' : 'light')
}
onMounted(() => applyTheme(isDark.value))

// ==================== 列表数据 ====================
const sortedSessions = computed(() =>
  [...chatStore.sessions].sort((a, b) => {
    if (a.isPinned !== b.isPinned) return a.isPinned ? -1 : 1
    return new Date(b.lastTime).getTime() - new Date(a.lastTime).getTime()
  })
)

/** 好友显示名：备注优先，其次昵称 */
function friendDisplayName(f: FriendInfo): string {
  return f.remark || f.nickname
}

/** 好友按分类分组（未分组放最后，其余按分类名排序） */
const groupedFriends = computed(() => {
  const map = new Map<string, FriendInfo[]>()
  for (const f of friendStore.friends) {
    const cat = f.category || '未分组'
    const list = map.get(cat) || []
    list.push(f)
    map.set(cat, list)
  }
  const groups = [...map.entries()]
    .map(([category, friends]) => ({
      category,
      friends: [...friends].sort((a, b) => Number(b.isOnline) - Number(a.isOnline) || a.nickname.localeCompare(b.nickname))
    }))
    .sort((a, b) => {
      if (a.category === '未分组') return 1
      if (b.category === '未分组') return -1
      return a.category.localeCompare(b.category)
    })
  return groups
})

/** 会话最后一条消息预览 */
function lastMessageFor(type: ChatType, id: number): string {
  const key = chatStore.sessionKey(type, id)
  const list = chatStore.messages.get(key)
  const last = list && list.length ? list[list.length - 1] : null
  if (!last) return ''
  const prefix = type === 'group' && Number(last.from) !== auth.user?.id ? `${last.senderName}: ` : ''
  return prefix + (last.content || '')
}

function unreadOf(type: ChatType, id: number): number {
  return chatStore.unreadCounts.get(chatStore.sessionKey(type, id)) || 0
}

/** 会话预览：优先本地最新消息，否则用服务端会话数据 */
function previewFor(s: SessionInfo): string {
  const local = lastMessageFor(s.type, s.id)
  return local || s.lastMessage || (s.type === 'group' ? '群组会话' : '暂无消息')
}

/** 会话时间：本地消息用毫秒时间戳，服务端数据用 ISO 字符串 */
function timeFor(s: SessionInfo): string {
  const key = chatStore.sessionKey(s.type, s.id)
  const list = chatStore.messages.get(key)
  const last = list && list.length ? list[list.length - 1] : null
  const t = last ? last.timestamp : s.lastTime ? new Date(s.lastTime).getTime() : 0
  if (!t) return ''
  const d = new Date(t)
  const now = new Date()
  const sameDay = d.toDateString() === now.toDateString()
  return sameDay
    ? `${pad(d.getHours())}:${pad(d.getMinutes())}`
    : `${d.getMonth() + 1}/${d.getDate()}`
}

// ==================== 消息搜索 ====================
const searchKeyword = ref('')
const searchActive = ref(false)
const searchLoading = ref(false)
const searchResults = ref<MessageSearchResult[]>([])
const searchPage = ref(1)
const searchTotal = ref(0)
const searchHasMore = computed(() => searchResults.value.length < searchTotal.value)

async function runSearch() {
  const kw = searchKeyword.value.trim()
  if (!kw) return
  searchActive.value = true
  searchLoading.value = true
  searchPage.value = 1
  searchResults.value = []
  searchTotal.value = 0
  try {
    const res = await messageApi.searchMessages(kw, 1)
    if (res.success && res.data) {
      searchResults.value = res.data.items
      searchTotal.value = res.data.total
    }
  } finally {
    searchLoading.value = false
  }
}

async function loadMoreSearch() {
  const kw = searchKeyword.value.trim()
  if (!kw || searchLoading.value) return
  searchLoading.value = true
  try {
    const res = await messageApi.searchMessages(kw, searchPage.value + 1)
    if (res.success && res.data) {
      searchResults.value = [...searchResults.value, ...res.data.items]
      searchTotal.value = res.data.total
      searchPage.value += 1
    }
  } finally {
    searchLoading.value = false
  }
}

function closeSearch() {
  searchActive.value = false
  searchResults.value = []
  searchTotal.value = 0
  searchKeyword.value = ''
}

function openSearchResult(r: MessageSearchResult) {
  emit('open-result', r)
}
</script>

<template>
  <aside class="sidebar" :class="{ 'is-hidden': mobileChatOpen }">
    <div class="sidebar-header">
      <div class="user-info" @click="emit('profile')" title="个人信息">
        <Avatar :name="auth.user?.nickname || ''" :url="auth.user?.avatar" />
        <div class="user-text">
          <span class="nickname">{{ auth.user?.nickname }}</span>
          <span class="account-id" @click.stop="copyAccountId" :title="copyTip">{{ copyTip }}</span>
        </div>
      </div>
      <div class="header-actions">
        <button class="icon-btn" @click="applyTheme(!isDark)" :title="isDark ? '切换到浅色模式' : '切换到深色模式'">
          <svg v-if="!isDark" viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
          </svg>
          <svg v-else viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="12" cy="12" r="5" />
            <line x1="12" y1="1" x2="12" y2="3" />
            <line x1="12" y1="21" x2="12" y2="23" />
            <line x1="4.22" y1="4.22" x2="5.64" y2="5.64" />
            <line x1="18.36" y1="18.36" x2="19.78" y2="19.78" />
            <line x1="1" y1="12" x2="3" y2="12" />
            <line x1="21" y1="12" x2="23" y2="12" />
            <line x1="4.22" y1="19.78" x2="5.64" y2="18.36" />
            <line x1="18.36" y1="5.64" x2="19.78" y2="4.22" />
          </svg>
        </button>
        <button class="icon-btn" @click="emit('requests')" title="好友申请">
          <svg viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
            <path d="M13.73 21a2 2 0 0 1-3.46 0" />
          </svg>
          <span v-if="friendStore.pendingRequests.length" class="badge">{{ friendStore.pendingRequests.length }}</span>
        </button>
        <button class="icon-btn" @click="emit('robot')" title="我的机器人">
          <svg viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="4" y="8" width="16" height="12" rx="3" />
            <path d="M12 8V4" />
            <circle cx="12" cy="3" r="1" />
            <circle cx="9" cy="13" r="1" />
            <circle cx="15" cy="13" r="1" />
            <line x1="9" y1="17" x2="15" y2="17" />
          </svg>
        </button>
        <button class="icon-btn" @click="emit('logout')" title="退出登录">
          <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
            <polyline points="16 17 21 12 16 7" />
            <line x1="21" y1="12" x2="9" y2="12" />
          </svg>
        </button>
      </div>
    </div>

    <!-- 消息搜索 -->
    <div class="search-box">
      <svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="search-icon">
        <circle cx="11" cy="11" r="8" />
        <line x1="21" y1="21" x2="16.65" y2="16.65" />
      </svg>
      <input v-model="searchKeyword" class="search-input" placeholder="搜索消息…" @keydown.enter="runSearch" />
      <button class="search-go" @click="runSearch" :disabled="searchLoading" title="搜索">搜索</button>
    </div>

    <!-- Tab 切换 -->
    <div class="tabs">
      <button :class="['tab', { active: activeTab === 'sessions' }]" @click="emit('update:activeTab', 'sessions')">会话</button>
      <button :class="['tab', { active: activeTab === 'friends' }]" @click="emit('update:activeTab', 'friends')">好友</button>
      <button :class="['tab', { active: activeTab === 'groups' }]" @click="emit('update:activeTab', 'groups')">群组</button>
    </div>

    <!-- 搜索结果面板 -->
    <div class="search-panel" v-if="searchActive">
      <div class="search-panel-head">
        <span>{{ searchLoading ? '搜索中…' : `找到 ${searchResults.length} / ${searchTotal} 条` }}</span>
        <button class="search-close" @click="closeSearch">✕</button>
      </div>
      <div class="search-panel-body">
        <div class="search-result-item" v-for="(r, i) in searchResults" :key="r.type + '_' + r.sessionId + '_' + (r.messageId || i)" @click="openSearchResult(r)">
          <div class="search-result-top">
            <span class="search-result-session">{{ r.type === 'group' ? '群·' : '' }}{{ r.sessionName }}</span>
            <span class="search-result-time">{{ formatMsgTime(new Date(r.sentAt).getTime()) }}</span>
          </div>
          <div class="search-result-content">
            <span class="search-result-sender">{{ r.senderName }}：</span>{{ r.content }}
          </div>
        </div>
        <button v-if="searchHasMore" class="search-more" @click="loadMoreSearch" :disabled="searchLoading">
          {{ searchLoading ? '加载中…' : '加载更多' }}
        </button>
        <div class="search-empty" v-if="!searchLoading && searchResults.length === 0">未找到相关消息</div>
      </div>
    </div>

    <!-- 操作栏 -->
    <div class="action-bar" v-show="!searchActive">
      <button class="btn btn-primary" @click="emit('add')">
        {{ activeTab === 'friends' ? '+ 添加好友' : activeTab === 'groups' ? '+ 创建群组' : '＋' }}
      </button>
    </div>

    <!-- 会话列表 -->
    <div class="contact-list" v-if="activeTab === 'sessions' && !searchActive">
      <div v-for="s in sortedSessions" :key="s.type + '_' + s.id"
        :class="['contact-item', { active: currentChat?.type === s.type && currentChat.id === s.id }]"
        @click="emit('select-session', s)">
        <div class="avatar-wrap">
          <Avatar :name="s.name" :url="s.avatar" size="sm" />
          <span class="group-badge" v-if="s.type === 'group'">群</span>
        </div>
        <div class="contact-info">
          <div class="info-top">
            <span class="contact-name">
              <span class="pin-icon" v-if="s.isPinned">📌</span>
              <span class="bot-tag" v-if="s.isBot">🤖</span>
              {{ s.name }}
            </span>
            <span class="session-time">
              <span class="mute-icon" v-if="s.muted">🔕</span>
              {{ timeFor(s) }}
            </span>
          </div>
          <div class="info-bottom">
            <span class="contact-meta">{{ previewFor(s) }}</span>
            <span v-if="unreadOf(s.type, s.id)" class="unread-badge">{{ unreadOf(s.type, s.id) }}</span>
          </div>
        </div>
        <button class="friend-more" title="会话设置" @click.stop="emit('session-setting', s)">⋯</button>
      </div>
      <div class="empty" v-if="chatStore.sessions.length === 0">
        <span class="empty-icon">💬</span>
        <span>暂无会话，去添加好友开始聊天吧</span>
      </div>
    </div>

    <!-- 好友列表（按分类分组） -->
    <div class="contact-list" v-else-if="activeTab === 'friends' && !searchActive">
      <template v-for="group in groupedFriends" :key="group.category">
        <div class="group-header" v-if="group.friends.length">
          <span>{{ group.category }}</span>
          <span class="group-count">{{ group.friends.length }}</span>
        </div>
        <div v-for="f in group.friends" :key="f.userId"
          :class="['contact-item', { active: currentChat?.type === 'private' && currentChat.id === f.userId }]"
          @click="emit('select-private', f)">
          <div class="avatar-wrap">
            <Avatar :name="friendDisplayName(f)" :url="f.avatar" size="sm" />
            <span :class="['status-dot', f.isOnline ? 'online' : 'offline']"></span>
          </div>
          <div class="contact-info">
            <div class="info-top">
              <span class="contact-name"><span class="bot-tag" v-if="f.isBot">🤖</span>{{ friendDisplayName(f) }}</span>
              <span v-if="unreadOf('private', f.userId)" class="unread-badge">{{ unreadOf('private', f.userId) }}</span>
            </div>
            <div class="info-bottom">
              <span class="contact-meta">{{ lastMessageFor('private', f.userId) || (f.isOnline ? '在线' : '离线') }}</span>
            </div>
          </div>
          <button class="friend-more" title="备注/分类" @click.stop="emit('friend-setting', f)">⋯</button>
        </div>
      </template>
      <div class="empty" v-if="friendStore.friends.length === 0">
        <span class="empty-icon">👥</span>
        <span>暂无好友，点击上方按钮添加</span>
      </div>
    </div>

    <!-- 群组列表 -->
    <div class="contact-list" v-else-if="!searchActive">
      <div v-for="g in groupStore.groups" :key="g.id"
        :class="['contact-item', { active: currentChat?.type === 'group' && currentChat.id === g.id }]"
        @click="emit('select-group', g)">
        <div class="avatar-wrap">
          <Avatar :name="g.name" :url="g.avatar" size="sm" />
          <span class="group-badge">群</span>
        </div>
        <div class="contact-info">
          <div class="info-top">
            <span class="contact-name">{{ g.name }}</span>
            <span v-if="unreadOf('group', g.id)" class="unread-badge">{{ unreadOf('group', g.id) }}</span>
          </div>
          <div class="info-bottom">
            <span class="contact-meta">{{ lastMessageFor('group', g.id) || `${g.memberCount} 人` }}</span>
          </div>
        </div>
      </div>
      <div class="empty" v-if="groupStore.groups.length === 0">
        <span class="empty-icon">🗂️</span>
        <span>暂无群组，点击上方按钮创建</span>
      </div>
    </div>
  </aside>
</template>

<style scoped>
.sidebar {
  width: 320px;
  background: var(--bg-white);
  border-right: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 16px;
}

.user-info {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
  cursor: pointer;
  padding: 4px 6px;
  margin: -4px -6px;
  border-radius: 10px;
  transition: background 0.15s;
}

.user-info:hover {
  background: var(--bg-hover);
}

.nickname {
  font-weight: 600;
  font-size: 15px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.user-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.account-id {
  font-size: 12px;
  color: var(--text-secondary);
  cursor: pointer;
  user-select: none;
  line-height: 1.4;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  transition: color 0.15s;
}

.account-id:hover {
  color: var(--primary);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.icon-btn {
  position: relative;
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  background: transparent;
  border-radius: 50%;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s;
}

.icon-btn:hover {
  background: var(--bg-hover);
  color: var(--text);
}

.badge {
  position: absolute;
  top: 2px;
  right: 2px;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 8px;
  background: var(--danger);
  color: white;
  font-size: 10.5px;
  line-height: 16px;
  text-align: center;
  border: 1.5px solid var(--bg-white);
}

/* Tab 切换 */
.tabs {
  display: flex;
  gap: 4px;
  padding: 2px 12px 10px;
}

.tab {
  flex: 1;
  padding: 8px 0;
  border: none;
  border-radius: 10px;
  background: transparent;
  cursor: pointer;
  font-size: 14px;
  font-weight: 500;
  color: var(--text-secondary);
  transition: all 0.2s;
}

.tab:hover {
  color: var(--text);
  background: var(--bg-hover);
}

.tab.active {
  background: var(--active-bg);
  color: var(--primary);
  font-weight: 600;
}

/* 操作栏 */
.action-bar {
  padding: 0 12px 12px;
}

.action-bar .btn {
  width: 100%;
  background: var(--mine-bubble);
  box-shadow: 0 4px 12px rgba(91, 108, 255, 0.28);
}

.action-bar .btn:hover {
  filter: brightness(1.06);
}

/* 消息搜索 */
.search-box {
  display: flex;
  align-items: center;
  gap: 6px;
  margin: 0 12px 10px;
  padding: 0 6px 0 10px;
  border: 1px solid var(--border);
  border-radius: 10px;
  background: var(--bg);
  transition: border-color 0.15s;
}
.search-box:focus-within {
  border-color: var(--primary);
}
.search-icon {
  color: var(--text-secondary);
  flex-shrink: 0;
}
.search-input {
  flex: 1;
  min-width: 0;
  border: none;
  outline: none;
  background: transparent;
  padding: 7px 0;
  font-size: 13px;
  color: var(--text);
}
.search-input::placeholder {
  color: var(--text-secondary);
}
.search-go {
  border: none;
  background: var(--mine-bubble);
  color: #fff;
  font-size: 12px;
  padding: 4px 10px;
  border-radius: 7px;
  cursor: pointer;
  flex-shrink: 0;
}
.search-go:disabled {
  opacity: 0.6;
  cursor: default;
}

/* 搜索结果面板 */
.search-panel {
  flex: 1;
  overflow-y: auto;
  padding-bottom: 8px;
  min-height: 0;
}
.search-panel-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 16px 8px;
  font-size: 12px;
  color: var(--text-secondary);
}
.search-close {
  border: none;
  background: transparent;
  color: var(--text-secondary);
  font-size: 14px;
  cursor: pointer;
  padding: 2px 6px;
  border-radius: 6px;
}
.search-close:hover {
  background: var(--bg-hover);
  color: var(--text);
}
.search-result-item {
  padding: 9px 14px;
  margin: 2px 10px;
  border-radius: 12px;
  cursor: pointer;
  transition: background 0.15s;
  overflow: hidden;
}
.search-result-item:hover {
  background: var(--bg-hover);
}
.search-result-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.search-result-session {
  font-size: 13px;
  font-weight: 600;
  color: var(--text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.search-result-time {
  font-size: 11px;
  color: var(--text-secondary);
  flex-shrink: 0;
}
.search-result-content {
  font-size: 12px;
  color: var(--text-secondary);
  margin-top: 3px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.search-result-sender {
  color: var(--primary);
}
.search-more {
  display: block;
  width: calc(100% - 20px);
  margin: 8px 10px;
  padding: 7px 0;
  border: 1px solid var(--border);
  border-radius: 10px;
  background: transparent;
  color: var(--primary);
  font-size: 13px;
  cursor: pointer;
}
.search-more:disabled {
  opacity: 0.6;
  cursor: default;
}
.search-empty {
  text-align: center;
  padding: 40px 0;
  font-size: 13px;
  color: var(--text-secondary);
}

/* 列表 */
.contact-list {
  flex: 1;
  overflow-y: auto;
  padding-bottom: 8px;
}

.contact-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 14px;
  margin: 2px 10px;
  border-radius: 12px;
  cursor: pointer;
  transition: background 0.15s;
}

.contact-item:hover {
  background: var(--bg-hover);
}

.contact-item.active {
  background: var(--active-bg);
}

.avatar-wrap {
  position: relative;
  flex-shrink: 0;
}

/* 群聊标识：头像右下角徽标 */
.group-badge {
  position: absolute;
  right: -2px;
  bottom: -2px;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 5px;
  background: var(--primary);
  color: white;
  font-size: 10px;
  font-weight: 600;
  line-height: 16px;
  text-align: center;
  border: 1.5px solid var(--bg-white);
}

.contact-info {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.info-top,
.info-bottom {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.contact-meta {
  font-size: 12.5px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.session-time {
  font-size: 11.5px;
  color: var(--text-secondary);
  flex-shrink: 0;
}

.unread-badge {
  min-width: 18px;
  height: 18px;
  padding: 0 5px;
  border-radius: 9px;
  background: var(--danger);
  color: white;
  font-size: 11px;
  line-height: 18px;
  text-align: center;
  flex-shrink: 0;
}

/* 好友分组 */
.group-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 14px 16px 4px;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-secondary);
}

.group-count {
  font-weight: 400;
  font-size: 11px;
  background: var(--bg-hover);
  border-radius: 8px;
  padding: 0 6px;
  line-height: 16px;
}

.friend-more {
  width: 26px;
  height: 26px;
  border: none;
  border-radius: 50%;
  background: transparent;
  color: var(--text-secondary);
  font-size: 16px;
  line-height: 1;
  cursor: pointer;
  flex-shrink: 0;
  opacity: 0;
  transition: all 0.15s;
}

.contact-item:hover .friend-more,
.friend-more:focus-visible {
  opacity: 1;
}

.friend-more:hover {
  background: var(--bg-hover);
  color: var(--text);
}

/* 会话项图标 */
.pin-icon,
.mute-icon {
  font-size: 11px;
  margin-right: 2px;
}
</style>
