<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import Avatar from '@/components/Avatar.vue'
import MessageBubble from './MessageBubble.vue'
import { emojiGroups } from '@/constants/emojis'
import { useAuthStore } from '@/stores/auth'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import { messageApi } from '@/api/message'
import { useToast } from '@/composables/useToast'
import { formatMsgTime } from '@/utils/format'
import type { WsMessage, ChatType, GroupMemberInfo } from '@/types'

const props = defineProps<{
  chat: { type: ChatType; id: number; name: string } | null
  mobileChatOpen: boolean
}>()
const emit = defineEmits<{ back: []; members: []; announcement: [] }>()

const auth = useAuthStore()
const friendStore = useFriendStore()
const groupStore = useGroupStore()
const chatStore = useChatStore()
const ws = useWebSocketStore()
const { toast } = useToast()

const inputText = ref('')
const inputEl = ref<HTMLInputElement | null>(null)
const msgContainer = ref<HTMLElement | null>(null)
const sendHint = ref('')
const lightboxUrl = ref('')

const currentMessages = computed(() => {
  if (!props.chat) return []
  const key = `${props.chat.type}_${props.chat.id}`
  return chatStore.messages.get(key) || []
})

// 聊天窗口副标题：私聊显示原昵称+在线状态（有备注时），群聊显示人数
const chatSub = computed(() => {
  if (!props.chat) return ''
  if (props.chat.type === 'private') {
    const f = friendStore.friends.find(x => x.userId === props.chat!.id)
    if (!f) return ''
    const status = f.isOnline ? '在线' : '离线'
    return f.remark ? `${f.nickname} · ${status}` : status
  }
  const g = groupStore.groups.find(x => x.id === props.chat!.id)
  return g ? `${g.memberCount} 人` : ''
})

// 当前会话头像（私聊=好友头像，群聊=群头像）
const chatAvatar = computed(() => {
  if (!props.chat) return null
  if (props.chat.type === 'private') {
    return friendStore.friends.find(x => x.userId === props.chat!.id)?.avatar ?? null
  }
  return groupStore.groups.find(x => x.id === props.chat!.id)?.avatar ?? null
})

// 群公告（横幅）
const currentAnnouncement = computed(() => {
  if (!props.chat || props.chat.type !== 'group') return ''
  return groupStore.groups.find(g => g.id === props.chat!.id)?.announcement || ''
})

// ==================== 历史加载与滚动 ====================
const historyMeta = computed(() => {
  if (!props.chat) return null
  return chatStore.historyMeta.get(chatStore.sessionKey(props.chat.type, props.chat.id))
})
const historyHasMore = computed(() => !!historyMeta.value?.hasMore)
const historyLoading = computed(() => !!historyMeta.value?.loading)
const historyHint = computed(() => {
  if (historyLoading.value) return '正在加载更早的消息…'
  if (historyHasMore.value) return '上滑加载更早的消息'
  if (currentMessages.value.length > 0) return '没有更多消息了'
  return ''
})

function scrollToBottom() {
  nextTick(() => {
    if (msgContainer.value) {
      msgContainer.value.scrollTop = msgContainer.value.scrollHeight
    }
  })
}

/** 是否已滚到接近底部（自动滚动阈值） */
function isNearBottom(): boolean {
  const el = msgContainer.value
  if (!el) return true
  return el.scrollHeight - el.scrollTop - el.clientHeight < 120
}

/** 滚到顶部时加载更早的历史，并保持当前视口位置 */
async function onMessagesScroll() {
  const el = msgContainer.value
  if (!el || !props.chat) return
  if (el.scrollTop > 24) return
  if (historyLoading.value || !historyHasMore.value) return
  const prevHeight = el.scrollHeight
  await chatStore.loadMoreHistory(props.chat.type, props.chat.id, auth.user?.id)
  nextTick(() => {
    if (msgContainer.value) {
      msgContainer.value.scrollTop = msgContainer.value.scrollHeight - prevHeight
    }
  })
}

/** 会话切换：加载第一页历史并滚到底部（供父级定位等待） */
let historyReady: Promise<void> = Promise.resolve()
watch(() => props.chat, (chat) => {
  if (!chat) return
  replyTarget.value = null
  sendHint.value = ''
  historyReady = (async () => {
    await chatStore.loadHistory(chat.type, chat.id, 1, auth.user?.id)
    scrollToBottom()
  })()
}, { immediate: true })

