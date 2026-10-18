<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import MessageBubble from './MessageBubble.vue'
import ChatHeader from './ChatHeader.vue'
import ChatSearchPanel from './ChatSearchPanel.vue'
import MessageComposer from './MessageComposer.vue'
import ForwardModal from './modals/ForwardModal.vue'
import ForwardViewerModal from './modals/ForwardViewerModal.vue'
import { useAuthStore } from '@/stores/auth'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import { useToast } from '@/composables/useToast'
import { copyText } from '@/utils/clipboard'
import {
  MAX_FORWARD_MESSAGES, buildBatchCopyText, buildForwardBundle,
  type ForwardTarget, type ForwardedItem
} from '@/utils/forward'
import { genMessageId, sendMessage } from '@/utils/messageSend'
import type { WsMessage, ChatType } from '@/types'
import type { ChatMessage } from '@/stores/chat'

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

const msgContainer = ref<HTMLElement | null>(null)
/** 输入区实例（回复/重新编辑/提示都转发给它） */
const composerRef = ref<InstanceType<typeof MessageComposer> | null>(null)
const lightboxUrl = ref('')

// ==================== 会话内搜索 ====================
/** 搜索条开合（头部按钮控制）；结果、防抖、分页都在 ChatSearchPanel 里 */
const searchOpen = ref(false)
/** 搜索关键词高亮（传给 MessageBubble 渲染 <mark>） */
const highlightKeyword = ref('')

function closeSearch() {
  searchOpen.value = false
  highlightKeyword.value = ''
}

function toggleSearch() {
  if (searchOpen.value) closeSearch()
  else searchOpen.value = true
}

/** 点击搜索结果：定位到消息并高亮关键词 */
function onSearchLocate(messageId: string, keyword: string) {
  highlightKeyword.value = keyword
  if (messageId) scrollToMessage(messageId)
}

const currentMessages = computed(() => {
  if (!props.chat) return []
  const key = `${props.chat.type}_${props.chat.id}`
  return chatStore.messages.get(key) || []
})

/** 未读分隔线锚点：该会话打开时的第一条未读消息 ID（空串 = 没有未读） */
const unreadAnchor = computed(() => (
  props.chat ? chatStore.unreadAnchorOf(chatStore.sessionKey(props.chat.type, props.chat.id)) : ''
))

// 群公告（横幅）
const currentAnnouncement = computed(() => {
  if (!props.chat || props.chat.type !== 'group') return ''
  return groupStore.groups.find(g => g.id === props.chat!.id)?.announcement || ''
})

// ==================== 正在输入 ====================
/**
 * 对方是否正在输入：收到一次 typing 帧就置 true，4 秒内没有新帧自动消失。
 * 服务端只做转发（私聊），所以这里不落库、也不参与未读。
 */
const peerTyping = ref(false)
let peerTypingTimer: number | null = null

/** 父级收到 typing 帧时调用（只对当前会话的人调用，见 ChatLayout） */
function setPeerTyping() {
  peerTyping.value = true
  if (peerTypingTimer) clearTimeout(peerTypingTimer)
  peerTypingTimer = window.setTimeout(() => { peerTyping.value = false }, 4000)
}

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

/** 把某条消息滚进视口（并可选高亮）；返回是否找到该行 */
function scrollRowIntoView(messageId: string, flash = false): boolean {
  const el = msgContainer.value?.querySelector<HTMLElement>(`[data-mid="${CSS.escape(messageId)}"]`)
  if (!el) return false
  el.scrollIntoView({ block: 'center' })
  if (flash) {
    el.classList.add('msg-highlight')
    setTimeout(() => el.classList.remove('msg-highlight'), 2200)
  }
  return true
}

/**
 * 会话切换：加载第一页历史；有未读分隔线就定位到「第一条未读」，否则滚到底部。
 * 切走的上一个会话要清掉它自己的分隔线记录（否则下次进来会看到过期的分隔线）。
 */
let historyReady: Promise<void> = Promise.resolve()
watch(() => props.chat, (chat, prev) => {
  if (prev) chatStore.clearUnreadAnchor(chatStore.sessionKey(prev.type, prev.id))
  if (!chat) return
  highlightKeyword.value = ''
  // 切会话退出多选：选中的是上一个会话的消息，留着只会误操作
  exitSelectMode()
  // 输入提示属于上一个会话，切走就清；节流计时也重置，新会话第一条消息立刻能提示
  peerTyping.value = false
  if (peerTypingTimer) {
    clearTimeout(peerTypingTimer)
    peerTypingTimer = null
  }
  const anchor = chatStore.unreadAnchorOf(chatStore.sessionKey(chat.type, chat.id))
  historyReady = (async () => {
    await chatStore.loadHistory(chat.type, chat.id, 1, auth.user?.id)
    await nextTick()
    if (!anchor || !scrollRowIntoView(anchor)) scrollToBottom()
  })().catch(err => {
    // 历史加载失败不能变成未处理的 Promise 拒绝：会话仍可用，用户上滑可重试
    console.error('[ChatArea] 加载历史消息失败', err)
  })
}, { immediate: true })

