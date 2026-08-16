<template>
  <div class="chat-layout">
    <!-- 侧边栏 -->
    <aside class="sidebar" :class="{ 'is-hidden': mobileChatOpen }">
      <div class="sidebar-header">
        <div class="user-info">
          <span class="avatar" :style="{ background: avatarGradient(auth.user?.nickname || '') }">
            {{ avatarInitial(auth.user?.nickname || '') }}
          </span>
          <div class="user-text">
            <span class="nickname">{{ auth.user?.nickname }}</span>
            <span class="account-id" @click="copyAccountId" :title="copyTip">{{ copyTip }}</span>
          </div>
        </div>
        <div class="header-actions">
          <button class="icon-btn" @click="openRequestsModal" title="好友申请">
            <svg viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
              <path d="M13.73 21a2 2 0 0 1-3.46 0" />
            </svg>
            <span v-if="friendStore.pendingRequests.length" class="badge">{{ friendStore.pendingRequests.length }}</span>
          </button>
          <button class="icon-btn" @click="handleLogout" title="退出登录">
            <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
              <polyline points="16 17 21 12 16 7" />
              <line x1="21" y1="12" x2="9" y2="12" />
            </svg>
          </button>
        </div>
      </div>

      <!-- Tab 切换 -->
      <div class="tabs">
        <button :class="['tab', { active: activeTab === 'sessions' }]" @click="activeTab = 'sessions'">会话</button>
        <button :class="['tab', { active: activeTab === 'friends' }]" @click="activeTab = 'friends'">好友</button>
        <button :class="['tab', { active: activeTab === 'groups' }]" @click="activeTab = 'groups'">群组</button>
      </div>

      <!-- 操作栏 -->
      <div class="action-bar">
        <button class="btn btn-primary" @click="openAddModal">
          {{ activeTab === 'friends' ? '+ 添加好友' : activeTab === 'groups' ? '+ 创建群组' : '＋' }}
        </button>
      </div>

      <!-- 会话列表 -->
      <div class="contact-list" v-if="activeTab === 'sessions'">
        <div v-for="s in sortedSessions" :key="s.type + '_' + s.id"
          :class="['contact-item', { active: currentChat?.type === s.type && currentChat.id === s.id }]"
          @click="selectSession(s)">
          <span class="avatar small" :style="{ background: avatarGradient(s.name) }">{{ avatarInitial(s.name) }}</span>
          <div class="contact-info">
            <div class="info-top">
              <span class="contact-name">{{ s.name }}</span>
              <span class="session-time">{{ timeFor(s) }}</span>
            </div>
            <div class="info-bottom">
              <span class="contact-meta">{{ previewFor(s) }}</span>
              <span v-if="unreadOf(s.type, s.id)" class="unread-badge">{{ unreadOf(s.type, s.id) }}</span>
            </div>
          </div>
        </div>
        <div class="empty" v-if="chatStore.sessions.length === 0">
          <span class="empty-icon">💬</span>
          <span>暂无会话，去添加好友开始聊天吧</span>
        </div>
      </div>

      <!-- 好友列表 -->
      <div class="contact-list" v-else-if="activeTab === 'friends'">
        <div v-for="f in friendStore.friends" :key="f.userId"
          :class="['contact-item', { active: currentChat?.type === 'private' && currentChat.id === f.userId }]"
          @click="selectPrivateChat(f)">
          <div class="avatar-wrap">
            <span class="avatar small" :style="{ background: avatarGradient(f.nickname) }">{{ avatarInitial(f.nickname) }}</span>
            <span :class="['status-dot', f.isOnline ? 'online' : 'offline']"></span>
          </div>
          <div class="contact-info">
            <div class="info-top">
              <span class="contact-name">{{ f.nickname }}</span>
              <span v-if="unreadOf('private', f.userId)" class="unread-badge">{{ unreadOf('private', f.userId) }}</span>
            </div>
            <div class="info-bottom">
              <span class="contact-meta">{{ lastMessageFor('private', f.userId) || (f.isOnline ? '在线' : '离线') }}</span>
            </div>
          </div>
        </div>
        <div class="empty" v-if="friendStore.friends.length === 0">
          <span class="empty-icon">👥</span>
          <span>暂无好友，点击上方按钮添加</span>
        </div>
      </div>

      <!-- 群组列表 -->
      <div class="contact-list" v-else>
        <div v-for="g in groupStore.groups" :key="g.id"
          :class="['contact-item', { active: currentChat?.type === 'group' && currentChat.id === g.id }]"
          @click="selectGroupChat(g)">
          <span class="avatar small" :style="{ background: avatarGradient(g.name) }">#</span>
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

    <!-- 聊天区域 -->
    <main class="chat-main" :class="{ 'is-show': mobileChatOpen }">
      <!-- 未选择会话 -->
      <div class="no-chat" v-if="!currentChat">
        <span class="empty-icon">💬</span>
        <p>选择一个会话开始聊天</p>
      </div>

      <!-- 聊天窗口 -->
      <template v-else>
        <div class="chat-header">
          <button class="back-btn" @click="backToList" title="返回">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <polyline points="15 18 9 12 15 6" />
            </svg>
          </button>
          <span class="avatar small" :style="{ background: avatarGradient(currentChat.name) }">{{ avatarInitial(currentChat.name) }}</span>
          <div class="chat-title">
            <span class="chat-name">{{ currentChat.name }}</span>
            <span class="chat-sub">{{ chatSub }}</span>
          </div>
        </div>

        <div class="chat-messages" ref="msgContainer">
          <div v-for="msg in currentMessages" :key="msg.messageId"
            :class="['msg-row', { mine: Number(msg.from) === auth.user?.id }]">
            <span class="avatar msg-avatar" :style="{ background: avatarGradient(msg.senderName || currentChat.name) }">
              {{ avatarInitial(msg.senderName || currentChat.name) }}
            </span>
            <div class="msg-body">
              <span class="msg-sender" v-if="currentChat.type === 'group' && Number(msg.from) !== auth.user?.id">
                {{ msg.senderName }}
              </span>
              <div class="msg-bubble">{{ msg.content }}</div>
              <span class="msg-time">{{ formatMsgTime(msg.timestamp) }}</span>
            </div>
          </div>
        </div>

        <div class="chat-input-bar">
          <!-- 表情面板 -->
          <div class="emoji-panel" ref="emojiPanelRef" v-if="showEmojiPanel" @click.stop>
            <div class="emoji-group" v-for="g in emojiGroups" :key="g.name">
              <div class="emoji-group-title">{{ g.name }}</div>
              <div class="emoji-grid">
                <button v-for="e in g.list" :key="e" class="emoji-item" @click="insertEmoji(e)">{{ e }}</button>
              </div>
            </div>
          </div>
          <button class="emoji-btn" ref="emojiBtnRef" @click.stop="toggleEmojiPanel" title="表情">
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="12" cy="12" r="10" />
              <path d="M8 14s1.5 2 4 2 4-2 4-2" />
              <line x1="9" y1="9" x2="9.01" y2="9" />
              <line x1="15" y1="9" x2="15.01" y2="9" />
            </svg>
          </button>
          <input ref="inputEl" v-model="inputText" class="input" placeholder="输入消息…" @keydown.enter="onInputEnter" />
          <button class="send-btn" @click="send" :disabled="!inputText.trim()">发送</button>
        </div>
        <p class="send-hint" v-if="sendHint">{{ sendHint }}</p>
      </template>
    </main>

    <!-- 好友申请弹窗 -->
    <div class="modal-overlay" v-if="showRequestsModal" @click.self="showRequestsModal = false">
      <div class="modal">
        <h3>好友申请</h3>
        <div class="empty" v-if="friendStore.pendingRequests.length === 0">
          <span class="empty-icon">📭</span>
          <span>暂无待处理的申请</span>
        </div>
        <div v-for="r in friendStore.pendingRequests" :key="r.id" class="request-item">
          <span class="avatar small" :style="{ background: avatarGradient(r.nickname) }">{{ avatarInitial(r.nickname) }}</span>
          <div class="request-info">
            <span class="request-name">{{ r.nickname }}</span>
            <span class="request-meta">账号 {{ r.userId }}</span>
          </div>
          <div class="request-actions">
            <button class="btn btn-sm btn-primary" :disabled="handlingRequestId === r.id" @click="acceptRequest(r)">接受</button>
            <button class="btn btn-sm btn-ghost" :disabled="handlingRequestId === r.id" @click="rejectRequest(r)">拒绝</button>
          </div>
        </div>
        <p class="modal-error" v-if="requestError">{{ requestError }}</p>
        <button class="btn btn-ghost" @click="showRequestsModal = false">关闭</button>
      </div>
    </div>

    <!-- 添加弹窗 -->
    <div class="modal-overlay" v-if="showAddModal" @click.self="showAddModal = false">
      <div class="modal">
        <h3>{{ activeTab === 'friends' ? '添加好友' : '创建群组' }}</h3>
        <template v-if="activeTab === 'friends'">
          <input v-model="addFriendAccount" class="input" type="text" inputmode="numeric" placeholder="输入对方账号 ID" @keyup.enter="addFriend" />
          <button class="btn btn-primary" @click="addFriend">发送申请</button>
        </template>
        <template v-else>
          <input v-model="newGroupName" class="input" placeholder="输入群组名称" @keyup.enter="createGroup" />
          <button class="btn btn-primary" @click="createGroup">创建</button>
        </template>
        <p class="modal-error" v-if="modalError">{{ modalError }}</p>
        <p class="modal-success" v-if="modalSuccess">{{ modalSuccess }}</p>
        <button class="btn btn-ghost" @click="showAddModal = false">关闭</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import { emojiGroups } from '@/constants/emojis'
