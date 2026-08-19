<template>
  <div class="chat-layout">
    <ChatSidebar v-model:active-tab="activeTab" :current-chat="currentChat" :mobile-chat-open="mobileChatOpen"
      @select-private="selectPrivateChat" @select-group="selectGroupChat" @select-session="selectSession"
      @session-setting="openSessionSetting" @friend-setting="openFriendSetting" @add="openAddModal"
      @requests="openRequestsModal" @robot="showRobotModal = true" @profile="showProfileModal = true"
      @logout="handleLogout" @open-result="openSearchResult" />

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
          <Avatar :name="currentChat.name" :url="chatAvatar" size="sm" />
          <div class="chat-title">
            <div class="chat-title-row">
              <span class="chat-type-tag" v-if="currentChat.type === 'group'">群</span>
              <span class="chat-name">{{ currentChat.name }}</span>
            </div>
            <span class="chat-sub">{{ chatSub }}</span>
          </div>
          <div class="header-spacer"></div>
          <button v-if="currentChat.type === 'group'" class="icon-btn" @click="openMembersModal" title="群成员">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
              <circle cx="9" cy="7" r="4" />
              <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
              <path d="M16 3.13a4 4 0 0 1 0 7.75" />
            </svg>
          </button>
        </div>

        <!-- 群公告横幅 -->
        <div class="announce-bar" v-if="currentChat.type === 'group' && currentAnnouncement" @click="showAnnouncementModal = true" title="查看群公告">
          <span class="announce-icon">📢</span>
          <span class="announce-text">{{ currentAnnouncement }}</span>
        </div>

        <div class="chat-messages" ref="msgContainer" @scroll="onMessagesScroll">
          <div class="history-load-hint" v-if="historyHint">{{ historyHint }}</div>
          <MessageBubble v-for="msg in currentMessages" :key="msg.messageId" :msg="msg"
            :chat-type="currentChat.type" :chat-id="currentChat.id" :chat-name="currentChat.name"
            @reply="startReply" @image-click="openLightbox" />
        </div>

        <!-- 引用回复横幅 -->
        <div class="reply-bar" v-if="replyTarget">
          <span class="reply-bar-text">回复 {{ replyTarget.senderName }}：{{ replyTarget.content }}</span>
          <button class="reply-cancel" @click="replyTarget = null">✕</button>
        </div>

        <div class="chat-input-bar">
          <!-- @ 成员选择浮层 -->
          <div class="mention-panel" v-if="mentionOpen && mentionFiltered.length" @click.stop>
            <button v-for="m in mentionFiltered" :key="m.userId" class="mention-item" @click="pickMention(m)">
              <Avatar :name="m.nickname" :url="m.avatar" size="sm" />
              <span class="contact-name">{{ m.nickname }}</span>
              <span class="mention-role" v-if="m.role === 0">群主</span>
            </button>
          </div>
          <!-- 表情面板 -->
          <div class="emoji-panel" ref="emojiPanelRef" v-if="showEmojiPanel" @click.stop>
            <div class="emoji-group" v-for="g in emojiGroups" :key="g.name">
              <div class="emoji-group-title">{{ g.name }}</div>
              <div class="emoji-grid">
                <button v-for="e in g.list" :key="e" class="emoji-item" @click="insertEmoji(e)">{{ e }}</button>
              </div>
            </div>
          </div>
          <button v-if="currentChat.type === 'group'" class="emoji-btn mention-btn" @click.stop="openMentionPicker" title="提及成员">
            <span class="at-symbol">@</span>
          </button>
          <button class="emoji-btn" :disabled="sendingImage" @click="imageInputRef?.click()" title="发送图片">
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
              <circle cx="8.5" cy="8.5" r="1.5" />
              <polyline points="21 15 16 10 5 21" />
            </svg>
          </button>
          <input ref="imageInputRef" type="file" accept="image/*" class="hidden-file" @change="onImageSelect" />
          <button class="emoji-btn" ref="emojiBtnRef" @click.stop="toggleEmojiPanel" title="表情">
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="12" cy="12" r="10" />
              <path d="M8 14s1.5 2 4 2 4-2 4-2" />
              <line x1="9" y1="9" x2="9.01" y2="9" />
              <line x1="15" y1="9" x2="15.01" y2="9" />
            </svg>
          </button>
          <input ref="inputEl" v-model="inputText" class="input" :placeholder="inputPlaceholder" @keydown="onInputKeydown" @input="onInputChange" />
          <button class="send-btn" @click="send" :disabled="!inputText.trim()">发送</button>
        </div>
        <p class="send-hint" v-if="sendHint">{{ sendHint }}</p>
      </template>
    </main>

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
      @update:notify-sound-enabled="onNotifySoundChange" />

    <!-- 黑名单管理弹窗 -->
    <BlacklistModal v-if="showBlacklistModal" @close="showBlacklistModal = false" />

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
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import { emojiGroups } from '@/constants/emojis'
import Avatar from '@/components/Avatar.vue'
import { avatarGradient, avatarInitial } from '@/utils/avatar'
import { authApi } from '@/api/auth'
import { messageApi } from '@/api/message'
import { groupApi } from '@/api/group'
import { useToast } from '@/composables/useToast'
import { formatMsgTime } from '@/utils/format'
import BlacklistModal from '@/components/chat/modals/BlacklistModal.vue'
import RobotManagerModal from '@/components/chat/modals/RobotManagerModal.vue'
import ProfileModal from '@/components/chat/modals/ProfileModal.vue'
import RequestsModal from '@/components/chat/modals/RequestsModal.vue'
import AddModal from '@/components/chat/modals/AddModal.vue'
import SessionSettingModal from '@/components/chat/modals/SessionSettingModal.vue'
import MembersModal from '@/components/chat/modals/MembersModal.vue'
import AnnouncementModal from '@/components/chat/modals/AnnouncementModal.vue'
import FriendSettingModal from '@/components/chat/modals/FriendSettingModal.vue'
import MessageBubble from '@/components/chat/MessageBubble.vue'
import ChatSidebar from '@/components/chat/ChatSidebar.vue'
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
// @ 提及
const mentionOpen = ref(false)
const mentionQuery = ref('')
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