/** 新消息到达时：接近底部才滚动（用户正在上翻历史时不打扰） */
/** MessageBubble 的「回复」：转发给输入区挂引用 */
function onReply(msg: WsMessage) {
  composerRef.value?.reply(msg)
}

/** 发送失败消息的「重试」：同样交给输入区（发送逻辑只有一份） */
function onRetry(msg: ChatMessage) {
  composerRef.value?.retry(msg)
}

function scrollToBottomIfNear() {
  if (isNearBottom()) scrollToBottom()
}

/** 设置输入栏提示（如"对方已将你拉黑"）：转发给输入区显示 */
function setHint(text: string) {
  composerRef.value?.setHint(text)
}

/** 定位消息：已加载则滚动到该行并高亮；否则继续翻更早的历史（最多 10 页） */
async function scrollToMessage(messageId: string) {
  const chat = props.chat
  if (!chat) return
  await historyReady
  const key = chatStore.sessionKey(chat.type, chat.id)
  // 先让历史加载的 scrollToBottom nextTick 落定，避免其覆盖定位滚动
  await nextTick()
  if (scrollRowIntoView(messageId, true)) return
  for (let i = 0; i < 10; i++) {
    const meta = chatStore.historyMeta.get(key)
    if (!meta?.hasMore) break
    await chatStore.loadMoreHistory(chat.type, chat.id, auth.user?.id)
    await nextTick()
    if (scrollRowIntoView(messageId, true)) return
  }
}

/** 撤回后「重新编辑」：把原内容交回输入区（草稿按会话存，不会串会话） */
function reeditMessage(content: string) {
  composerRef.value?.reedit(content)
}

defineExpose({ scrollToBottomIfNear, setHint, scrollToMessage, setPeerTyping })

onMounted(() => window.addEventListener('keydown', onSelectKeydown))
onUnmounted(() => window.removeEventListener('keydown', onSelectKeydown))

// ==================== 多选 / 批量转发 ====================
const selectMode = ref(false)
const selectedIds = ref<Set<string>>(new Set())
const forwardOpen = ref(false)

/** 当前会话里被选中的消息（按列表顺序，复制/转发都保持上下文顺序） */
const selectedMessages = computed(() => currentMessages.value.filter(m => selectedIds.value.has(m.messageId)))

/** 转发目标：会话 + 好友 + 群，按 key 去重（没聊过的好友也要能转） */
const forwardTargets = computed<ForwardTarget[]>(() => {
  const out: ForwardTarget[] = []
  const seen = new Set<string>()
  const push = (t: ForwardTarget) => {
    const key = `${t.type}_${t.id}`
    if (seen.has(key)) return
    seen.add(key)
    out.push(t)
  }
  for (const s of chatStore.sessions) push({ type: s.type, id: s.id, name: s.name, avatar: s.avatar })
  for (const f of friendStore.friends) {
    push({ type: 'private', id: f.userId, name: f.remark || f.nickname, avatar: f.avatar })
  }
  for (const g of groupStore.groups) push({ type: 'group', id: g.id, name: g.name, avatar: g.avatar })
  return out
})

/** 从某条消息进入多选模式（右键菜单里的「多选」） */
function enterSelectMode(msg: ChatMessage) {
  selectMode.value = true
  selectedIds.value = new Set([msg.messageId])
}

function toggleSelect(msg: ChatMessage) {
  const next = new Set(selectedIds.value)
  if (next.has(msg.messageId)) {
    next.delete(msg.messageId)
  } else if (next.size >= MAX_FORWARD_MESSAGES) {
    toast(`一次最多选 ${MAX_FORWARD_MESSAGES} 条`)
    return
  } else {
    next.add(msg.messageId)
  }
  selectedIds.value = next
}

function exitSelectMode() {
  selectMode.value = false
  selectedIds.value = new Set()
  forwardOpen.value = false
}

/** 批量复制：每条一行「[发送者] 内容」 */
async function copySelected() {
  const list = selectedMessages.value
  if (list.length === 0) return
  const ok = await copyText(buildBatchCopyText(list))
  toast(ok ? `已复制 ${list.length} 条` : '复制失败，请手动选中文本复制')
}

/**
 * 批量转发：一次转发 = 一条「聊天记录」卡片消息（messageType = 4），所有目标共用同一份快照。
 * 卡片是只读快照，原消息后续被撤回也不影响已发出的卡片。
 */
