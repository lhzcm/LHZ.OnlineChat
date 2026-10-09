<template>
  <div class="chat-layout">
    <ChatSidebar v-model:active-tab="activeTab" :current-chat="currentChat" :mobile-chat-open="mobileChatOpen"
      @select-private="selectPrivateChat" @select-group="selectGroupChat" @select-session="selectSession"
      @session-setting="openSessionSetting" @friend-setting="openFriendSetting" @add="openAddModal"
      @requests="openRequestsModal" @robot="showRobotModal = true" @profile="showProfileModal = true"
      @logout="handleLogout" @open-result="openSearchResult" />

    <!-- 聊天区域 -->
    <ChatArea ref="chatAreaRef" :chat="currentChat" :mobile-chat-open="mobileChatOpen"
      @back="backToList" @members="openMembersModal" @announcement="showAnnouncementModal = true" />

    <!-- 初始化数据加载失败：非致命错误条（不阻塞聊天，可重试） -->
    <div class="startup-error" v-if="startupError">
      <span class="startup-error-text">{{ startupError }}</span>
      <button class="startup-error-btn" @click="loadInitialData">重试</button>
      <button class="startup-error-close" @click="startupError = ''" title="关闭">✕</button>
    </div>

    <!-- WS 自动重连次数耗尽：必须给出口，否则界面看起来"一直在重连"却永远不会恢复 -->
    <div class="startup-error" v-if="ws.reconnectExhausted">
      <span class="startup-error-text">实时连接已断开（自动重连失败），消息可能无法即时收发</span>
      <button class="startup-error-btn" @click="reconnectNow">重新连接</button>
    </div>

    <!-- 轻提示 Toast -->
    <transition name="toast-fade">
      <div class="app-toast" v-if="toastMsg">{{ toastMsg }}</div>
    </transition>

    <!-- 好友设置弹窗（备注/分类） -->
    <FriendSettingModal v-if="showFriendSetting && friendSetting" :friend="friendSetting"
      @close="showFriendSetting = false" @saved="onFriendTagSaved" />

    <!-- 群成员面板（含邀请好友/添加机器人） -->
    <MembersModal v-if="showMembersModal && currentChat" :groupId="currentChat.id" :group-name="currentChat.name"
      @close="showMembersModal = false" />

    <!-- 群公告弹窗 -->
    <AnnouncementModal v-if="showAnnouncementModal && currentChat" :groupId="currentChat.id"
      @close="showAnnouncementModal = false" @saved="announcementSaved" />

    <!-- 个人资料弹窗 -->
    <ProfileModal v-if="showProfileModal" :notify-sound-enabled="notifySoundEnabled"
      @close="showProfileModal = false" @open-blacklist="showBlacklistModal = true"
      @open-sessions="showSessionsModal = true"
      @update:notify-sound-enabled="onNotifySoundChange" />

    <!-- 黑名单管理弹窗 -->
    <BlacklistModal v-if="showBlacklistModal" @close="showBlacklistModal = false" />

    <!-- 登录设备管理弹窗（多端登录） -->
    <SessionsModal v-if="showSessionsModal" @close="showSessionsModal = false" />

    <!-- 机器人管理/测试弹窗 -->
    <RobotManagerModal v-if="showRobotModal" @close="showRobotModal = false" />

    <!-- 好友申请弹窗 -->
    <RequestsModal v-if="showRequestsModal" @close="showRequestsModal = false" />

    <!-- 添加弹窗（添加好友/创建群组） -->
    <AddModal v-if="showAddModal" :mode="activeTab === 'friends' ? 'friend' : 'group'" @close="showAddModal = false" />

    <!-- 会话设置弹窗（置顶/免打扰） -->
    <SessionSettingModal v-if="showSessionSetting && sessionSettingTarget" :session="sessionSettingTarget" @close="showSessionSetting = false" />

    <!-- 图片放大预览 -->
    <div class="lightbox" v-if="lightboxUrl" @click="lightboxUrl = ''">
      <img :src="lightboxUrl" alt="图片预览" />
      <span class="lightbox-close">✕ 点击任意处关闭</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import { useToast } from '@/composables/useToast'