import type { FriendRequestInfo, WsMessage, ChatType, SessionInfo } from '@/types'

const router = useRouter()
const auth = useAuthStore()
const friendStore = useFriendStore()
const groupStore = useGroupStore()
const chatStore = useChatStore()
const ws = useWebSocketStore()

const activeTab = ref<'sessions' | 'friends' | 'groups'>('friends')
const inputText = ref('')
const inputEl = ref<HTMLInputElement | null>(null)
const msgContainer = ref<HTMLElement | null>(null)
const showAddModal = ref(false)
const addFriendAccount = ref('')
const newGroupName = ref('')
const modalError = ref('')
const modalSuccess = ref('')
const showRequestsModal = ref(false)
const handlingRequestId = ref<number | null>(null)
const requestError = ref('')
const sendHint = ref('')
// 移动端：聊天窗口全屏开关
const mobileChatOpen = ref(false)
// 表情面板
const showEmojiPanel = ref(false)
const emojiPanelRef = ref<HTMLElement | null>(null)
const emojiBtnRef = ref<HTMLElement | null>(null)
// 账号 ID 复制提示
const copyTip = ref('')
let copyTipTimer: number | null = null

const currentChat = ref<{ type: ChatType; id: number; name: string } | null>(null)

const currentMessages = computed(() => {
  if (!currentChat.value) return []
  const key = `${currentChat.value.type}_${currentChat.value.id}`
  return chatStore.messages.get(key) || []
})

