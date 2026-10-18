<script setup lang="ts">
import { computed, onUnmounted } from 'vue'
import Avatar from '@/components/Avatar.vue'
import ImageMessage from './messages/ImageMessage.vue'
import MarkdownMessage from './messages/MarkdownMessage.vue'
import ForwardMessage from './messages/ForwardMessage.vue'
import TextMessage from './messages/TextMessage.vue'
import VoiceMessage from './messages/VoiceMessage.vue'
import { useAuthStore } from '@/stores/auth'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import { useToast } from '@/composables/useToast'
import { useMessageMenu } from '@/composables/useMessageMenu'
import { copyText } from '@/utils/clipboard'
import type { ForwardedItem } from '@/utils/forward'
import { formatMsgTime } from '@/utils/format'
import type { WsMessage, ChatType } from '@/types'
import type { ChatMessage } from '@/stores/chat'

const props = defineProps<{
  msg: ChatMessage
  chatType: ChatType
  chatId: number
  chatName: string
  /** 搜索高亮关键词（会话内搜索定位时传入） */
  highlight?: string
  /** 多选模式：显示勾选框，点击整行切换选中（此时不弹右键菜单、图片不放大） */
  selectMode?: boolean
  /** 该消息是否已选中 */
  selected?: boolean
}>()
const emit = defineEmits<{
  reply: [msg: WsMessage]
  retry: [msg: ChatMessage]
  'image-click': [url: string]
  /** 撤回后重新编辑：把原内容交回输入框 */
  reedit: [content: string]
  /** 从该条消息进入多选模式 */
  select: []
  /** 多选模式下切换选中 */
  'toggle-select': []
  /** 打开「聊天记录」卡片（转发消息） */
  'open-forward': [items: ForwardedItem[]]
}>()

const auth = useAuthStore()
const chatStore = useChatStore()
const ws = useWebSocketStore()

const mine = Number(props.msg.from) === auth.user?.id

/** 图片点击：多选模式下是勾选，否则放大预览 */
function onImageClick() {
  if (props.selectMode) {
    emit('toggle-select')
    return
  }
  emit('image-click', props.msg.content)
}

/** 「聊天记录」卡片点击：多选模式下是勾选，否则打开只读明细 */
function onForwardOpen(items: ForwardedItem[]) {
  if (props.selectMode) {
    emit('toggle-select')
    return
  }
  emit('open-forward', items)
}


/** 该消息是否提及了当前用户 */
function isMentioned(msg: WsMessage): boolean {
  if (!msg.mentions?.length || !auth.user) return false
  return msg.mentions.includes(auth.user.id)
}

/** 是否是我发出的私聊消息（展示已读状态） */
function isMyPrivateMessage(msg: WsMessage): boolean {
  return props.chatType === 'private' && Number(msg.from) === auth.user?.id
}

/** 可回复：未撤回的消息 */
function canReply(msg: WsMessage): boolean {
  return !msg.isDeleted
}

/** 可撤回：自己发出的、未撤回的消息（服务端限 2 分钟内） */
function canRecall(msg: WsMessage): boolean {
  if (msg.isDeleted) return false
  return Number(msg.from) === auth.user?.id
}

function startReply(msg: WsMessage) {
  emit('reply', msg)
}

function recallMessage(msg: WsMessage) {
  if (!auth.user) return
  if (!window.confirm('确定撤回这条消息？')) return
  ws.sendMessage({
    type: 'message_recalled',
    from: String(auth.user.id),
    to: String(props.chatId),
    content: msg.messageId,
    timestamp: Date.now(),
    messageId: msg.messageId,
    messageType: 0,
    senderName: '',
    senderAvatar: null
  })
  // 本地乐观标记
  chatStore.markMessageRecalled(chatStore.sessionKey(props.chatType, props.chatId), msg.messageId)
}