import { inDnd, showDesktopNotify } from '@/composables/useNotify'
import BlacklistModal from '@/components/chat/modals/BlacklistModal.vue'
import RobotManagerModal from '@/components/chat/modals/RobotManagerModal.vue'
import ProfileModal from '@/components/chat/modals/ProfileModal.vue'
import RequestsModal from '@/components/chat/modals/RequestsModal.vue'
import AddModal from '@/components/chat/modals/AddModal.vue'
import SessionSettingModal from '@/components/chat/modals/SessionSettingModal.vue'
import MembersModal from '@/components/chat/modals/MembersModal.vue'
import AnnouncementModal from '@/components/chat/modals/AnnouncementModal.vue'
import FriendSettingModal from '@/components/chat/modals/FriendSettingModal.vue'
import SessionsModal from '@/components/chat/modals/SessionsModal.vue'
import ChatSidebar from '@/components/chat/ChatSidebar.vue'
import ChatArea from '@/components/chat/ChatArea.vue'
import type { FriendInfo, WsMessage, ChatType, SessionInfo, MessageSearchResult } from '@/types'

const { toastMsg, toast } = useToast()

const router = useRouter()
const auth = useAuthStore()
const friendStore = useFriendStore()
const groupStore = useGroupStore()
const chatStore = useChatStore()
const ws = useWebSocketStore()

const activeTab = ref<'sessions' | 'friends' | 'groups'>('friends')
// 移动端：聊天窗口全屏开关
const mobileChatOpen = ref(false)
const lightboxUrl = ref('')
// 消息提示音
const notifySoundEnabled = ref(localStorage.getItem('notifySound') !== '0')
let notifyAudioCtx: AudioContext | null = null

function onNotifySoundChange(enabled: boolean) {
  notifySoundEnabled.value = enabled
  localStorage.setItem('notifySound', enabled ? '1' : '0')
}

/** 播放新消息提示音（Web Audio 合成，无需音频文件）；免打扰时段静音 */
function playNotifySound() {
  if (!notifySoundEnabled.value) return
  if (inDnd()) return
  try {
    notifyAudioCtx = notifyAudioCtx || new AudioContext()
    const ctx = notifyAudioCtx
    if (ctx.state === 'suspended') ctx.resume()
    const now = ctx.currentTime
    const notes = [880, 660]
    notes.forEach((freq, i) => {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      osc.frequency.value = freq
      const t = now + i * 0.15
      gain.gain.setValueAtTime(0.001, t)
      gain.gain.exponentialRampToValueAtTime(0.1, t + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.12)
      osc.connect(gain).connect(ctx.destination)
      osc.start(t)
      osc.stop(t + 0.14)
    })
  } catch { /* 忽略音频错误（如浏览器策略限制） */ }
}
// 群成员面板
const showMembersModal = ref(false)
// 好友设置（备注/分类）
const showFriendSetting = ref(false)
const friendSetting = ref<FriendInfo | null>(null)
// 个人资料
const showProfileModal = ref(false)
// 添加/申请/会话设置弹窗开关
const showAddModal = ref(false)
const showRequestsModal = ref(false)
const showSessionSetting = ref(false)
const sessionSettingTarget = ref<SessionInfo | null>(null)

const currentChat = ref<{ type: ChatType; id: number; name: string } | null>(null)

// 初始化数据加载失败提示（非致命：聊天本身仍可用）
const startupError = ref('')

// WS 回调的取消订阅函数：SPA 内部跳转不会刷新页面，卸载时必须调用（详见 onMounted 的说明）
let offMessage: (() => void) | null = null
let offStatusChange: (() => void) | null = null

/** 桌面通知点击 → 打开对应会话（具名函数：注册与移除必须是同一个引用） */
function handleOpenSession(e: Event) {
  const detail = (e as CustomEvent<{ type: ChatType; id: number }>).detail
  if (!detail) return
  if (detail.type === 'private') {
    const f = friendStore.friends.find(x => x.userId === detail.id)
    selectPrivateChat({ userId: detail.id, nickname: f ? (f.remark || f.nickname) : String(detail.id) })
  } else {
    const g = groupStore.groups.find(x => x.id === detail.id)
    selectGroupChat({ id: detail.id, name: g?.name || String(detail.id) })
  }
  mobileChatOpen.value = true
}

/**
 * 后台静默刷新：这类刷新由 WS 事件触发、调用方不等待结果，
 * 不 catch 会产生未处理的 Promise 拒绝（控制台报错且可能触发全局错误上报）。
 */
function refreshQuietly(promise: Promise<unknown>, label: string) {
  promise.catch(err => console.error(`[ChatLayout] ${label} 失败`, err))
}