/** 新消息到达时：接近底部才滚动（用户正在上翻历史时不打扰） */
function scrollToBottomIfNear() {
  if (isNearBottom()) scrollToBottom()
}

/** 设置输入栏提示（如"对方已将你拉黑"） */
function setHint(text: string) {
  sendHint.value = text
}

/** 定位消息：已加载则滚动到该行并高亮；否则继续翻更早的历史（最多 10 页） */
async function scrollToMessage(messageId: string) {
  const chat = props.chat
  if (!chat) return
  await historyReady
  const key = chatStore.sessionKey(chat.type, chat.id)
  // 先让历史加载的 scrollToBottom nextTick 落定，避免其覆盖定位滚动
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

defineExpose({ scrollToBottomIfNear, setHint, scrollToMessage })

// ==================== 表情 ====================
const showEmojiPanel = ref(false)
const emojiPanelRef = ref<HTMLElement | null>(null)
const emojiBtnRef = ref<HTMLElement | null>(null)

function toggleEmojiPanel() {
  showEmojiPanel.value = !showEmojiPanel.value
}

/** 在输入框光标位置插入表情，并恢复焦点与光标 */
function insertEmoji(emoji: string) {
  const el = inputEl.value
  if (!el) return
  const start = el.selectionStart ?? inputText.value.length
  const end = el.selectionEnd ?? inputText.value.length
  const text = inputText.value
  inputText.value = text.slice(0, start) + emoji + text.slice(end)
  nextTick(() => {
    const pos = start + emoji.length
    el.focus()
    el.setSelectionRange(pos, pos)
  })
}

function onDocClick(e: MouseEvent) {
  const target = e.target as HTMLElement
  if (!emojiPanelRef.value?.contains(target) && !emojiBtnRef.value?.contains(target)) {
    showEmojiPanel.value = false
  }
}
onMounted(() => document.addEventListener('click', onDocClick))
onUnmounted(() => document.removeEventListener('click', onDocClick))

// ==================== @ 提及 ====================
const mentionOpen = ref(false)
const mentionQuery = ref('')

const mentionFiltered = computed(() => {
  const q = mentionQuery.value.trim().toLowerCase()
  const list = groupStore.members.filter(m => !q || m.nickname.toLowerCase().includes(q))
  return list.slice(0, 8)
})

const inputPlaceholder = computed(() =>
  props.chat?.type === 'group' ? '输入消息… 输入 @ 可提及成员' : '输入消息…'
)

/** 输入框按键：@ 键直接打开成员面板（双保险，避免 input 事件异常），Enter 发送 */
function onInputKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter') {
    send()
    return
  }
  if ((e.key === '@' || e.key === '＠') && props.chat?.type === 'group') {
    mentionOpen.value = true
    mentionQuery.value = ''
    if (groupStore.members.length === 0) {
      groupStore.fetchMembers(props.chat.id)
    }
  }
}

/** 输入变化时检测 @ 触发成员选择（仅群聊），兼容全角 ＠ */
function onInputChange() {
  const el = inputEl.value
  if (!el || props.chat?.type !== 'group') {
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
      if (groupStore.members.length === 0) {
        groupStore.fetchMembers(props.chat.id)
      }
      return
    }
  }
  mentionOpen.value = false
}

function pickMention(m: GroupMemberInfo) {
  const el = inputEl.value
  if (!el) return
  const text = el.value
  const caret = el.selectionStart ?? text.length
  const atIdx = Math.max(text.lastIndexOf('@', caret - 1), text.lastIndexOf('＠', caret - 1))
  const insert = `@${m.nickname} `
  const start = atIdx >= 0 ? atIdx : caret
  inputText.value = text.slice(0, start) + insert + text.slice(caret)
  nextTick(() => {
    const pos = start + insert.length
    el.focus()
    el.setSelectionRange(pos, pos)
  })
  mentionOpen.value = false
}

function openMentionPicker() {
  if (!props.chat) return
  const el = inputEl.value
  mentionOpen.value = true
  mentionQuery.value = ''
  if (el) {
    const caret = el.selectionStart ?? inputText.value.length
    const end = el.selectionEnd ?? caret
    inputText.value = `${inputText.value.slice(0, caret)}@${inputText.value.slice(end)}`
    nextTick(() => {
      const pos = caret + 1
      el.focus()
      el.setSelectionRange(pos, pos)
    })
  }
  if (groupStore.members.length === 0) {
    groupStore.fetchMembers(props.chat.id)
  }
}