// ==================== 操作菜单（右键 / 移动端长按） ====================
const { toast } = useToast()
// 菜单状态是**全局单例**：每条消息各持一份时，右键 A 再右键 B 会叠出两个菜单
const { position: menuPosition, openMenu, closeMenu, isMenuOpen } = useMessageMenu()
let longPressTimer: number | null = null

/** 菜单定位（只有 isMenuOpen 为真时才有意义；关闭时返回空对象） */
const menuStyle = computed(() => (menuPosition.value
  ? { left: `${menuPosition.value.x}px`, top: `${menuPosition.value.y}px` }
  : {}))

function onContextMenu(e: MouseEvent) {
  // 移动端长按浏览器也会派发 contextmenu，这里统一处理（长按另有 touch 兜底）
  e.preventDefault()
  openMenu(props.msg.messageId, e.clientX, e.clientY)
}

function onTouchStart(e: TouchEvent) {
  const t = e.touches[0]
  if (!t) return
  cancelLongPress()
  longPressTimer = window.setTimeout(() => openMenu(props.msg.messageId, t.clientX, t.clientY), 500)
}

function cancelLongPress() {
  if (longPressTimer) {
    clearTimeout(longPressTimer)
    longPressTimer = null
  }
}

/**
 * 行级事件包装。
 *
 * 注意：**不能**在模板里写 `@contextmenu="selectMode ? undefined : onContextMenu"` ——
 * 三元里的裸函数引用会被编译成 `$event => (条件 ? undefined : fn)`，只是返回函数、
 * 从不调用，表现就是"右键完全没反应"（构建和类型检查都发现不了）。
 */
function onRowClick() {
  if (props.selectMode) emit('toggle-select')
}

function onRowContextMenu(e: MouseEvent) {
  if (props.selectMode) return
  onContextMenu(e)
}

function onRowTouchStart(e: TouchEvent) {
  if (props.selectMode) return
  onTouchStart(e)
}

async function copyMessage(msg: ChatMessage) {
  const ok = await copyText(msg.content || '')
  toast(ok ? '已复制' : '复制失败，请手动选中文本复制', ok ? 'info' : 'error')
  closeMenu(true)
}

onUnmounted(() => {
  cancelLongPress()
  // 只关自己的菜单：别的消息的菜单不归这里管
  if (isMenuOpen(props.msg.messageId)) closeMenu(true)
})
</script>

