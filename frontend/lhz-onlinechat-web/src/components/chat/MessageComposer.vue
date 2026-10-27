<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useChatStore } from '@/stores/chat'
import { useGroupStore } from '@/stores/group'
import { useWebSocketStore } from '@/stores/websocket'
import { messageApi } from '@/api/message'
import { MAX_MESSAGE_LENGTH } from '@/constants/limits'
import { useToast } from '@/composables/useToast'
import { genMessageId, sendMessage } from '@/utils/messageSend'
import { compressChatImage } from '@/utils/image'
import { useVoiceRecorder, type VoiceRecording } from '@/composables/useVoiceRecorder'
import { messagePreview } from '@/utils/forward'
import { formatVoiceDuration } from '@/utils/voice'
import EmojiPanel from './composer/EmojiPanel.vue'
import MentionPanel from './composer/MentionPanel.vue'
import type { WsMessage, ChatType, GroupMemberInfo } from '@/types'
import type { ChatMessage } from '@/stores/chat'

/**
 * 消息输入区：草稿、Markdown 开关、表情、@提及、图片上传、引用回复、字数、发送提示、输入中。
 *
 * 与父级的分工：
 *   - 父级（ChatArea）只管布局、滚动与消息列表；
 *   - 发送成功后只 emit `sent`，由父级决定滚到底部（这里不碰消息容器）；
 *   - 父级需要往里塞内容/提示时走 expose：`reply(msg)` / `reedit(content)` / `setHint(text)` / `focus()`。
 */
const props = defineProps<{ chat: { type: ChatType; id: number; name: string } }>()
const emit = defineEmits<{ sent: [] }>()

const auth = useAuthStore()
const chatStore = useChatStore()
const groupStore = useGroupStore()
const ws = useWebSocketStore()
const { toast } = useToast()

// ==================== 草稿（按会话） ====================
/**
 * 草稿按会话存（chat store 的 drafts）：普通文本与 Markdown 各存各的、互不带内容，
 * 切到别的会话再切回来内容还在 —— 也就不会出现"A 的草稿发到 B"。
 * 输入框、表情/@ 插入、发送按钮都只作用于 activeText（当前会话 + 当前模式对应的那一份）。
 */
const draftKey = computed(() => chatStore.sessionKey(props.chat.type, props.chat.id))
const draft = computed(() => chatStore.getDraft(draftKey.value))
/** Markdown 模式也按会话记忆（A 会话用 MD、切到 B 用普通，各自保留） */
const markdownMode = computed({
  get: () => draft.value.markdown,
  set: (value: boolean) => chatStore.setDraft(draftKey.value, { markdown: value })
})
const activeText = computed({
  get: () => (markdownMode.value ? draft.value.markdownText : draft.value.text),
  set: (value: string) => chatStore.setDraft(
    draftKey.value,
    markdownMode.value ? { markdownText: value } : { text: value }
  )
})
const inputEl = ref<HTMLInputElement | HTMLTextAreaElement | null>(null)
const sendHint = ref('')

// ==================== 正在输入 ====================
/** 我这边也输入：私聊每 3 秒最多发一帧（服务端只转发，不发对方就看不到"正在输入…"） */
let lastTypingSentAt = 0

function notifyTyping() {
  const chat = props.chat
  if (chat.type !== 'private' || !auth.user || !ws.connected) return
  if (!activeText.value.trim()) return
  const now = Date.now()
  if (now - lastTypingSentAt < 3000) return
  lastTypingSentAt = now
  ws.sendMessage({
    type: 'typing',
    from: String(auth.user.id),
    to: String(chat.id),
    content: '',
    timestamp: now,
    messageId: '',
    messageType: 0,
    senderName: '',
    senderAvatar: null
  })
}

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
  const start = el.selectionStart ?? activeText.value.length
  const end = el.selectionEnd ?? activeText.value.length
  const text = activeText.value
  activeText.value = text.slice(0, start) + emoji + text.slice(end)
  nextTick(() => {
    const pos = start + emoji.length
    el.focus()
    el.setSelectionRange(pos, pos)
  })
}