const currentMessages = computed(() => {
  if (!currentChat.value) return []
  const key = `${currentChat.value.type}_${currentChat.value.id}`
  return chatStore.messages.get(key) || []
})

// 聊天窗口副标题：私聊显示原昵称+在线状态（有备注时），群聊显示人数
const chatSub = computed(() => {
  if (!currentChat.value) return ''
  if (currentChat.value.type === 'private') {
    const f = friendStore.friends.find(x => x.userId === currentChat.value!.id)
    if (!f) return ''
    const status = f.isOnline ? '在线' : '离线'
    return f.remark ? `${f.nickname} · ${status}` : status
  }
  const g = groupStore.groups.find(x => x.id === currentChat.value!.id)
  return g ? `${g.memberCount} 人` : ''
})

// @ 成员选择：按输入过滤
const mentionFiltered = computed(() => {
  const q = mentionQuery.value.trim().toLowerCase()
  const list = groupStore.members.filter(m => !q || m.nickname.toLowerCase().includes(q))
  return list.slice(0, 8)
})

// 输入框提示：仅群聊提示 @ 提及
const inputPlaceholder = computed(() =>
  currentChat.value?.type === 'group' ? '输入消息… 输入 @ 可提及成员' : '输入消息…'
)

// 当前会话头像（私聊=好友头像，群聊=群头像）
const chatAvatar = computed(() => {
  if (!currentChat.value) return null
  if (currentChat.value.type === 'private') {
    return friendStore.friends.find(x => x.userId === currentChat.value!.id)?.avatar ?? null
  }
  return groupStore.groups.find(x => x.id === currentChat.value!.id)?.avatar ?? null
})