/** 首页数据加载（好友 / 申请 / 群组 / 会话），失败只提示不阻塞 */
async function loadInitialData() {
  try {
    await Promise.all([
      friendStore.fetchFriends(),
      friendStore.fetchPendingRequests(),
      groupStore.fetchGroups(),
      chatStore.fetchSessions()
    ])
    startupError.value = ''
  } catch (err) {
    // Promise.all 会被任一失败中断，但成功的那些已写入 store，界面仍可正常使用
    console.error('[ChatLayout] 初始化数据加载失败', err)
    startupError.value = '部分数据加载失败，请检查网络后重试'
  }
  // 进入主页：有会话时默认显示会话列表，否则显示好友 Tab 引导添加
  if (chatStore.sessions.length > 0) {
    activeTab.value = 'sessions'
  }
}

// 好友显示名：备注优先，其次昵称
function friendDisplayName(f: FriendInfo): string {
  return f.remark || f.nickname
}

/** ChatArea 实例引用（新消息滚动/提示/定位） */
const chatAreaRef = ref<InstanceType<typeof ChatArea> | null>(null)

onMounted(async () => {
  if (!auth.isLoggedIn) {
    router.push('/login')
    return
  }
  await auth.fetchUser()

  // 桌面通知点击：跳转到对应会话
  window.addEventListener('oc:open-session', handleOpenSession)

  // 先注册回调，再建立连接，避免漏掉连接期间的消息。
  // 取消订阅函数必须留着并在卸载时调用：这个页面是 SPA 内部跳转（登出→登录不会刷新页面），
  // 不注销的话每次重新登录都会往回调数组里再塞一份闭包 ——
  // 未读角标、提示音、桌面通知、已读回执各被处理 N 次，
  // 而且旧闭包属于上一个账号，会把上一个账号的消息写进当前 store（串号）。
  offMessage = ws.onMessage((msg) => {
    handleWsMessage(msg)
  })

  offStatusChange = ws.onStatusChange((online, userId) => {
    friendStore.updateOnlineStatus(userId, online)
  })

  ws.connect(auth.token)
  await loadInitialData()

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

onUnmounted(() => {
  offMessage?.()
  offStatusChange?.()
  window.removeEventListener('oc:open-session', handleOpenSession)
  offMessage = null
  offStatusChange = null
})

/** 自动重连失败后的手动重连（界面上的「重新连接」按钮） */
function reconnectNow() {
  if (ws.connected) return
  ws.connect(auth.token)
}

async function openSearchResult(r: MessageSearchResult) {
  if (r.type === 'private') {
    await selectPrivateChat({ userId: r.sessionId, nickname: r.sessionName })
  } else {
    await selectGroupChat({ id: r.sessionId, name: r.sessionName })
  }
  if (r.messageId) await chatAreaRef.value?.scrollToMessage(r.messageId)
}

async function selectPrivateChat(friend: { userId: number; nickname: string }) {
  currentChat.value = { type: 'private', id: friend.userId, name: friend.nickname }
  chatStore.setCurrentSession('private', friend.userId, friend.nickname)
  chatStore.markSessionRead('private', friend.userId)
  // 通知对方：该会话已读
  sendReadReceipt(friend.userId)
  mobileChatOpen.value = true
}

/** 发送已读回执（to = 对方） */
function sendReadReceipt(peerId: number) {
  if (!auth.user || !ws.connected) return
  ws.sendMessage({
    type: 'read_receipt',
    from: String(auth.user.id),
    to: String(peerId),
    content: 'all',
    timestamp: Date.now(),
    messageId: '',
    messageType: 0,
    senderName: '',
    senderAvatar: null
  })
}

async function selectGroupChat(group: { id: number; name: string }) {
  currentChat.value = { type: 'group', id: group.id, name: group.name }
  chatStore.setCurrentSession('group', group.id, group.name)
  chatStore.markSessionRead('group', group.id)
  // 预加载群成员（@ 选择器与成员面板共用）
  refreshQuietly(groupStore.fetchMembers(group.id), '加载群成员')
  mobileChatOpen.value = true
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

function handleWsMessage(msg: WsMessage) {
  // 会话被踢下线（其他设备踢出本设备/修改密码/重置密码）：清理登录态并回登录页
  if (msg.type === 'kicked') {
    toast('该设备已被踢下线，请重新登录')
    ws.disconnect()
    auth.logout()
    router.push('/login')
    return
  }
  // 新好友申请：刷新申请列表
  if (msg.type === 'friend_request') {
    refreshQuietly(friendStore.fetchPendingRequests(), '刷新好友申请')
    return
  }
  // 好友申请被接受/拒绝：双方刷新好友与申请列表
  if (msg.type === 'friend_accepted' || msg.type === 'friend_rejected') {
    refreshQuietly(friendStore.fetchFriends(), '刷新好友列表')
    refreshQuietly(friendStore.fetchPendingRequests(), '刷新好友申请')
    refreshQuietly(chatStore.fetchSessions(), '刷新会话列表')
    return
  }
  // 好友关系被删除（双向通知）：刷新好友列表；若正在与该用户私聊则关闭会话
  if (msg.type === 'friend_removed') {
    refreshQuietly(friendStore.fetchFriends(), '刷新好友列表')
    refreshQuietly(chatStore.fetchSessions(), '刷新会话列表')
    const peerId = Number(msg.from)
    if (currentChat.value?.type === 'private' && currentChat.value.id === peerId) {
      toast('好友关系已解除')
      currentChat.value = null
      chatStore.setCurrentSession('private', peerId, '')
      mobileChatOpen.value = false
    }
    return
  }
  // 被邀请加入群组：刷新群列表与会话
  if (msg.type === 'group_invited') {
    refreshQuietly(groupStore.fetchGroups(), '刷新群列表')
    refreshQuietly(chatStore.fetchSessions(), '刷新会话列表')
    return
  }
  // 在线状态已由 onStatusChange 处理
  if (msg.type === 'online_status') return

  // 已读回执：对方读了我发出的私聊消息
  if (msg.type === 'read_receipt') {
    if (msg.from) {
      chatStore.markSessionReadByPeer(chatStore.sessionKey('private', Number(msg.from)))
    }
    return
  }

  // 消息撤回：标记本地对应消息
  if (msg.type === 'message_recalled') {
    const me = auth.user?.id
    const targetId = msg.content || msg.messageId
    if (me && targetId) {
      // 群聊优先（to 为群 ID），否则按私聊对方
      const gKey = chatStore.sessionKey('group', Number(msg.to))
      if (chatStore.messages.get(gKey)) {
        chatStore.markMessageRecalled(gKey, targetId)
      } else {
        const peer = Number(msg.from) === me ? Number(msg.to) : Number(msg.from)
        chatStore.markMessageRecalled(chatStore.sessionKey('private', peer), targetId)
      }
    }
    return
  }

  // 被拉黑：发送被拒或对方拉黑通知
  if (msg.type === 'blocked') {
    const me = auth.user?.id
    const peer = Number(msg.from)
    // 对方把我拉黑了（好友关系已被解除）：刷新好友/会话列表
    refreshQuietly(friendStore.fetchFriends(), '刷新好友列表')
    refreshQuietly(chatStore.fetchSessions(), '刷新会话列表')
    if (me && peer && currentChat.value?.type === 'private' && currentChat.value.id === peer) {
      // 正在与该用户聊天：移除被拒的乐观消息并提示
      if (msg.messageId) {
        chatStore.removeMessage(chatStore.sessionKey('private', peer), msg.messageId)
      }
      chatAreaRef.value?.setHint(msg.content || '对方已将你拉黑，无法发送消息')
    }
    return
  }

  // 群消息被拒绝（禁言中）：移除乐观消息并提示
  if (msg.type === 'muted') {
    if (msg.messageId) {
      chatStore.removeMessage(chatStore.sessionKey('group', Number(msg.to)), msg.messageId)
    }
    chatAreaRef.value?.setHint(msg.content || '你已被禁言，无法发送消息')
    return
  }

  // 所在群被解散（管理后台操作）：刷新群/会话列表，若正在该群则退出
  if (msg.type === 'group_dissolved') {
    toast(msg.content || '群已被解散')
    refreshQuietly(groupStore.fetchGroups(), '刷新群列表')
    refreshQuietly(chatStore.fetchSessions(), '刷新会话列表')
    const gid = Number(msg.to)
    if (currentChat.value?.type === 'group' && currentChat.value.id === gid) {
      currentChat.value = null
      chatStore.setCurrentSession('group', gid, '')
      mobileChatOpen.value = false
    }
    return
  }

  // 被移出群（群主/管理员踢人）：与解散同样处理 —— 客户端主动退出该会话
  if (msg.type === 'group_removed') {
    toast(msg.content || '你已被移出该群')
    refreshQuietly(groupStore.fetchGroups(), '刷新群列表')
    refreshQuietly(chatStore.fetchSessions(), '刷新会话列表')
    const gid = Number(msg.to)
    if (currentChat.value?.type === 'group' && currentChat.value.id === gid) {
      currentChat.value = null
      chatStore.setCurrentSession('group', gid, '')
      mobileChatOpen.value = false
    }
    return
  }

  const { key, isNew } = chatStore.addMessage(msg, auth.user?.id)
  // 是否是自己发出的消息（多端同步：其他设备收到的"自己的消息"不应计未读/提示音）
  const isMine = Number(msg.from) === auth.user?.id
  const currentKey = currentChat.value ? chatStore.sessionKey(currentChat.value.type, currentChat.value.id) : ''
  if (key === currentKey) {
    // 当前会话：清未读、标记已读、滚到底部；对方发来的消息即时回已读回执
    const chat = currentChat.value
    if (chat) {
      chatStore.markSessionRead(chat.type, chat.id)
      if (msg.type === 'private_message' && chat.type === 'private' && !isMine) {
        sendReadReceipt(chat.id)
      }
    }
    // 仅在接近底部时自动滚动（用户正在上翻历史时不做打扰）
    chatAreaRef.value?.scrollToBottomIfNear()
  } else if (isNew && !isMine) {
    // 免打扰会话不增加未读提醒、不播放提示音
    const sep = key.indexOf('_')
    const sType = key.slice(0, sep) as ChatType
    const sId = Number(key.slice(sep + 1))
    if (!chatStore.isSessionMuted(sType, sId)) {
      chatStore.bumpUnread(key)
      playNotifySound()
      // 浏览器桌面通知（页面隐藏时）：点击通知可跳转到该会话
      const isGroup = sType === 'group'
      const title = isGroup
        ? `群消息 · ${msg.senderName || ''}`
        : msg.senderName || '新消息'
      showDesktopNotify(title, msg.content, { type: sType, id: sId })
    }
  }
}

function openAddModal() {
  showAddModal.value = true
}

function openRequestsModal() {
  showRequestsModal.value = true
}

/** 打开会话设置弹窗（置顶/免打扰） */
function openSessionSetting(s: SessionInfo) {
  sessionSettingTarget.value = s
  showSessionSetting.value = true
}

// ==================== 群成员/公告弹窗 ====================
const showAnnouncementModal = ref(false)

function openMembersModal() {
  if (!currentChat.value) return
  refreshQuietly(groupStore.fetchMembers(currentChat.value.id), '加载群成员')
  showMembersModal.value = true
}

/** 公告保存后（组件内已刷新 store，此处兜底同步横幅） */
function announcementSaved() {
  refreshQuietly(groupStore.fetchGroups(), '刷新群列表')
}

// ==================== 好友设置（备注/分类） ====================
function openFriendSetting(f: FriendInfo) {
  friendSetting.value = f
  showFriendSetting.value = true
}

/** 备注/分类保存成功：若正在与该好友聊天，更新会话显示名 */
function onFriendTagSaved() {
  if (currentChat.value?.type === 'private' && friendSetting.value &&
      currentChat.value.id === friendSetting.value.userId) {
    currentChat.value.name = friendDisplayName(friendSetting.value)
  }
}

// ==================== 黑名单 ====================
const showBlacklistModal = ref(false)

// ==================== 登录设备管理（多端登录） ====================
const showSessionsModal = ref(false)

// ==================== 机器人 ====================
const showRobotModal = ref(false)

function handleLogout() {
  ws.disconnect()
  auth.logout()
  router.push('/login')
}

watch(activeTab, () => {
  if (activeTab.value === 'friends') refreshQuietly(friendStore.fetchFriends(), '刷新好友列表')
  else if (activeTab.value === 'groups') refreshQuietly(groupStore.fetchGroups(), '刷新群列表')
  else refreshQuietly(chatStore.fetchSessions(), '刷新会话列表')
})
</script>

<style scoped>
.chat-layout {
  display: flex;
  height: 100vh;
  height: 100dvh;
}

/* 初始化数据加载失败：顶部悬浮的非致命错误条 */
.startup-error {
  position: fixed;
  top: 12px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 300;
  display: flex;
  align-items: center;
  gap: 10px;
  max-width: calc(100vw - 32px);
  padding: 8px 12px;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-left: 3px solid var(--danger);
  border-radius: 10px;
  box-shadow: var(--shadow);
  font-size: 13px;
  color: var(--text);
}

.startup-error-text {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.startup-error-btn {
  border: none;
  border-radius: 8px;
  background: var(--active-bg);
  color: var(--primary);
  font-size: 12.5px;
  padding: 4px 10px;
  cursor: pointer;
  flex-shrink: 0;
}

.startup-error-btn:hover {
  background: var(--bg-hover);
}

.startup-error-close {
  border: none;
  background: transparent;
  color: var(--text-secondary);
  font-size: 13px;
  padding: 0 2px;
  cursor: pointer;
  flex-shrink: 0;
}
</style>