/** 点面板与按钮以外的区域收起表情面板（面板的 DOM 引用在本组件，才能判断内外） */
function onDocClick(e: MouseEvent) {
  const target = e.target as HTMLElement
  if (!emojiPanelRef.value?.contains(target) && !emojiBtnRef.value?.contains(target)) {
    showEmojiPanel.value = false
  }
}

// ==================== @ 提及 ====================
const mentionOpen = ref(false)
const mentionQuery = ref('')

const mentionFiltered = computed(() => {
  const q = mentionQuery.value.trim().toLowerCase()
  const list = groupStore.members.filter(m => !q || m.nickname.toLowerCase().includes(q))
  return list.slice(0, 8)
})

const inputPlaceholder = computed(() => {
  if (markdownMode.value) return 'Markdown：支持 # 标题、**粗体**、`代码`、- 列表（Ctrl+Enter 发送）'
  return props.chat.type === 'group' ? '输入消息… 输入 @ 可提及成员' : '输入消息…'
})

/** 接近上限才显示字数：平时不打扰，快满了才有存在感 */
const showLengthHint = computed(() => activeText.value.length >= MAX_MESSAGE_LENGTH * 0.8)

/** 超限就禁止发送（服务端同样会拒，这里是为了不让人白等一下） */
const isOverLengthLimit = computed(() => activeText.value.length > MAX_MESSAGE_LENGTH)

/**
 * 输入框按键：@ 键直接打开成员面板（双保险，避免 input 事件异常）。
 * 普通模式 Enter 发送；Markdown 模式 Enter 是换行（多行语法得能敲出来），Ctrl/Cmd+Enter 才发送。
 */
function onInputKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter') {
    if (markdownMode.value && !(e.ctrlKey || e.metaKey)) return
    send()
    return
  }
  if ((e.key === '@' || e.key === '＠') && props.chat.type === 'group') {
    mentionOpen.value = true
    mentionQuery.value = ''
    if (groupStore.members.length === 0) {
      groupStore.fetchMembers(props.chat.id)
    }
  }
}

