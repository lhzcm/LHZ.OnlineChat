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
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import { authApi } from '@/api/auth'
import { groupApi } from '@/api/group'
import { useToast } from '@/composables/useToast'
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
import type { FriendInfo, GroupMemberInfo, WsMessage, ChatType, SessionInfo, MessageSearchResult } from '@/types'

const { toastMsg, toast } = useToast()

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
const sendHint = ref('')
// 移动端：聊天窗口全屏开关
const mobileChatOpen = ref(false)
// 表情面板
const showEmojiPanel = ref(false)
const emojiPanelRef = ref<HTMLElement | null>(null)
const emojiBtnRef = ref<HTMLElement | null>(null)
// 图片消息
const imageInputRef = ref<HTMLInputElement | null>(null)
const sendingImage = ref(false)
const lightboxUrl = ref('')
// 消息提示音
const notifySoundEnabled = ref(localStorage.getItem('notifySound') !== '0')
let notifyAudioCtx: AudioContext | null = null

function onNotifySoundChange(enabled: boolean) {
  notifySoundEnabled.value = enabled
  localStorage.setItem('notifySound', enabled ? '1' : '0')
}

/** 播放新消息提示音（Web Audio 合成，无需音频文件） */
function playNotifySound() {
  if (!notifySoundEnabled.value) return
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

  // 进入主页：有会话时默认显示会话列表，否则显示好友 Tab 引导添加
  if (chatStore.sessions.length > 0) {
    activeTab.value = 'sessions'
  }

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
  groupStore.fetchMembers(group.id)
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
  // 被邀请加入群组：刷新群列表与会话
  if (msg.type === 'group_invited') {
    groupStore.fetchGroups()
    chatStore.fetchSessions()
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
  if (msg.type === 'blocked') {    const me = auth.user?.id
    const peer = Number(msg.from)
    // 对方把我拉黑了（好友关系已被解除）：刷新好友/会话列表
    friendStore.fetchFriends()
    chatStore.fetchSessions()
    if (me && peer && currentChat.value?.type === 'private' && currentChat.value.id === peer) {
      // 正在与该用户聊天：移除被拒的乐观消息并提示
      if (msg.messageId) {
        chatStore.removeMessage(chatStore.sessionKey('private', peer), msg.messageId)
      }
      chatAreaRef.value?.setHint(msg.content || '对方已将你拉黑，无法发送消息')
    }
    return
  }

  const { key, isNew } = chatStore.addMessage(msg, auth.user?.id)
  const currentKey = currentChat.value ? chatStore.sessionKey(currentChat.value.type, currentChat.value.id) : ''
  if (key === currentKey) {
    // 当前会话：清未读、标记已读、滚到底部；对方发来的消息即时回已读回执
    const chat = currentChat.value
    if (chat) {
      chatStore.markSessionRead(chat.type, chat.id)
      if (msg.type === 'private_message' && chat.type === 'private') {
        sendReadReceipt(chat.id)
      }
    }
    // 仅在接近底部时自动滚动（用户正在上翻历史时不做打扰）
    chatAreaRef.value?.scrollToBottomIfNear()
  } else if (isNew) {
    // 免打扰会话不增加未读提醒、不播放提示音
    const sep = key.indexOf('_')
    const sType = key.slice(0, sep) as ChatType
    const sId = Number(key.slice(sep + 1))
    if (!chatStore.isSessionMuted(sType, sId)) {
      chatStore.bumpUnread(key)
      playNotifySound()
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
  groupStore.fetchMembers(currentChat.value.id)
  showMembersModal.value = true
}

/** 公告保存后（组件内已刷新 store，此处兜底同步横幅） */
function announcementSaved() {
  groupStore.fetchGroups()
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
</style>