<template>
  <div class="msg-row"
    :class="{ mine, 'is-markdown-msg': msg.messageType === 3 && !msg.isDeleted, selectable: selectMode, selected }"
    :data-mid="msg.messageId"
    @contextmenu="onRowContextMenu"
    @click="onRowClick"
    @touchstart.passive="onRowTouchStart" @touchend="cancelLongPress" @touchmove="cancelLongPress">
    <!-- 多选模式的勾选框 -->
    <span v-if="selectMode" class="msg-check" :class="{ checked: selected }">{{ selected ? '✓' : '' }}</span>

    <!-- 操作菜单：右键或移动端长按唤出（复制/引用/撤回/重新编辑/多选） -->
    <div class="ctx-menu" v-if="isMenuOpen(msg.messageId)" :style="menuStyle" @click.stop>
      <button class="ctx-item" @click="copyMessage(msg)">复制</button>
      <button class="ctx-item" v-if="canReply(msg)" @click="startReply(msg); closeMenu(true)">引用</button>
      <button class="ctx-item" v-if="canRecall(msg)" @click="recallMessage(msg); closeMenu(true)">撤回</button>
      <button class="ctx-item" v-if="mine && msg.isDeleted && msg.content"
        @click="emit('reedit', msg.content); closeMenu(true)">重新编辑</button>
      <button class="ctx-item" v-if="!msg.isDeleted" @click="emit('select'); closeMenu(true)">多选</button>
    </div>
    <Avatar class="msg-avatar" :name="msg.senderName || chatName" :url="msg.senderAvatar" size="sm" />
    <div class="msg-body">
      <span class="msg-sender" v-if="chatType === 'group' && !mine">
        {{ msg.senderName }}
      </span>
      <div class="reply-preview" v-if="msg.replyContent && !msg.isDeleted">
        <span class="reply-sender">{{ msg.replySender || '引用' }}</span>
        <span class="reply-text">{{ msg.replyContent }}</span>
      </div>
      <div class="msg-bubble" :class="{ mentioned: isMentioned(msg), 'is-image': msg.messageType === 1, 'is-markdown': msg.messageType === 3, 'is-forward': msg.messageType === 4, 'is-voice': msg.messageType === 5, recalled: msg.isDeleted }">
        <span v-if="msg.isDeleted" class="msg-recalled-text">消息已撤回</span>
        <!-- 消息体按类型拆成子组件：文本 / 图片 / Markdown / 聊天记录卡片 / 语音 -->
        <ImageMessage v-else-if="msg.messageType === 1" :src="msg.content" @click="onImageClick" />
        <MarkdownMessage v-else-if="msg.messageType === 3" :content="msg.content" :highlight="highlight" />
        <ForwardMessage v-else-if="msg.messageType === 4" :content="msg.content" @open="onForwardOpen" />
        <!-- 多选模式下语音不参与播放：pointer-events 关掉，点击落到整行上用于勾选 -->
        <div v-else-if="msg.messageType === 5" :class="{ 'voice-static': selectMode }">
          <VoiceMessage :src="msg.content" />
        </div>
        <TextMessage v-else :content="msg.content" :highlight="highlight" />
      </div>
      <div class="msg-actions">
        <button v-if="canReply(msg)" class="msg-recall-btn" @click.stop="startReply(msg)">回复</button>
        <button v-if="canRecall(msg)" class="msg-recall-btn" @click.stop="recallMessage(msg)">撤回</button>
      </div>
      <div class="msg-meta-line">
        <span class="msg-time">{{ formatMsgTime(msg.timestamp) }}</span>
        <!-- 发送失败：小字标记 + 重试（沿用同一 messageId，服务端幂等去重，不会重复上屏） -->
        <template v-if="mine && msg.status === 'failed'">
          <span class="msg-send-failed">发送失败</span>
          <button class="msg-retry-btn" @click.stop="emit('retry', msg)">重试</button>
        </template>
        <span v-else-if="isMyPrivateMessage(msg)" class="msg-status"
          :class="{ read: chatStore.isReadByPeer(msg.messageId) }">
          {{ chatStore.isReadByPeer(msg.messageId) ? '已读' : '未读' }}
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
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

/* 发送失败标记与重试入口（与已读状态同一行，不额外占位） */
.msg-send-failed {
  font-size: 11px;
  color: var(--danger);
  padding: 0 4px;
}