// 好友显示名：备注优先，其次昵称
function friendDisplayName(f: FriendInfo): string {
  return f.remark || f.nickname
}

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

  // 点击面板/按钮外部时关闭表情面板
  document.addEventListener('click', onDocClick)
})

onUnmounted(() => {
  document.removeEventListener('click', onDocClick)
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

// ==================== 图片消息 ====================
/** 选择图片后上传并作为图片消息发送 */
async function onImageSelect(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!currentChat.value || !auth.user) return
  if (!ws.connected) {
    sendHint.value = '连接已断开，正在重连，请稍候…'
    return
  }
  if (!file.type.startsWith('image/')) {
    toast('请选择图片文件')
    return
  }
  // 前端预检大小，避免无谓上传（后端同样限制 5MB）
  if (file.size > 5 * 1024 * 1024) {
    toast('发送图片太大，发送失败（最大 5MB）')
    return
  }

  sendingImage.value = true
  sendHint.value = ''
  try {
    const res = await messageApi.uploadImage(file)
    if (!res.success || !res.data?.url) {
      const msg = res.message || ''
      // 具体化失败原因：大小超限 / 其他
      toast(msg.includes('5MB') || msg.includes('大小')
        ? '发送图片太大，发送失败（最大 5MB）'
        : `图片发送失败：${msg || '请重试'}`)
      return
    }
    const msg: WsMessage = {
      type: currentChat.value.type === 'private' ? 'private_message' : 'group_message',
      from: String(auth.user.id),
      to: String(currentChat.value.id),
      content: res.data.url,
      timestamp: Date.now(),
      messageId: genId(),
      messageType: 1, // 图片
      senderName: auth.user.nickname,
      senderAvatar: auth.user.avatar
    }
    chatStore.addMessage(msg, auth.user.id) // 乐观插入
    ws.sendMessage(msg)
    scrollToBottom()
  } catch (err: any) {
    toast('图片发送失败，请重试')
  } finally {
    sendingImage.value = false
  }
}

function openLightbox(url: string) {
  lightboxUrl.value = url
}

// ==================== 引用回复 ====================
/** 引用回复目标 */
const replyTarget = ref<{ messageId: string; content: string; senderName: string } | null>(null)

function startReply(msg: WsMessage) {
  replyTarget.value = {
    messageId: msg.messageId,
    content: (msg.content || '').slice(0, 50),
    senderName: msg.senderName || '对方'
  }
  inputEl.value?.focus()
}

// ==================== 会话设置（置顶/免打扰） ====================
function openSessionSetting(s: SessionInfo) {
  sessionSettingTarget.value = s
  showSessionSetting.value = true
}

// ==================== @ 提及 ====================
/** 输入框按键：@ 键直接打开成员面板（双保险，避免 input 事件异常），Enter 发送 */
function onInputKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter') {
    onInputEnter(e)
    return
  }
  if ((e.key === '@' || e.key === '＠') && currentChat.value?.type === 'group') {
    mentionOpen.value = true
    mentionQuery.value = ''
    if (groupStore.members.length === 0) {
      groupStore.fetchMembers(currentChat.value.id)
    }
  }
}

/** 输入变化时检测 @ 触发成员选择（仅群聊），兼容全角 ＠ */
function onInputChange() {
  const el = inputEl.value
  if (!el || currentChat.value?.type !== 'group') {
    mentionOpen.value = false
    return
  }
  const text = el.value
  const caret = el.selectionStart ?? text.length
  // 向前找最近的非邮箱 @（前面是空白或开头；兼容全角 ＠）
  const atIdx = Math.max(
    text.lastIndexOf('@', caret - 1),
    text.lastIndexOf('＠', caret - 1)
  )
  if (atIdx >= 0 && (atIdx === 0 || /\s/.test(text[atIdx - 1]))) {
    const token = text.slice(atIdx + 1, caret)
    if (!token.includes(' ')) {
      mentionOpen.value = true
      mentionQuery.value = token
      // 成员列表尚未加载时兜底拉取（进入群聊时通常已加载；拉取幂等）
      if (groupStore.members.length === 0) {
        groupStore.fetchMembers(currentChat.value.id)
      }
      return
    }
  }
  mentionOpen.value = false
}