// 会话列表按最后消息时间倒序
const sortedSessions = computed(() =>
  [...chatStore.sessions].sort((a, b) => new Date(b.lastTime).getTime() - new Date(a.lastTime).getTime())
)

// 聊天窗口副标题：私聊显示在线状态，群聊显示人数
const chatSub = computed(() => {
  if (!currentChat.value) return ''
  if (currentChat.value.type === 'private') {
    const f = friendStore.friends.find(x => x.userId === currentChat.value!.id)
    return f ? (f.isOnline ? '在线' : '离线') : ''
  }
  const g = groupStore.groups.find(x => x.id === currentChat.value!.id)
  return g ? `${g.memberCount} 人` : ''
})

// ==================== 头像 ====================
const avatarColors = [  'linear-gradient(135deg, #5b6cff, #9c6bff)',
  'linear-gradient(135deg, #00c6fb, #005bea)',
  'linear-gradient(135deg, #f093fb, #f5576c)',
  'linear-gradient(135deg, #4facfe, #00f2fe)',
  'linear-gradient(135deg, #43e97b, #38b6f9)',
  'linear-gradient(135deg, #fa709a, #fee140)',
  'linear-gradient(135deg, #a18cd1, #fbc2eb)',
  'linear-gradient(135deg, #f83600, #f9d423)'
]

