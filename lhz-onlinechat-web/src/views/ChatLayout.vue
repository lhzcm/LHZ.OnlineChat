<template>
  <div class="chat-layout">
    <!-- 侧边栏 -->
    <aside class="sidebar">
      <div class="sidebar-header">
        <div class="user-info">
          <span class="avatar-placeholder">{{ auth.user?.nickname?.charAt(0) }}</span>
          <span class="nickname">{{ auth.user?.nickname }}</span>
        </div>
        <div class="header-actions">
          <button class="btn btn-ghost btn-icon" @click="openRequestsModal" title="好友申请">
            🔔
            <span v-if="friendStore.pendingRequests.length" class="badge">{{ friendStore.pendingRequests.length }}</span>
          </button>
          <button class="btn btn-ghost btn-icon" @click="handleLogout" title="退出">⚙</button>
        </div>
      </div>

      <!-- Tab 切换 -->
      <div class="tabs">
        <button :class="['tab', { active: activeTab === 'sessions' }]" @click="activeTab = 'sessions'">
          会话
        </button>
        <button :class="['tab', { active: activeTab === 'friends' }]" @click="activeTab = 'friends'">
          好友
        </button>
        <button :class="['tab', { active: activeTab === 'groups' }]" @click="activeTab = 'groups'">
          群组
        </button>
      </div>

      <!-- 搜索/操作栏 -->
      <div class="action-bar">
        <button class="btn btn-primary btn-sm" @click="openAddModal">
          {{ activeTab === 'friends' ? '+ 添加好友' : activeTab === 'groups' ? '+ 创建群组' : '＋' }}
        </button>
      </div>

      <!-- 会话列表 -->
      <div class="contact-list" v-if="activeTab === 'sessions'">
        <div v-for="s in sortedSessions" :key="s.type + '_' + s.id"
          :class="['contact-item', { active: currentChat?.type === s.type && currentChat.id === s.id }]"
          @click="selectSession(s)">
          <span class="avatar-placeholder small">{{ s.type === 'group' ? '#' : s.name.charAt(0) }}</span>
          <div class="contact-info">
            <span class="contact-name">{{ s.name }}</span>
            <span class="contact-meta">{{ previewFor(s) }}</span>
          </div>
          <div class="session-right">
            <span class="session-time">{{ timeFor(s) }}</span>
            <span v-if="unreadOf(s.type, s.id)" class="unread-badge">{{ unreadOf(s.type, s.id) }}</span>
          </div>
        </div>
        <p class="empty" v-if="chatStore.sessions.length === 0">暂无会话</p>
      </div>

      <!-- 好友列表 -->
      <div class="contact-list" v-else-if="activeTab === 'friends'">
        <div v-for="f in friendStore.friends" :key="f.userId"
          :class="['contact-item', { active: currentChat?.type === 'private' && currentChat.id === f.userId }]"
          @click="selectPrivateChat(f)">
          <span :class="['status-dot', f.isOnline ? 'online' : 'offline']"></span>
          <span class="avatar-placeholder small">{{ f.nickname.charAt(0) }}</span>
          <div class="contact-info">
            <span class="contact-name">{{ f.nickname }}</span>
            <span class="contact-meta">{{ lastMessageFor('private', f.userId) || (f.isOnline ? '在线' : '离线') }}</span>
          </div>
          <span v-if="unreadOf('private', f.userId)" class="unread-badge">{{ unreadOf('private', f.userId) }}</span>
        </div>
        <p class="empty" v-if="friendStore.friends.length === 0">暂无好友</p>
      </div>

      <!-- 群组列表 -->
      <div class="contact-list" v-else>
        <div v-for="g in groupStore.groups" :key="g.id"
          :class="['contact-item', { active: currentChat?.type === 'group' && currentChat.id === g.id }]"
          @click="selectGroupChat(g)">
          <span class="avatar-placeholder small">#</span>
          <div class="contact-info">
            <span class="contact-name">{{ g.name }}</span>
            <span class="contact-meta">{{ lastMessageFor('group', g.id) || `${g.memberCount} 人` }}</span>
          </div>
          <span v-if="unreadOf('group', g.id)" class="unread-badge">{{ unreadOf('group', g.id) }}</span>
        </div>
        <p class="empty" v-if="groupStore.groups.length === 0">暂无群组</p>
      </div>
    </aside>

    <!-- 聊天区域 -->
    <main class="chat-main">
      <!-- 未选择会话 -->
      <div class="no-chat" v-if="!currentChat">
        <p>选择一个会话开始聊天</p>
      </div>

      <!-- 聊天窗口 -->
      <template v-else>
        <div class="chat-header">
          <span>{{ currentChat.name }}</span>
        </div>
        <div class="chat-messages" ref="msgContainer">
          <div v-for="msg in currentMessages" :key="msg.messageId"
            :class="['message', { mine: Number(msg.from) === auth.user?.id }]">
            <span class="msg-sender" v-if="currentChat.type === 'group' && Number(msg.from) !== auth.user?.id">
              {{ msg.senderName }}
            </span>
            <div class="msg-bubble">{{ msg.content }}</div>
          </div>
        </div>
        <div class="chat-input-bar">
          <input v-model="inputText" class="input" placeholder="输入消息..." @keyup.enter="send" />
          <button class="btn btn-primary" @click="send">发送</button>
        </div>
        <p class="send-hint" v-if="sendHint">{{ sendHint }}</p>
      </template>
    </main>

    <!-- 好友申请弹窗 -->
    <div class="modal-overlay" v-if="showRequestsModal" @click.self="showRequestsModal = false">
      <div class="modal">
        <h3>好友申请</h3>
        <p class="empty" v-if="friendStore.pendingRequests.length === 0">暂无待处理的申请</p>
        <div v-for="r in friendStore.pendingRequests" :key="r.id" class="request-item">
          <span class="avatar-placeholder small">{{ r.nickname.charAt(0) }}</span>
          <div class="request-info">
            <span class="request-name">{{ r.nickname }}</span>
            <span class="request-meta">{{ r.username }}</span>
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
          <input v-model="addFriendUsername" class="input" placeholder="输入好友用户名" />
          <button class="btn btn-primary" @click="addFriend">发送申请</button>
        </template>
        <template v-else>
          <input v-model="newGroupName" class="input" placeholder="输入群组名称" />
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
import { ref, computed, watch, nextTick, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import type { FriendRequestInfo, WsMessage, ChatType, SessionInfo } from '@/types'

const router = useRouter()
const auth = useAuthStore()
const friendStore = useFriendStore()
const groupStore = useGroupStore()
const chatStore = useChatStore()
const ws = useWebSocketStore()

const activeTab = ref<'sessions' | 'friends' | 'groups'>('friends')
const inputText = ref('')
const msgContainer = ref<HTMLElement | null>(null)
const showAddModal = ref(false)
const addFriendUsername = ref('')
const newGroupName = ref('')
const modalError = ref('')
const modalSuccess = ref('')
const showRequestsModal = ref(false)
const handlingRequestId = ref<number | null>(null)
const requestError = ref('')
const sendHint = ref('')

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

onMounted(async () => {
  if (!auth.isLoggedIn) {
    router.push('/login')
    return
  }
  await auth.fetchUser()

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
})

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
  scrollToBottom()
}