/** 选择成员：替换 @token 为 @昵称，保留光标 */
function pickMention(m: GroupMemberInfo) {
  const el = inputEl.value
  if (!el) return
  const text = el.value
  const caret = el.selectionStart ?? text.length
  const atIdx = Math.max(text.lastIndexOf('@', caret - 1), text.lastIndexOf('＠', caret - 1))
  const insert = `@${m.nickname} `
  const start = atIdx >= 0 ? atIdx : caret
  inputText.value = text.slice(0, start) + insert + text.slice(caret)
  mentionOpen.value = false
  nextTick(() => {
    el.focus()
    const pos = start + insert.length
    el.setSelectionRange(pos, pos)
  })
}

/** @ 快捷按钮：光标处插入 @ 并打开成员面板（不依赖键盘输入） */
function openMentionPicker() {
  if (!currentChat.value || currentChat.value.type !== 'group') return
  if (groupStore.members.length === 0) {
    groupStore.fetchMembers(currentChat.value.id)
  }
  const el = inputEl.value
  if (el) {
    const caret = el.selectionStart ?? inputText.value.length
    const end = el.selectionEnd ?? caret
    inputText.value = inputText.value.slice(0, caret) + '@' + inputText.value.slice(end)
    nextTick(() => {
      el.focus()
      const pos = caret + 1
      el.setSelectionRange(pos, pos)
    })
  }
  mentionOpen.value = true
  mentionQuery.value = ''
}

/** 解析文本中的 @昵称 为成员账号 ID 列表 */
function parseMentions(text: string): number[] {
  if (currentChat.value?.type !== 'group') return []
  const ids: number[] = []
  for (const m of groupStore.members) {
    if (text.includes(`@${m.nickname}`) && !ids.includes(m.userId)) {
      ids.push(m.userId)
    }
  }
  return ids
}

// 诊断辅助（排障用）：浏览器控制台执行 window.__mentionDebug() 查看 @ 功能状态
;(window as unknown as Record<string, unknown>).__mentionDebug = () => ({
  path: location.pathname,
  chatType: currentChat.value?.type,
  chatId: currentChat.value?.id,
  memberCount: groupStore.members.length,
  members: groupStore.members.map(m => m.nickname),
  mentionOpen: mentionOpen.value,
  mentionQuery: mentionQuery.value,
  inputValue: inputEl.value?.value ?? null,
  hasInputEvents: true
})

function scrollToBottom() {
  nextTick(() => {
    if (msgContainer.value) {
      msgContainer.value.scrollTop = msgContainer.value.scrollHeight
    }
  })
}

// ==================== 历史消息上滑加载 ====================
/** 当前会话的历史分页元数据（store 中按会话 key 维护） */
const historyMeta = computed(() => {
  if (!currentChat.value) return null
  return chatStore.historyMeta.get(chatStore.sessionKey(currentChat.value.type, currentChat.value.id))
})
const historyHasMore = computed(() => !!historyMeta.value?.hasMore)
const historyLoading = computed(() => !!historyMeta.value?.loading)
/** 消息区顶部的加载提示文案 */
const historyHint = computed(() => {
  if (historyLoading.value) return '正在加载更早的消息…'
  if (historyHasMore.value) return '上滑加载更早的消息'
  if (currentMessages.value.length > 0) return '没有更多消息了'
  return ''
})

/** 是否已滚到接近底部（自动滚动阈值） */
function isNearBottom(): boolean {
  const el = msgContainer.value
  if (!el) return true
  return el.scrollHeight - el.scrollTop - el.clientHeight < 120
}