.msg-retry-btn {
  border: none;
  background: transparent;
  color: var(--primary);
  font-size: 11px;
  padding: 0 2px;
  cursor: pointer;
  text-decoration: underline;
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

/* 「聊天记录」转发卡片：不是气泡，是一张可点的卡（独立配色 + 左侧强调条） */
.msg-bubble.is-forward {
  background: transparent;
  padding: 0;
  border: none;
  box-shadow: none;
  max-width: 320px;
}


/* 多选模式：整行可点、显示勾选框，隐藏悬停操作按钮避免误触 */
.msg-row.selectable {
  cursor: pointer;
  user-select: none;
}

.msg-row.selectable .msg-actions {
  display: none;
}

.msg-row.selected {
  background: var(--bg-active, rgba(59, 130, 246, 0.1));
  border-radius: 8px;
}

.msg-check {
  width: 18px;
  height: 18px;
  flex-shrink: 0;
  align-self: center;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--border, #d1d5db);
  border-radius: 50%;
  font-size: 12px;
  color: var(--primary);
  background: var(--bg-panel, #fff);
}

.msg-check.checked {
  border-color: var(--primary);
  background: var(--primary);
  color: #fff;
}

/* 操作菜单（右键 / 长按）：固定定位到指针处，避免被气泡或列表裁掉 */
.ctx-menu {
  position: fixed;
  z-index: 300;
  min-width: 120px;
  padding: 4px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  background: var(--bg-panel, #fff);
  border: 1px solid var(--border, #e5e7eb);
  border-radius: 8px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.18);
}

.ctx-item {
  display: block;
  width: 100%;
  padding: 7px 10px;
  border: none;
  background: transparent;
  color: var(--text-primary);
  font-size: 13px;
  text-align: left;
  border-radius: 6px;
  cursor: pointer;
}

.ctx-item:hover {
  background: var(--bg-hover);
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

/* 定位消息高亮 */
.msg-row.msg-highlight .msg-bubble {
  animation: msg-highlight-flash 2.2s ease;
}
@keyframes msg-highlight-flash {
  0%, 55% {
    box-shadow: 0 0 0 3px var(--primary-light);
  }
  100% {
    box-shadow: none;
  }
}


/* ==================== Markdown 消息（文档块，不是聊天气泡） ==================== */
/* Markdown 按「文档」渲染：整行铺开的面板，收发方只靠左侧色条区分。
   气泡那套渐变底色 / 内边距 / 圆角 / 阴影在这里全部清掉，代码块另有深色面板。

   ⚠ 文档内容是 v-html 注入的，**不带 scoped 属性**，所以下面所有后代选择器
   必须包 :deep()。否则 Vue 会把 [data-v-x] 缀在最后一段选择器上
   （`.msg-markdown pre[data-v-x]`），而 v-html 里的 <pre>/<table>/<code>
   没有这个属性 —— 标题、表格、代码块的样式会整段静默失效。
   面板自身（.msg-markdown 这一层）是模板渲染的，不需要 :deep()。 */
.msg-row.is-markdown-msg {
  max-width: 100%;
  width: 100%;
}

.msg-row.is-markdown-msg .msg-body {
  /* 给 .msg-body 一个确定宽度，下面气泡的 100% 才有意义 */
  flex: 1;
  min-width: 0;
}

.msg-row .msg-bubble.is-markdown:not(.recalled) {
  width: 100%;
  max-width: 100%;
  padding: 0;
  background: transparent;
  color: var(--text);
  border-radius: 0;
  box-shadow: none;
}

/* 被 @ 的描边落到文档面板上（气泡本身已经没有背景） */
.msg-row .msg-bubble.is-markdown.mentioned .msg-markdown {
  border-color: var(--primary-light);
  box-shadow: 0 0 0 1.5px var(--primary-light);
}

/* 回复/撤回按钮：文档行占满整行，按钮改为浮在面板右上角 */
.msg-row.is-markdown-msg .msg-body .msg-actions {
  top: 10px;
  right: 10px;
  left: auto;
  transform: none;
}


/* 文本消息的 @提及 / 搜索关键词高亮同样由 v-html 注入（renderContent 拼的 span），
   原先写成裸 .mention / .search-mark 时带了 scope 属性、永远匹配不上 —— 顺手修掉。 */
.msg-bubble :deep(.mention) {
  color: var(--primary);
  font-weight: 500;
}

.msg-bubble :deep(.search-mark) {
  background: var(--search-mark-bg, #ffd54d);
  color: #333;
  border-radius: 3px;
  padding: 0 2px;
}

html[data-theme='dark'] .msg-bubble :deep(.search-mark) {
  background: #b8860b;
  color: #fff;
}

/* 被 @ 的消息主色描边 */
/* 多选模式：语音按钮不接收点击，整行点击用于勾选 */
.msg-bubble.is-voice .voice-static {
  pointer-events: none;
}

.msg-bubble.mentioned {
  box-shadow: 0 0 0 1.5px var(--primary-light);
}
</style>