/** 按名称哈希生成稳定的渐变头像背景 */
function avatarGradient(name: string): string {
  let h = 0
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0
  return avatarColors[h % avatarColors.length]
}

function avatarInitial(name: string): string {
  return (name || '?').charAt(0).toUpperCase()
}

// ==================== 时间 ====================
function pad(n: number): string {
  return String(n).padStart(2, '0')
}

function formatMsgTime(ts: number): string {
  if (!ts) return ''
  const d = new Date(ts)
  const now = new Date()
  const hm = `${pad(d.getHours())}:${pad(d.getMinutes())}`
  if (d.toDateString() === now.toDateString()) return hm
  if (d.getFullYear() === now.getFullYear()) return `${d.getMonth() + 1}/${d.getDate()} ${hm}`
  return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()} ${hm}`
}

onMounted(async () => {
  if (!auth.isLoggedIn) {
    router.push('/login')
    return
  }
  await auth.fetchUser()
  copyTip.value = auth.user ? `ID: ${auth.user.id}` : ''

  // 先注册回调，再建立连接，避免漏掉连接期间的消息
  ws.onMessage((msg) => {
    handleWsMessage(msg)
  })

  ws.onStatusChange((online, userId) => {
    friendStore.updateOnlineStatus(userId, online)
  })

  ws.connect(auth.token)
  await Promise.all([
    friendStore.fetchFriends(),
    friendStore.fetchPendingRequests(),
    groupStore.fetchGroups(),
    chatStore.fetchSessions()
  ])

  // 拉取离线消息并计入未读角标
  try {
    if (auth.user) {
      const counts = await chatStore.loadOfflineMessages(auth.user.id)
      for (const [key, count] of counts) {
        chatStore.setUnreadCount(key, count)
      }
    }
  } catch { /* 离线消息拉取失败不阻塞界面 */ }

  // 点击面板/按钮外部时关闭表情面板
  document.addEventListener('click', onDocClick)
})

onUnmounted(() => {
  document.removeEventListener('click', onDocClick)
  if (copyTipTimer) clearTimeout(copyTipTimer)
})

function onDocClick(e: MouseEvent) {
  const target = e.target as HTMLElement
  if (!emojiPanelRef.value?.contains(target) && !emojiBtnRef.value?.contains(target)) {
    showEmojiPanel.value = false
  }
}

// ==================== 表情 ====================
function toggleEmojiPanel() {
  showEmojiPanel.value = !showEmojiPanel.value
}

/** 在输入框光标位置插入表情，并恢复焦点与光标 */
function insertEmoji(emoji: string) {
  const el = inputEl.value
  if (el) {
    const start = el.selectionStart ?? inputText.value.length
    const end = el.selectionEnd ?? inputText.value.length
    inputText.value = inputText.value.slice(0, start) + emoji + inputText.value.slice(end)
    nextTick(() => {
      el.focus()
      const pos = start + emoji.length
      el.setSelectionRange(pos, pos)
    })
  } else {
    inputText.value += emoji
  }
}

/** 回车发送（中文输入法组词回车不误发） */
function onInputEnter(e: KeyboardEvent) {
  if (e.isComposing) return
  send()
}

function scrollToBottom() {
  nextTick(() => {
    if (msgContainer.value) {
      msgContainer.value.scrollTop = msgContainer.value.scrollHeight
    }
  })
}

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

function selectPrivateChat(friend: { userId: number; nickname: string }) {
  currentChat.value = { type: 'private', id: friend.userId, name: friend.nickname }
  chatStore.setCurrentSession('private', friend.userId, friend.nickname)
  chatStore.loadHistory('private', friend.userId)
  chatStore.markSessionRead('private', friend.userId)
  mobileChatOpen.value = true
  scrollToBottom()
}

function selectGroupChat(group: { id: number; name: string }) {
  currentChat.value = { type: 'group', id: group.id, name: group.name }
  chatStore.setCurrentSession('group', group.id, group.name)
  chatStore.loadHistory('group', group.id)
  chatStore.markSessionRead('group', group.id)
  mobileChatOpen.value = true
  scrollToBottom()
}

function selectSession(s: SessionInfo) {
  if (s.type === 'private') {
    selectPrivateChat({ userId: s.id, nickname: s.name })
  } else {
    selectGroupChat({ id: s.id, name: s.name })
  }
}

/** 移动端：返回会话列表 */
function backToList() {
  mobileChatOpen.value = false
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

function handleWsMessage(msg: WsMessage) {
  // 新好友申请：刷新申请列表
  if (msg.type === 'friend_request') {
    friendStore.fetchPendingRequests()
    return
  }
  // 好友申请被接受/拒绝：双方刷新好友与申请列表
  if (msg.type === 'friend_accepted' || msg.type === 'friend_rejected') {
    friendStore.fetchFriends()
    friendStore.fetchPendingRequests()
    chatStore.fetchSessions()
    return
  }
  // 在线状态已由 onStatusChange 处理
  if (msg.type === 'online_status') return

  const { key, isNew } = chatStore.addMessage(msg, auth.user?.id)
  const currentKey = currentChat.value ? chatStore.sessionKey(currentChat.value.type, currentChat.value.id) : ''
  if (key === currentKey) {
    // 当前会话：清未读、标记已读、滚到底部
    chatStore.markSessionRead(currentChat.value!.type, currentChat.value!.id)
    scrollToBottom()
  } else if (isNew) {
    chatStore.bumpUnread(key)
  }
}

function genId(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID()
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}

function send() {
  if (!currentChat.value || !auth.user) return
  const text = inputText.value.trim()
  if (!text) return
  if (!ws.connected) {
    sendHint.value = '连接已断开，正在重连，请稍候…'
    return
  }
  sendHint.value = ''
  const msg: WsMessage = {
    type: currentChat.value.type === 'private' ? 'private_message' : 'group_message',
    from: String(auth.user.id),
    to: String(currentChat.value.id),
    content: text,
    timestamp: Date.now(),
    messageId: genId(), // 客户端 ID，服务端回显时保留，用于去重
    messageType: 0,
    senderName: auth.user.nickname,
    senderAvatar: auth.user.avatar
  }
  chatStore.addMessage(msg, auth.user.id) // 乐观插入，回显到达后自动去重
  ws.sendMessage(msg)
  inputText.value = ''
  scrollToBottom()
}

function openAddModal() {
  modalError.value = ''
  modalSuccess.value = ''
  showAddModal.value = true
}

function openRequestsModal() {
  requestError.value = ''
  showRequestsModal.value = true
}

async function addFriend() {
  const account = Number(addFriendAccount.value.trim())
  if (!account || account <= 0) {
    modalError.value = '请输入正确的账号 ID'
    modalSuccess.value = ''
    return
  }
  const res = await friendStore.sendRequest(account)
  if (res.success) {
    modalSuccess.value = '好友申请已发送'
    modalError.value = ''
    addFriendAccount.value = ''
  } else {
    modalError.value = res.message
    modalSuccess.value = ''
  }
}

async function createGroup() {
  const res = await groupStore.createGroup(newGroupName.value)
  if (res.success) {
    modalSuccess.value = '群组创建成功'
    modalError.value = ''
    newGroupName.value = ''
  } else {
    modalError.value = res.message
    modalSuccess.value = ''
  }
}

async function acceptRequest(r: FriendRequestInfo) {
  handlingRequestId.value = r.id
  requestError.value = ''
  try {
    const res = await friendStore.acceptRequest(r.id)
    if (!res.success) requestError.value = res.message
  } finally {
    handlingRequestId.value = null
  }
}

async function rejectRequest(r: FriendRequestInfo) {
  handlingRequestId.value = r.id
  requestError.value = ''
  try {
    const res = await friendStore.rejectRequest(r.id)
    if (!res.success) requestError.value = res.message
  } finally {
    handlingRequestId.value = null
  }
}

/** 点击账号 ID 复制到剪贴板 */
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

function handleLogout() {
  ws.disconnect()
  auth.logout()
  router.push('/login')
}

watch(activeTab, () => {
  if (activeTab.value === 'friends') friendStore.fetchFriends()
  else if (activeTab.value === 'groups') groupStore.fetchGroups()
  else chatStore.fetchSessions()
})
</script>

<style scoped>
.chat-layout {
  display: flex;
  height: 100vh;
  height: 100dvh;
}

/* ==================== 侧边栏 ==================== */
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
  background: #eef1ff;
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
  background: #eef1ff;
}

.avatar-wrap {
  position: relative;
  flex-shrink: 0;
}

.status-dot {
  position: absolute;
  right: -1px;
  bottom: -1px;
  width: 11px;
  height: 11px;
  border-radius: 50%;
  border: 2px solid var(--bg-white);
}

.status-dot.online { background: var(--online); }
.status-dot.offline { background: var(--offline); }

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

.contact-name {
  font-weight: 600;
  font-size: 15px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
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

.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 48px 20px;
  color: var(--text-secondary);
  font-size: 13.5px;
  text-align: center;
}

.empty-icon {
  font-size: 40px;
  opacity: 0.55;
}

/* ==================== 聊天区域 ==================== */
.chat-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  background: var(--bg);
}

.no-chat {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  color: var(--text-secondary);
  font-size: 15px;
}

.chat-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 18px;
  background: var(--bg-white);
  border-bottom: 1px solid var(--border);
  box-shadow: 0 1px 4px rgba(31, 35, 41, 0.04);
  z-index: 1;
}

.back-btn {
  display: none;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border: none;
  background: var(--bg-hover);
  border-radius: 50%;
  cursor: pointer;
  color: var(--text);
  flex-shrink: 0;
}

.back-btn:active {
  background: var(--border);
}

.chat-title {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.chat-name {
  font-weight: 600;
  font-size: 16px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.chat-sub {
  font-size: 12px;
  color: var(--text-secondary);
}

.chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 18px 20px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  background: linear-gradient(180deg, #f2f4f9 0%, #e9edf5 100%);
}

/* 消息行 */
.msg-row {
  display: flex;
  gap: 10px;
  align-items: flex-start;
  max-width: 78%;
}

.msg-row.mine {
  align-self: flex-end;
  flex-direction: row-reverse;
}

.msg-avatar {
  width: 34px;
  height: 34px;
  font-size: 14px;
}

.msg-row.mine .msg-avatar {
  display: none;
}

.msg-body {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 3px;
  min-width: 0;
}

.msg-row.mine .msg-body {
  align-items: flex-end;
}

.msg-sender {
  font-size: 12px;
  color: var(--text-secondary);
  margin-left: 2px;
}

.msg-bubble {
  padding: 9px 13px;
  border-radius: 14px;
  font-size: 14.5px;
  line-height: 1.55;
  word-break: break-word;
  box-shadow: var(--shadow-sm);
}

.msg-row:not(.mine) .msg-bubble {
  background: var(--bg-white);
  border-top-left-radius: 4px;
}

.msg-row.mine .msg-bubble {
  background: var(--mine-bubble);
  color: white;
  border-top-right-radius: 4px;
}

.msg-time {
  font-size: 11px;
  color: var(--text-secondary);
  padding: 0 4px;
}

/* 输入区 */
.chat-input-bar {
  position: relative;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 16px calc(12px + env(safe-area-inset-bottom));
  background: var(--bg-white);
  border-top: 1px solid var(--border);
}

.emoji-btn {
  width: 38px;
  height: 38px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-radius: 50%;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s;
  flex-shrink: 0;
}

.emoji-btn:hover {
  background: var(--bg-hover);
  color: #f7b731;
}

.emoji-btn:active {
  transform: scale(0.92);
}

/* 表情面板 */
.emoji-panel {
  position: absolute;
  bottom: calc(100% + 10px);
  left: 12px;
  width: 340px;
  max-width: calc(100vw - 24px);
  max-height: 280px;
  overflow-y: auto;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-radius: 14px;
  box-shadow: var(--shadow);
  padding: 10px 12px 12px;
  z-index: 20;
  animation: modal-in 0.18s;
}

.emoji-group-title {
  font-size: 12px;
  color: var(--text-secondary);
  padding: 8px 2px 4px;
  font-weight: 500;
}

.emoji-group:first-child .emoji-group-title {
  padding-top: 2px;
}

.emoji-grid {
  display: grid;
  grid-template-columns: repeat(8, 1fr);
  gap: 2px;
}

.emoji-item {
  width: 100%;
  aspect-ratio: 1;
  border: none;
  background: transparent;
  border-radius: 8px;
  font-size: 21px;
  line-height: 1;
  cursor: pointer;
  transition: background 0.12s, transform 0.12s;
  display: flex;
  align-items: center;
  justify-content: center;
}

.emoji-item:hover {
  background: var(--bg-hover);
  transform: scale(1.15);
}

.emoji-item:active {
  transform: scale(0.92);
}

.chat-input-bar .input {
  flex: 1;
  border-radius: 20px;
  background: var(--bg-hover);
  border-color: transparent;
}

.chat-input-bar .input:focus {
  background: var(--bg-white);
}

.send-btn {
  padding: 9px 22px;
  border: none;
  border-radius: 20px;
  background: var(--mine-bubble);
  color: white;
  font-weight: 600;
  font-size: 14px;
  cursor: pointer;
  box-shadow: 0 4px 12px rgba(91, 108, 255, 0.35);
  transition: all 0.2s;
  flex-shrink: 0;
}

.send-btn:hover:not(:disabled) {
  filter: brightness(1.08);
}

.send-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
  box-shadow: none;
}

.send-hint {
  padding: 0 20px 10px;
  font-size: 12px;
  color: var(--danger);
  background: var(--bg-white);
}

/* ==================== 弹窗 ==================== */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 18, 26, 0.45);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 100;
  animation: fade-in 0.2s;
}

.modal {
  background: var(--bg-white);
  padding: 24px;
  border-radius: 16px;
  width: 420px;
  max-width: calc(100vw - 40px);
  max-height: 72vh;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 14px;
  box-shadow: var(--shadow);
  animation: modal-in 0.25s;
}

.modal h3 { font-size: 18px; }

.modal-error { color: var(--danger); font-size: 13px; }
.modal-success { color: var(--success); font-size: 13px; }

/* 好友申请列表 */
.request-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 0;
  border-bottom: 1px solid var(--border);
}

.request-item:last-of-type {
  border-bottom: none;
}

.request-info {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.request-name {
  font-weight: 600;
  font-size: 14px;
}

.request-meta {
  font-size: 12px;
  color: var(--text-secondary);
}

.request-actions {
  display: flex;
  gap: 8px;
}

.request-actions .btn {
  padding: 6px 14px;
  font-size: 12px;
}

/* ==================== 头像 ==================== */
.avatar {
  width: 44px;
  height: 44px;
  border-radius: 50%;
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 600;
  font-size: 18px;
  flex-shrink: 0;
  user-select: none;
  box-shadow: inset 0 -2px 6px rgba(0, 0, 0, 0.08);
}

.avatar.small {
  width: 38px;
  height: 38px;
  font-size: 15px;
}

/* ==================== 移动端适配 ==================== */
@media (max-width: 720px) {
  .sidebar {
    width: 100%;
  }

  .sidebar.is-hidden {
    display: none;
  }

  .chat-main {
    display: none;
    width: 100%;
  }

  .chat-main.is-show {
    display: flex;
  }

  .back-btn {
    display: flex;
  }

  .msg-row {
    max-width: 85%;
  }

  .chat-messages {
    padding: 14px 12px;
  }

  .emoji-panel {
    left: 8px;
    right: 8px;
    width: auto;
  }

  .emoji-grid {
    grid-template-columns: repeat(7, 1fr);
  }

  .modal {
    padding: 20px;
  }
}

@media (min-width: 721px) {
  .back-btn {
    display: none;
  }
}
</style>