/** 滚到顶部时加载更早的历史，并保持当前视口位置 */
async function onMessagesScroll() {
  const el = msgContainer.value
  if (!el || !currentChat.value) return
  if (el.scrollTop > 24) return
  if (historyLoading.value || !historyHasMore.value) return
  const prevHeight = el.scrollHeight
  await chatStore.loadMoreHistory(currentChat.value.type, currentChat.value.id, auth.user?.id)
  nextTick(() => {
    if (msgContainer.value) {
      msgContainer.value.scrollTop = msgContainer.value.scrollHeight - prevHeight
    }
  })
}

/** 点击搜索结果：打开会话并定位到该消息 */
async function openSearchResult(r: MessageSearchResult) {
  if (r.type === 'private') {
    await selectPrivateChat({ userId: r.sessionId, nickname: r.sessionName })
  } else {
    await selectGroupChat({ id: r.sessionId, name: r.sessionName })
  }
  if (r.messageId) await locateMessage(r.messageId)
}

/** 定位消息：已加载则滚动到该行并高亮；否则继续翻更早的历史（最多 10 页） */
async function locateMessage(messageId: string) {
  const chat = currentChat.value
  if (!chat) return
  const key = chatStore.sessionKey(chat.type, chat.id)
  // 先让 selectPrivateChat/selectGroupChat 里 scrollToBottom 的 nextTick 落定，避免其覆盖定位滚动
  await nextTick()
  const scrollToRow = (): boolean => {
    const el = msgContainer.value?.querySelector<HTMLElement>(`[data-mid="${CSS.escape(messageId)}"]`)
    if (!el) return false
    el.scrollIntoView({ block: 'center' })
    el.classList.add('msg-highlight')
    setTimeout(() => el.classList.remove('msg-highlight'), 2200)
    return true
  }
  if (scrollToRow()) return
  for (let i = 0; i < 10; i++) {
    const meta = chatStore.historyMeta.get(key)
    if (!meta?.hasMore) break
    await chatStore.loadMoreHistory(chat.type, chat.id, auth.user?.id)
    await nextTick()
    if (scrollToRow()) return
  }
}