/** 输入变化时检测 @ 触发成员选择（仅群聊），兼容全角 ＠；顺便给对方一个"正在输入"信号 */
function onInputChange() {
  notifyTyping()
  const el = inputEl.value
  if (!el || props.chat.type !== 'group') {
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
  activeText.value = text.slice(0, start) + insert + text.slice(caret)
  nextTick(() => {
    const pos = start + insert.length
    el.focus()
    el.setSelectionRange(pos, pos)
  })
  mentionOpen.value = false
}

function openMentionPicker() {
  const el = inputEl.value
  mentionOpen.value = true
  mentionQuery.value = ''
  if (el) {
    const caret = el.selectionStart ?? activeText.value.length
    const end = el.selectionEnd ?? caret
    activeText.value = `${activeText.value.slice(0, caret)}@${activeText.value.slice(end)}`
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

/** 命中「回复」：把目标挂到输入框上方（父级收到 MessageBubble 的 reply 事件后调用 expose.reply） */
function reply(msg: WsMessage) {
  replyTarget.value = {
    messageId: msg.messageId,
    // 引用条是给人看的：语音/图片别显示 URL，转发别显示 JSON
    content: messagePreview(msg.content || '', msg.messageType).slice(0, 50),
    senderName: msg.senderName || '对方'
  }
  inputEl.value?.focus()
}

// ==================== 发送 ====================
function send() {
  if (!auth.user) return
  // 只取当前模式那一份草稿（普通文本 / Markdown 各自独立暂存）
  const text = activeText.value.trim()
  if (!text) return
  // 超长不发出去：服务端会拒（回 message_failed），但没必要让用户等那一下，草稿也留着
  if (text.length > MAX_MESSAGE_LENGTH) {
    toast(`消息内容不能超过 ${MAX_MESSAGE_LENGTH} 字（当前 ${text.length} 字）`, 'error')
    return
  }
  // 断线时不再直接丢弃消息：照常乐观上屏，由 sendMessage 的返回值决定是否标记"发送失败"（可重试）
  sendHint.value = ws.connected
    ? ''
    : (ws.reconnectExhausted ? '实时连接已断开，请点顶部「重新连接」' : '连接已断开，正在重连，请稍候…')
  const key = chatStore.sessionKey(props.chat.type, props.chat.id)
  const msg: ChatMessage = {
    type: props.chat.type === 'private' ? 'private_message' : 'group_message',
    from: String(auth.user.id),
    to: String(props.chat.id),
    content: text,
    timestamp: Date.now(),
    messageId: genMessageId(),
    messageType: markdownMode.value ? 3 : 0,
    senderName: auth.user.nickname,
    senderAvatar: auth.user.avatar,
    mentions: parseMentions(text),
    replyTo: replyTarget.value?.messageId,
    replyContent: replyTarget.value?.content,
    replySender: replyTarget.value?.senderName,
    status: 'sending'
  }
  chatStore.addMessage(msg, auth.user.id)
  if (!sendMessage(ws, msg)) chatStore.setMessageStatus(key, msg.messageId, 'failed')
  // 只清当前会话当前模式的草稿：另一种模式、以及别的会话的草稿都留着
  chatStore.setDraft(key, markdownMode.value ? { markdownText: '' } : { text: '' })
  replyTarget.value = null
  emit('sent')
}

/**
 * 重试发送失败的消息：沿用原 messageId（服务端按它幂等去重，不会产生重复消息），
 * 只有真正写入连接才回到"发送中"，否则保持失败态并把提示留给用户。
 */
function retryMessage(msg: ChatMessage) {
  if (!auth.user) return
  // 重发不会让超长内容变合法：直接说清楚，别让它再被服务端拒一次
  if ((msg.content || '').length > MAX_MESSAGE_LENGTH) {
    toast(`这条消息超过 ${MAX_MESSAGE_LENGTH} 字，无法重发（请精简后重新发送）`, 'error')
    return
  }
  const key = chatStore.sessionKey(props.chat.type, props.chat.id)
  if (sendMessage(ws, msg)) {
    chatStore.setMessageStatus(key, msg.messageId, 'sending')
    sendHint.value = ''
  } else {
    sendHint.value = '发送失败，请检查网络后重试'
  }
}
// ==================== 图片消息 ====================
/** 上传返回时，用户是否还停在发起上传的那个会话（决定要不要通知父级滚到底部） */
function isCurrentChat(target: { type: string; id: number }): boolean {
  return props.chat.type === target.type && props.chat.id === target.id
}

const imageInputRef = ref<HTMLInputElement | null>(null)
const sendingImage = ref(false)

async function onImageSelect(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!auth.user) return
  if (!ws.connected) {
    sendHint.value = '连接已断开，正在重连，请稍候…'
    return
  }
  if (!file.type.startsWith('image/')) {
    toast('请选择图片文件', 'error')
    return
  }
  if (file.size > 5 * 1024 * 1024) {
    toast('发送图片太大，发送失败（最大 5MB）', 'error')
    return
  }

  // 上传是异步的，期间用户可能已切到别的会话 / 登出：目标与身份必须在 await 之前定下来，
  // 否则图片会发进「现在打开的」那个会话
  const target = { type: props.chat.type, id: props.chat.id }
  const me = auth.user

  sendingImage.value = true
  sendHint.value = ''
  try {
    // 先压缩（长边 ≤1600px、WebP）：气泡里最多只渲染到 260px，
    // 没必要让每个接收者都把手机原图整份下下来
    const compressed = await compressChatImage(file)
    const res = await messageApi.uploadImage(compressed)
    if (!res.success || !res.data?.url) {
      const message = res.message || ''
      toast(message.includes('5MB') || message.includes('大小')
        ? '发送图片太大，发送失败（最大 5MB）'
        : `图片发送失败：${message || '请重试'}`, 'error')
      return
    }
    const msg: ChatMessage = {
      type: target.type === 'private' ? 'private_message' : 'group_message',
      from: String(me.id),
      to: String(target.id),
      content: res.data.url,
      timestamp: Date.now(),
      messageId: genMessageId(),
      messageType: 1,
      senderName: me.nickname,
      senderAvatar: me.avatar,
      status: 'sending'
    }
    chatStore.addMessage(msg, me.id)
    if (!sendMessage(ws, msg)) {
      chatStore.setMessageStatus(chatStore.sessionKey(target.type, target.id), msg.messageId, 'failed')
    }
    if (isCurrentChat(target)) emit('sent')
  } catch {
    toast('图片发送失败，请重试', 'error')
  } finally {
    sendingImage.value = false
  }
}

// ==================== 语音消息 ====================
const sendingVoice = ref(false)

/**
 * 录音 → 上传 → 发送。录制与上传分开：`useVoiceRecorder` 只产出 File，
 * 这里才碰网络与 store（与图片上传同一套发送路径）。
 */
// 解构成顶层 ref：模板里才会自动解包（嵌套在对象里的 ref 在模板中不会解包）
const {
  supported: voiceSupported,
  recording: voiceRecording,
  seconds: voiceSeconds,
  maxSeconds: voiceMaxSeconds,
  start: startVoiceRecording,
  stop: stopVoiceRecording,
  cancel: cancelVoiceRecording
} = useVoiceRecorder({
  onComplete: (recording) => { void uploadAndSendVoice(recording) },
  onError: (message) => toast(message, 'error')
})

function onMicClick() {
  if (voiceRecording.value) {
    stopVoiceRecording()
    return
  }
  if (sendingVoice.value) return
  void startVoiceRecording()
}

async function uploadAndSendVoice(recording: VoiceRecording) {
  if (!auth.user) return
  if (!ws.connected) {
    sendHint.value = '连接已断开，正在重连，请稍候…'
    return
  }
  // 同图片：目标与身份在 await 之前定下来，上传期间切会话不能让语音串到新会话
  const target = { type: props.chat.type, id: props.chat.id }
  const me = auth.user

  sendingVoice.value = true
  sendHint.value = ''
  try {
    const res = await messageApi.uploadVoice(recording.file)
    if (!res.success || !res.data?.url) {
      toast(`语音发送失败：${res.message || '请重试'}`, 'error')
      return
    }
    const msg: ChatMessage = {
      type: target.type === 'private' ? 'private_message' : 'group_message',
      from: String(me.id),
      to: String(target.id),
      content: res.data.url,
      timestamp: Date.now(),
      messageId: genMessageId(),
      messageType: 5,
      senderName: me.nickname,
      senderAvatar: me.avatar,
      status: 'sending'
    }
    chatStore.addMessage(msg, me.id)
    if (!sendMessage(ws, msg)) {
      chatStore.setMessageStatus(chatStore.sessionKey(target.type, target.id), msg.messageId, 'failed')
    }
    if (isCurrentChat(target)) emit('sent')
  } catch {
    toast('语音发送失败，请重试', 'error')
  } finally {
    sendingVoice.value = false
  }
}

// ==================== 对外接口 ====================
/** 撤回后「重新编辑」：把原内容填回当前会话草稿并聚焦 */
function reedit(content: string) {
  activeText.value = content
  nextTick(() => {
    inputEl.value?.focus()
    const len = content.length
    inputEl.value?.setSelectionRange(len, len)
  })
}

/** 父级（ChatLayout → ChatArea）要显示输入栏提示时调用（拉黑/禁言/被拒原因） */
function setHint(text: string) {
  sendHint.value = text
}

function focus() {
  inputEl.value?.focus()
}

defineExpose({ reply, reedit, setHint, focus, retry: retryMessage })

// 换会话：清引用、提示、面板与节流计时（草稿本身按会话存在 store 里，不清）
watch(() => props.chat.id, () => {
  replyTarget.value = null
  sendHint.value = ''
  showEmojiPanel.value = false
  mentionOpen.value = false
  lastTypingSentAt = 0
  cancelVoiceRecording()
})

onMounted(() => document.addEventListener('click', onDocClick))
onUnmounted(() => document.removeEventListener('click', onDocClick))
</script>

<template>
  <!-- 引用回复横幅 -->
  <div class="reply-bar" v-if="replyTarget">
    <span class="reply-bar-text">回复 {{ replyTarget.senderName }}：{{ replyTarget.content }}</span>
    <button class="reply-cancel" @click="replyTarget = null">✕</button>
  </div>

  <div class="chat-input-bar">
    <!-- @ 成员选择浮层 -->
    <MentionPanel v-if="mentionOpen && mentionFiltered.length" :members="mentionFiltered" @pick="pickMention" />
    <!-- 表情面板（包一层 div 是为了让"点外面关闭"能判断内外） -->
    <div ref="emojiPanelRef">
      <EmojiPanel v-if="showEmojiPanel" @pick="insertEmoji" />
    </div>
    <button class="emoji-btn md-btn" :class="{ active: markdownMode }" @click.stop="markdownMode = !markdownMode"
      :title="markdownMode ? '退出 Markdown 模式' : 'Markdown 模式（Ctrl+Enter 发送）'">
      <span class="md-symbol">MD</span>
    </button>
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
    <!-- 语音：没录音时点一下开始，录音中点一下结束并发送（不支持 MediaRecorder 的浏览器直接不显示） -->
    <button v-if="voiceSupported" class="emoji-btn voice-btn" :class="{ recording: voiceRecording }"
      :disabled="sendingVoice" @click.stop="onMicClick"
      :title="voiceRecording ? '结束并发送语音' : '录语音（最长 60 秒）'">
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3z" />
        <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
        <line x1="12" y1="19" x2="12" y2="22" />
      </svg>
    </button>
    <template v-if="voiceRecording">
      <span class="voice-rec">
        <span class="voice-dot"></span>
        录音中 {{ formatVoiceDuration(voiceSeconds) }} / {{ formatVoiceDuration(voiceMaxSeconds) }}
      </span>
      <button class="voice-cancel-btn" @click.stop="cancelVoiceRecording()">取消</button>
      <button class="send-btn" @click.stop="stopVoiceRecording()">发送</button>
    </template>
    <template v-else>
      <input ref="inputEl" v-if="!markdownMode" v-model="activeText" class="input" :placeholder="inputPlaceholder" @keydown="onInputKeydown" @input="onInputChange" />
      <textarea v-else ref="inputEl" v-model="activeText" class="input md-input" :placeholder="inputPlaceholder"
        rows="3" @keydown="onInputKeydown" @input="onInputChange"></textarea>
      <span class="input-counter" v-if="showLengthHint" :class="{ over: isOverLengthLimit }">
        {{ activeText.length }} / {{ MAX_MESSAGE_LENGTH }}
      </span>
      <button class="send-btn" @click="send" :disabled="!activeText.trim() || isOverLengthLimit">发送</button>
    </template>
  </div>
  <p class="send-hint" v-if="sendHint">{{ sendHint }}</p>
</template>

<style scoped>
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

/* Markdown 模式开关：开启后输入框换多行、消息按 Markdown 渲染 */
.md-btn.active {
  background: var(--active-bg);
  color: var(--primary);
}

.md-symbol {
  font-size: 12.5px;
  font-weight: 700;
  letter-spacing: 0.2px;
}

/* 字数提示：接近上限才出现，超限变红（此时发送按钮同时禁用） */
.input-counter {
  flex-shrink: 0;
  font-size: 11.5px;
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.input-counter.over {
  color: var(--danger);
  font-weight: 600;
}

/* 多行输入：Enter 换行，Ctrl/Cmd+Enter 发送 */
.md-input {
  resize: none;
  min-height: 62px;
  max-height: 160px;
  line-height: 1.5;
  padding-top: 8px;
  padding-bottom: 8px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 13.5px;
  overflow-y: auto;
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

.voice-btn.recording {
  background: var(--danger);
  color: #fff;
}

.voice-rec {
  flex: 1;
  min-width: 0;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: var(--text-secondary);
}

.voice-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--danger);
  animation: voice-pulse 1s ease-in-out infinite;
}

@keyframes voice-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.25; }
}

.voice-cancel-btn {
  border: 1px solid var(--border);
  background: transparent;
  color: var(--text-secondary);
  font-size: 13px;
  padding: 7px 14px;
  border-radius: 10px;
  cursor: pointer;
  flex-shrink: 0;
}

.voice-cancel-btn:hover {
  background: var(--bg-hover);
}

.send-hint {
  padding: 0 20px 10px;
  font-size: 12px;
  color: var(--danger);
  background: var(--bg-white);
}
</style>