/** 解析消息中的 @提及：返回被提及成员的账号 ID 列表（仅群聊） */
function parseMentions(text: string): number[] {
  const ids: number[] = []
  for (const m of groupStore.members) {
    if (text.includes(`@${m.nickname}`)) ids.push(m.userId)
  }
  return ids
}

// ==================== 引用回复 ====================
const replyTarget = ref<{ messageId: string; content: string; senderName: string } | null>(null)

function startReply(msg: WsMessage) {
  replyTarget.value = {
    messageId: msg.messageId,
    content: (msg.content || '').slice(0, 50),
    senderName: msg.senderName || '对方'
  }
  inputEl.value?.focus()
}

// ==================== 发送 ====================
function genId(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID()
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}

function send() {
  if (!props.chat || !auth.user) return
  const text = inputText.value.trim()
  if (!text) return
  if (!ws.connected) {
    sendHint.value = '连接已断开，正在重连，请稍候…'
    return
  }
  sendHint.value = ''
  const msg: WsMessage = {
    type: props.chat.type === 'private' ? 'private_message' : 'group_message',
    from: String(auth.user.id),
    to: String(props.chat.id),
    content: text,
    timestamp: Date.now(),
    messageId: genId(),
    messageType: 0,
    senderName: auth.user.nickname,
    senderAvatar: auth.user.avatar,
    mentions: parseMentions(text),
    replyTo: replyTarget.value?.messageId,
    replyContent: replyTarget.value?.content,
    replySender: replyTarget.value?.senderName
  }
  chatStore.addMessage(msg, auth.user.id)
  ws.sendMessage(msg)
  inputText.value = ''
  replyTarget.value = null
  scrollToBottom()
}

// ==================== 图片消息 ====================
const imageInputRef = ref<HTMLInputElement | null>(null)
const sendingImage = ref(false)

async function onImageSelect(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!props.chat || !auth.user) return
  if (!ws.connected) {
    sendHint.value = '连接已断开，正在重连，请稍候…'
    return
  }
  if (!file.type.startsWith('image/')) {
    toast('请选择图片文件')
    return
  }
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
      toast(msg.includes('5MB') || msg.includes('大小')
        ? '发送图片太大，发送失败（最大 5MB）'
        : `图片发送失败：${msg || '请重试'}`)
      return
    }
    const msg: WsMessage = {
      type: props.chat.type === 'private' ? 'private_message' : 'group_message',
      from: String(auth.user.id),
      to: String(props.chat.id),
      content: res.data.url,
      timestamp: Date.now(),
      messageId: genId(),
      messageType: 1,
      senderName: auth.user.nickname,
      senderAvatar: auth.user.avatar
    }
    chatStore.addMessage(msg, auth.user.id)
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
</script>

<template>
  <main class="chat-main" :class="{ 'is-show': mobileChatOpen }">
    <!-- 未选择会话 -->
    <div class="no-chat" v-if="!chat">
      <span class="empty-icon">💬</span>
      <p>选择一个会话开始聊天</p>
    </div>

    <!-- 聊天窗口 -->
    <template v-else>
      <div class="chat-header">
        <button class="back-btn" @click="emit('back')" title="返回">
          <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="15 18 9 12 15 6" />
          </svg>
        </button>
        <Avatar :name="chat.name" :url="chatAvatar" size="sm" />
        <div class="chat-title">
          <div class="chat-title-row">
            <span class="chat-type-tag" v-if="chat.type === 'group'">群</span>
            <span class="chat-name">{{ chat.name }}</span>
          </div>
          <span class="chat-sub">{{ chatSub }}</span>
        </div>
        <div class="header-spacer"></div>
        <button v-if="chat.type === 'group'" class="icon-btn" @click="emit('members')" title="群成员">
          <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
            <circle cx="9" cy="7" r="4" />
            <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
            <path d="M16 3.13a4 4 0 0 1 0 7.75" />
          </svg>
        </button>
      </div>

      <!-- 群公告横幅 -->
      <div class="announce-bar" v-if="chat.type === 'group' && currentAnnouncement" @click="emit('announcement')" title="查看群公告">
        <span class="announce-icon">📢</span>
        <span class="announce-text">{{ currentAnnouncement }}</span>
      </div>

      <div class="chat-messages" ref="msgContainer" @scroll="onMessagesScroll">
        <div class="history-load-hint" v-if="historyHint">{{ historyHint }}</div>
        <MessageBubble v-for="msg in currentMessages" :key="msg.messageId" :msg="msg"
          :chat-type="chat.type" :chat-id="chat.id" :chat-name="chat.name"
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
        <button v-if="chat.type === 'group'" class="emoji-btn mention-btn" @click.stop="openMentionPicker" title="提及成员">
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

    <!-- 图片放大预览 -->
    <div class="lightbox" v-if="lightboxUrl" @click="lightboxUrl = ''">
      <img :src="lightboxUrl" alt="图片预览" />
      <span class="lightbox-close">✕ 点击任意处关闭</span>
    </div>
  </main>