function doForward(targets: ForwardTarget[]) {
  const list = selectedMessages.value
  if (!auth.user || list.length === 0) return
  const bundle = buildForwardBundle(list)
  let sent = 0
  for (const target of targets) {
    const isGroup = target.type === 'group'
    const msg: ChatMessage = {
      type: isGroup ? 'group_message' : 'private_message',
      from: String(auth.user.id),
      to: String(target.id),
      content: bundle,
      timestamp: Date.now(),
      messageId: genMessageId(),
      messageType: 4,
      senderName: auth.user.nickname,
      senderAvatar: auth.user.avatar,
      mentions: isGroup ? [] : undefined,
      status: 'sending'
    }
    chatStore.addMessage(msg, auth.user.id)
    if (sendMessage(ws, msg)) sent++
    else chatStore.setMessageStatus(chatStore.sessionKey(target.type, target.id), msg.messageId, 'failed')
  }
  forwardOpen.value = false
  exitSelectMode()
  toast(sent === targets.length
    ? `已转发 ${list.length} 条记录到 ${targets.length} 个会话`
    : `已转发 ${sent}/${targets.length} 个会话（其余发送失败，可在目标会话里重试）`)
}

/** 打开「聊天记录」卡片（只读明细） */
const forwardViewerItems = ref<ForwardedItem[] | null>(null)

function openForwardViewer(items: ForwardedItem[]) {
  forwardViewerItems.value = items
}

/** 多选模式下 Esc 退出（弹窗打开时交给弹窗自己处理） */
function onSelectKeydown(e: KeyboardEvent) {
  if (e.key !== 'Escape' || !selectMode.value || forwardOpen.value) return
  exitSelectMode()
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
      <ChatHeader :chat="chat" :typing="peerTyping" :search-open="searchOpen"
        @back="emit('back')" @members="emit('members')" @toggle-search="toggleSearch" />

      <ChatSearchPanel :chat="chat" :open="searchOpen" @close="closeSearch" @locate="onSearchLocate" />

      <!-- 群公告横幅 -->
      <div class="announce-bar" v-if="chat.type === 'group' && currentAnnouncement" @click="emit('announcement')" title="查看群公告">
        <span class="announce-icon">📢</span>
        <span class="announce-text">{{ currentAnnouncement }}</span>
      </div>

      <!-- 多选工具条：批量转发 / 复制 -->
      <div class="select-bar" v-if="selectMode">
        <span class="select-count">已选 {{ selectedIds.size }} 条<span class="select-max">（最多 {{ MAX_FORWARD_MESSAGES }} 条）</span></span>
        <button class="btn btn-sm btn-ghost" :disabled="selectedIds.size === 0" @click="copySelected">复制</button>
        <button class="btn btn-sm btn-primary" :disabled="selectedIds.size === 0" @click="forwardOpen = true">转发</button>
        <button class="btn btn-sm btn-ghost" @click="exitSelectMode">取消</button>
      </div>

      <div class="chat-messages" ref="msgContainer" @scroll="onMessagesScroll">
        <div class="history-load-hint" v-if="historyHint">{{ historyHint }}</div>
        <template v-for="msg in currentMessages" :key="msg.messageId">
          <!-- 未读分隔线：打开会话时把"从这条开始是新消息"标出来（定位也用它） -->
          <div class="unread-divider" v-if="unreadAnchor && msg.messageId === unreadAnchor">
            <span>以下是新消息</span>
          </div>
          <MessageBubble :msg="msg"
            :chat-type="chat.type" :chat-id="chat.id" :chat-name="chat.name"
            :highlight="highlightKeyword"
            :select-mode="selectMode" :selected="selectedIds.has(msg.messageId)"
            @reply="onReply" @retry="onRetry" @image-click="openLightbox"
            @reedit="reeditMessage" @select="enterSelectMode(msg)" @toggle-select="toggleSelect(msg)"
            @open-forward="openForwardViewer" />
        </template>
      </div>

      <MessageComposer ref="composerRef" :chat="chat" @sent="scrollToBottom" />
      <!-- 引用回复横幅 -->
    </template>

    <!-- 图片放大预览 -->
    <!-- 转发目标选择 -->
    <ForwardModal v-if="forwardOpen" :targets="forwardTargets" :count="selectedIds.size"
      @confirm="doForward" @close="forwardOpen = false" />

    <!-- 「聊天记录」卡片查看器 -->
    <ForwardViewerModal v-if="forwardViewerItems" :items="forwardViewerItems"
      @close="forwardViewerItems = null" />

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
  position: relative; /* 搜索结果下拉定位锚点 */
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

/* 多选工具条：紧贴消息列表上方 */
.select-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px;
  background: var(--bg-hover);
  border-top: 1px solid var(--border, #e5e7eb);
  font-size: 13px;
  flex-shrink: 0;
}

.select-count {
  margin-right: auto;
  color: var(--text-primary);
}

.select-max {
  color: var(--text-secondary);
  font-size: 12px;
}

/* 未读分隔线：打开会话时标出"从这里开始是新消息" */
.unread-divider {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
  color: var(--danger, #ef4444);
  font-size: 12px;
  user-select: none;
}

.unread-divider::before,
.unread-divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: currentColor;
  opacity: 0.35;
}

/* 对方正在输入：用弱化的文字，别抢消息本身的注意力 */
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

  .chat-messages {
    padding: 14px 12px;
  }

}
</style>