function selectGroupChat(group: { id: number; name: string }) {
  currentChat.value = { type: 'group', id: group.id, name: group.name }
  chatStore.setCurrentSession('group', group.id, group.name)
  chatStore.loadHistory('group', group.id)
  chatStore.markSessionRead('group', group.id)
  scrollToBottom()
}

function selectSession(s: SessionInfo) {
  if (s.type === 'private') {
    selectPrivateChat({ userId: s.id, nickname: s.name })
  } else {
    selectGroupChat({ id: s.id, name: s.name })
  }
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
    ? `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
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
  const res = await friendStore.sendRequest(addFriendUsername.value)
  if (res.success) {
    modalSuccess.value = '好友申请已发送'
    modalError.value = ''
    addFriendUsername.value = ''
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
}

/* Sidebar */
.sidebar {
  width: 300px;
  background: var(--bg-white);
  border-right: 1px solid var(--border);
  display: flex;
  flex-direction: column;
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px;
  border-bottom: 1px solid var(--border);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 4px;
}

.btn-icon {
  position: relative;
  padding: 6px 8px;
  font-size: 16px;
  line-height: 1;
}

.badge {
  position: absolute;
  top: -4px;
  right: -4px;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 8px;
  background: var(--danger);
  color: white;
  font-size: 11px;
  line-height: 16px;
  text-align: center;
}

.user-info {
  display: flex;
  align-items: center;
  gap: 10px;
}

.avatar-placeholder {
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: var(--primary);
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: bold;
  font-size: 18px;
}

.avatar-placeholder.small {
  width: 36px;
  height: 36px;
  font-size: 16px;
  flex-shrink: 0;
}

.nickname {
  font-weight: 500;
}

.tabs {
  display: flex;
  border-bottom: 1px solid var(--border);
}

.tab {
  flex: 1;
  padding: 12px;
  border: none;
  background: none;
  cursor: pointer;
  font-size: 14px;
  color: var(--text-secondary);
  transition: all 0.2s;
}

.tab.active {
  color: var(--primary);
  border-bottom: 2px solid var(--primary);
}

.action-bar {
  padding: 12px;
}

.btn-sm {
  width: 100%;
  padding: 8px 16px;
  font-size: 13px;
}

.contact-list {
  flex: 1;
  overflow-y: auto;
}

.contact-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 16px;
  cursor: pointer;
  transition: background 0.15s;
}

.contact-item:hover { background: var(--bg); }
.contact-item.active { background: #eef2ff; }

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}

.status-dot.online { background: var(--online); }
.status-dot.offline { background: var(--offline); }

.contact-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.contact-name {
  font-weight: 500;
  font-size: 14px;
}

.contact-meta {
  font-size: 12px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
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

.session-right {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
  flex-shrink: 0;
}

.session-time {
  font-size: 11px;
  color: var(--text-secondary);
}

.empty {
  text-align: center;
  padding: 40px;
  color: var(--text-secondary);
  font-size: 14px;
}

/* Chat Main */
.chat-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  background: var(--bg);
}

.no-chat {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-secondary);
  font-size: 16px;
}

.chat-header {
  padding: 16px 20px;
  background: var(--bg-white);
  border-bottom: 1px solid var(--border);
  font-weight: 500;
}

.chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.message {
  display: flex;
  flex-direction: column;
  max-width: 60%;
}

.message.mine {
  align-self: flex-end;
}

.msg-sender {
  font-size: 12px;
  color: var(--text-secondary);
  margin-bottom: 2px;
  margin-left: 4px;
}

.msg-bubble {
  padding: 10px 14px;
  border-radius: 12px;
  font-size: 14px;
  line-height: 1.5;
  word-break: break-word;
}

.message:not(.mine) .msg-bubble {
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-top-left-radius: 4px;
}

.message.mine .msg-bubble {
  background: var(--primary);
  color: white;
  border-top-right-radius: 4px;
}

.chat-input-bar {
  display: flex;
  gap: 10px;
  padding: 14px 20px 4px;
  background: var(--bg-white);
  border-top: 1px solid var(--border);
}

.chat-input-bar .input {
  flex: 1;
}

.chat-input-bar .btn {
  padding: 10px 24px;
}

.send-hint {
  padding: 0 20px 10px;
  font-size: 12px;
  color: var(--danger);
  background: var(--bg-white);
}

/* Modal */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0,0,0,0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 100;
}

.modal {
  background: var(--bg-white);
  padding: 30px;
  border-radius: 12px;
  width: 420px;
  max-height: 70vh;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.modal h3 { font-size: 18px; }

.modal-error { color: var(--danger); font-size: 13px; }
.modal-success { color: var(--online); font-size: 13px; }

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
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.request-name {
  font-weight: 500;
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
</style>