</template>

<style scoped>
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

/* 引用回复横幅 */
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
  min-width: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.reply-cancel {
  border: none;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  font-size: 14px;
  padding: 2px 6px;
  flex-shrink: 0;
}

/* 输入栏 */
.chat-input-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 16px calc(12px + env(safe-area-inset-bottom));
  background: var(--bg-white);
  border-top: 1px solid var(--border);
  position: relative;
}

.emoji-btn {
  width: 38px;
  height: 38px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  background: var(--bg-hover);
  border-radius: 50%;
  cursor: pointer;
  color: var(--text-secondary);
  flex-shrink: 0;
  transition: all 0.2s;
}

.emoji-btn:hover:not(:disabled) {
  background: var(--active-bg);
  color: var(--primary);
}

.emoji-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.mention-btn {
  color: var(--primary);
  font-weight: 700;
}

.at-symbol {
  font-size: 18px;
}

.hidden-file {
  display: none;
}

.input {
  flex: 1;
  min-width: 0;
}

.send-btn {
  padding: 9px 22px;
  border: none;
  border-radius: 10px;
  background: var(--mine-bubble);
  color: #fff;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  flex-shrink: 0;
  transition: all 0.2s;
  box-shadow: 0 4px 12px rgba(91, 108, 255, 0.3);
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

/* @ 成员选择浮层 */
.mention-panel {
  position: absolute;
  bottom: calc(100% + 8px);
  left: 16px;
  right: 16px;
  max-height: 240px;
  overflow-y: auto;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-radius: 12px;
  box-shadow: var(--shadow);
  z-index: 20;
  padding: 6px;
}

.mention-item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 8px 10px;
  border: none;
  border-radius: 8px;
  background: transparent;
  cursor: pointer;
  text-align: left;
  transition: background 0.15s;
}

.mention-item:hover {
  background: var(--bg-hover);
}

.mention-role {
  font-size: 11px;
  color: var(--primary);
  background: var(--active-bg);
  border-radius: 8px;
  padding: 0 6px;
  line-height: 16px;
  flex-shrink: 0;
}

/* 表情面板 */
.emoji-panel {
  position: absolute;
  bottom: calc(100% + 8px);
  left: 16px;
  width: 340px;
  max-height: 260px;
  overflow-y: auto;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-radius: 12px;
  box-shadow: var(--shadow);
  z-index: 20;
  padding: 12px;
}

.emoji-group {
  margin-bottom: 10px;
}

.emoji-group-title {
  font-size: 12px;
  color: var(--text-secondary);
  margin-bottom: 6px;
}

.emoji-grid {
  display: grid;
  grid-template-columns: repeat(8, 1fr);
  gap: 2px;
}

.emoji-item {
  font-size: 20px;
  padding: 4px;
  border: none;
  background: transparent;
  border-radius: 8px;
  cursor: pointer;
  transition: background 0.15s;
}

.emoji-item:hover {
  background: var(--bg-hover);
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
  cursor: pointer;
}

.lightbox img {
  max-width: 90vw;
  max-height: 85vh;
  border-radius: 8px;
  object-fit: contain;
}

.lightbox-close {
  position: fixed;
  bottom: 24px;
  color: rgba(255, 255, 255, 0.8);
  font-size: 13px;
}

/* 移动端适配（聊天区部分） */
@media (max-width: 720px) {
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
}

@media (min-width: 721px) {
  .back-btn {
    display: none;
  }
}
</style>