async function selectPrivateChat(friend: { userId: number; nickname: string }) {
  currentChat.value = { type: 'private', id: friend.userId, name: friend.nickname }
  chatStore.setCurrentSession('private', friend.userId, friend.nickname)
  await chatStore.loadHistory('private', friend.userId, 1, auth.user?.id)
  chatStore.markSessionRead('private', friend.userId)
  // 通知对方：该会话已读
  sendReadReceipt(friend.userId)
  mobileChatOpen.value = true
  scrollToBottom()
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
  await chatStore.loadHistory('group', group.id)
  chatStore.markSessionRead('group', group.id)
  // 预加载群成员（@ 选择器与成员面板共用）
  groupStore.fetchMembers(group.id)
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
  if (msg.type === 'blocked') {
    const me = auth.user?.id
    const peer = Number(msg.from)
    // 对方把我拉黑了（好友关系已被解除）：刷新好友/会话列表
    friendStore.fetchFriends()
    chatStore.fetchSessions()
    if (me && peer && currentChat.value?.type === 'private' && currentChat.value.id === peer) {
      // 正在与该用户聊天：移除被拒的乐观消息并提示
      if (msg.messageId) {
        chatStore.removeMessage(chatStore.sessionKey('private', peer), msg.messageId)
      }
      sendHint.value = msg.content || '对方已将你拉黑，无法发送消息'
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
    if (isNearBottom()) scrollToBottom()
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
    senderAvatar: auth.user.avatar,
    mentions: parseMentions(text), // 群聊 @ 提及
    replyTo: replyTarget.value?.messageId,
    replyContent: replyTarget.value?.content,
    replySender: replyTarget.value?.senderName
  }
  chatStore.addMessage(msg, auth.user.id) // 乐观插入，回显到达后自动去重
  ws.sendMessage(msg)
  inputText.value = ''
  replyTarget.value = null // 发送后清除引用
  scrollToBottom()
}

function openAddModal() {
  showAddModal.value = true
}

function openRequestsModal() {
  showRequestsModal.value = true
}

// ==================== 群成员/公告弹窗 ====================
/** 当前群的信息（横幅公告展示用） */
const currentGroupInfo = computed(() => {
  if (!currentChat.value || currentChat.value.type !== 'group') return null
  return groupStore.groups.find(g => g.id === currentChat.value!.id) ?? null
})
const currentAnnouncement = computed(() => currentGroupInfo.value?.announcement || '')

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

/* 轻提示 Toast */
.app-toast {
  position: fixed;
  top: 24px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 9999;
  max-width: 80vw;
  padding: 10px 18px;
  border-radius: 10px;
  background: rgba(31, 35, 41, 0.92);
  color: #fff;
  font-size: 13px;
  line-height: 1.5;
  box-shadow: 0 8px 30px rgba(31, 35, 41, 0.25);
  pointer-events: none;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
html[data-theme='dark'] .app-toast {
  background: rgba(46, 52, 64, 0.95);
}
.toast-fade-enter-active,
.toast-fade-leave-active {
  transition: opacity 0.25s, transform 0.25s;
}
.toast-fade-enter-from,
.toast-fade-leave-to {
  opacity: 0;
  transform: translateX(-50%) translateY(-8px);
}

/* 群公告横幅 */
.announce-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 8px 12px 0;
  padding: 8px 12px;
  background: var(--active-bg);
  border: 1px solid var(--border);
  border-radius: 10px;
  cursor: pointer;
  font-size: 12px;
  color: var(--text-secondary);
  flex-shrink: 0;
  transition: background 0.15s;
}
.announce-bar:hover {
  background: var(--bg-hover);
}
.announce-icon {
  flex-shrink: 0;
}
.announce-text {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
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

/* 聊天标题行 */
.chat-title-row {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.chat-type-tag {
  flex-shrink: 0;
  font-size: 11px;
  font-weight: 600;
  padding: 1px 7px;
  border-radius: 8px;
  background: var(--active-bg);
  color: var(--primary);
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

.header-spacer {
  flex: 1;
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
  overflow-x: hidden;
  padding: 18px 20px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  background: var(--chat-bg);
}

/* 顶部历史加载提示 */
.history-load-hint {
  text-align: center;
  font-size: 12px;
  color: var(--text-secondary);
  padding: 2px 0;
  user-select: none;
  flex-shrink: 0;
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
  position: relative;
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

/* 已读状态 */
.msg-meta-line {
  display: flex;
  align-items: center;
  gap: 2px;
}

.msg-status {
  font-size: 11px;
  color: var(--text-secondary);
  padding: 0 4px;
}

.msg-status.read {
  color: var(--primary);
  font-weight: 500;
}

/* 撤回 */
.msg-bubble.recalled {
  background: var(--bg-hover);
  color: var(--text-secondary);
  box-shadow: none;
  font-size: 13px;
  font-style: italic;
}

.msg-recalled-text {
  padding: 0 6px;
}

.msg-recall-btn {
  border: none;
  background: var(--bg-white);
  color: var(--text-secondary);
  font-size: 11.5px;
  padding: 4px 10px;
  border-radius: 8px;
  cursor: pointer;
  box-shadow: var(--shadow-sm);
  white-space: nowrap;
  transition: background 0.15s, color 0.15s;
}

.msg-recall-btn:hover {
  background: var(--border);
  color: var(--text);
}

/* 引用回复 */
.reply-preview {
  display: flex;
  flex-direction: column;
  gap: 1px;
  max-width: 100%;
  padding: 4px 10px;
  border-left: 3px solid var(--primary-light);
  background: var(--bg-hover);
  border-radius: 8px 8px 0 0;
  margin-bottom: 2px;
}

.reply-sender {
  font-size: 11.5px;
  color: var(--primary);
  font-weight: 500;
}

.reply-text {
  font-size: 12px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 260px;
}

/* 操作按钮（回复/撤回）：绝对定位在气泡旁的空白区，不占文档流 → 悬停不再引起布局跳动 */
.msg-actions {
  position: absolute;
  top: 50%;
  transform: translateY(-50%);
  display: inline-flex;
  gap: 6px;
  z-index: 2;
  opacity: 0;
  visibility: hidden;
  pointer-events: none;
  transition: opacity 0.15s ease, visibility 0.15s ease;
}

/* 锚定在气泡（.msg-body 按内容收缩），按钮紧贴气泡：
   别人的消息 → 气泡右侧；自己的消息 → 气泡左侧 */
.msg-row:not(.mine) .msg-actions {
  left: calc(100% + 8px);
}

.msg-row.mine .msg-actions {
  right: calc(100% + 8px);
}

.msg-row:hover .msg-actions,
/* 鼠标移到按钮上时保持显示（按钮在气泡外，:has 兜底） */
.msg-row:has(.msg-actions:hover) .msg-actions {
  opacity: 1;
  visibility: visible;
  pointer-events: auto;
}

.reply-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px;
  background: var(--bg-hover);
  border-top: 1px solid var(--border);
  font-size: 12.5px;
  color: var(--text-secondary);
}

.reply-bar-text {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.reply-cancel {
  border: none;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  font-size: 13px;
  flex-shrink: 0;
}

.reply-cancel:hover {
  color: var(--danger);
}

/* @ 提及高亮 */
.mention {
  color: var(--primary);
  font-weight: 600;
}

/* 图片消息 */
.msg-bubble.is-image {
  padding: 4px;
  background: transparent;
  border: none;
  box-shadow: none;
}

.msg-image {
  display: block;
  max-width: 260px;
  max-height: 260px;
  border-radius: 12px;
  cursor: zoom-in;
  box-shadow: var(--shadow-sm);
  object-fit: cover;
}

@media (max-width: 720px) {
  .msg-image {
    max-width: 220px;
    max-height: 220px;
  }
}

/* 图片放大预览 */
.lightbox {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.85);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 200;
  cursor: zoom-out;
  animation: fade-in 0.2s;
}

.lightbox img {
  max-width: 92vw;
  max-height: 88vh;
  border-radius: 8px;
  object-fit: contain;
}

.lightbox-close {
  position: absolute;
  bottom: 24px;
  left: 50%;
  transform: translateX(-50%);
  color: rgba(255, 255, 255, 0.7);
  font-size: 13px;
}

/* 被 @ 的消息：主色描边标记 */
.msg-bubble.mentioned {
  outline: 2px solid rgba(91, 108, 255, 0.5);
  outline-offset: -2px;
}

/* @ 成员选择浮层 */
.mention-panel {
  position: absolute;
  bottom: calc(100% + 10px);
  left: 12px;
  width: 260px;
  max-width: calc(100vw - 24px);
  max-height: 280px;
  overflow-y: auto;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-radius: 12px;
  box-shadow: var(--shadow);
  padding: 6px;
  z-index: 20;
  animation: modal-in 0.15s;
}

.mention-item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 7px 8px;
  border: none;
  background: transparent;
  border-radius: 8px;
  cursor: pointer;
  font-size: 13px;
  text-align: left;
  transition: background 0.12s;
}

.mention-item:hover {
  background: var(--bg-hover);
}

.mention-item .avatar.small {
  width: 28px;
  height: 28px;
  font-size: 12px;
}

.mention-role {
  font-size: 11px;
  color: var(--primary);
  margin-left: auto;
  flex-shrink: 0;
}

/* @ 快捷按钮 */
.mention-btn:hover {
  color: var(--primary);
}

.at-symbol {
  font-size: 20px;
  font-weight: 700;
  line-height: 1;
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
