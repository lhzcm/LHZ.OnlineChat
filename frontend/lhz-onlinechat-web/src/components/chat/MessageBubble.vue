<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { useAuthStore } from '@/stores/auth'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import { useToast } from '@/composables/useToast'
import { copyText } from '@/utils/clipboard'
import { forwardItemPreview, parseForwardBundle, type ForwardedItem } from '@/utils/forward'
import { formatMsgTime } from '@/utils/format'
import { renderMarkdownMessage } from '@/utils/markdown'
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

/** 「聊天记录」卡片明细（messageType = 4）：解析失败返回 null，界面退化成占位卡片而不是崩 */
const forwardItems = computed(() => (
  props.msg.messageType === 4 ? parseForwardBundle(props.msg.content || '') : null
))

function onForwardClick() {
  if (props.selectMode) {
    emit('toggle-select')
    return
  }
  emit('open-forward', forwardItems.value || [])
}

/**
 * Markdown 消息（messageType = 3）：内容存的是源文本，渲染前一律消毒。
 * 高亮（@提及 / 搜索关键词）在消毒后的 DOM 上做，见 utils/markdown.ts。
 */
const renderedMarkdown = computed(() => renderMarkdownMessage(props.msg.content || '', props.highlight))

/** 转义 HTML（防 XSS） */
function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/**
 * 渲染消息内容：转义 HTML → 搜索关键词高亮（<mark>）→ @提及高亮。
 * 用占位符先提取 @提及 片段，避免关键词高亮的 split/join 拆坏 mention 标签。
 */
function renderContent(content: string): string {
  const esc = escapeHtml(content)

  // 1. 提取 @提及 片段（占位符替换，稍后还原）
  const mentions: string[] = []
  const withoutMention = esc.replace(/@([^\s@，。！？!?,]+)/g, (m) => {
    mentions.push(m)
    return `\u0000${mentions.length - 1}\u0000`
  })

  // 2. 搜索关键词高亮（split/join 而非正则，避免特殊字符问题）
  const kw = escapeHtml(props.highlight?.trim() || '')
  let html = kw
    ? withoutMention.split(kw).join(`<mark class="search-mark">${kw}</mark>`)
    : withoutMention

  // 3. 还原 @提及（若关键词与提及重叠，mark 已包住原文，还原后嵌套正确）
  html = html.replace(/\u0000(\d+)\u0000/g, (_, i) => `<span class="mention">${mentions[Number(i)]}</span>`)
  return html
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
const menu = ref<{ x: number; y: number } | null>(null)
let longPressTimer: number | null = null

/** 菜单大概宽高，用于把浮层限制在视口内 */
const MENU_W = 132
const MENU_H = 168

/** 打开时间：长按后浏览器会补发一次合成 click，别让它刚打开就被点关 */
let menuOpenedAt = 0

function openMenu(x: number, y: number) {
  menuOpenedAt = Date.now()
  menu.value = {
    x: Math.max(8, Math.min(x, window.innerWidth - MENU_W - 8)),
    y: Math.max(8, Math.min(y, window.innerHeight - MENU_H - 8))
  }
}

function onContextMenu(e: MouseEvent) {
  // 移动端长按浏览器也会派发 contextmenu，这里统一处理（长按另有 touch 兜底）
  e.preventDefault()
  openMenu(e.clientX, e.clientY)
}

function onTouchStart(e: TouchEvent) {
  const t = e.touches[0]
  if (!t) return
  cancelLongPress()
  longPressTimer = window.setTimeout(() => openMenu(t.clientX, t.clientY), 500)
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
  toast(ok ? '已复制' : '复制失败，请手动选中文本复制')
  menu.value = null
}

/** 关闭菜单：点别处、Esc、滚动都关（否则浮层会留在原地挡住消息） */
function closeMenu() {
  // 长按（touch）后浏览器会补发一次合成 click：刚打开 300ms 内忽略，否则菜单一闪就没了
  if (Date.now() - menuOpenedAt < 300) return
  menu.value = null
}

function onDocKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') closeMenu()
}

onMounted(() => {
  document.addEventListener('click', closeMenu)
  document.addEventListener('keydown', onDocKeydown)
})
onUnmounted(() => {
  document.removeEventListener('click', closeMenu)
  document.removeEventListener('keydown', onDocKeydown)
  cancelLongPress()
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
    <div class="ctx-menu" v-if="menu" :style="{ left: menu.x + 'px', top: menu.y + 'px' }" @click.stop>
      <button class="ctx-item" @click="copyMessage(msg)">复制</button>
      <button class="ctx-item" v-if="canReply(msg)" @click="startReply(msg); closeMenu()">引用</button>
      <button class="ctx-item" v-if="canRecall(msg)" @click="recallMessage(msg); closeMenu()">撤回</button>
      <button class="ctx-item" v-if="mine && msg.isDeleted && msg.content"
        @click="emit('reedit', msg.content); closeMenu()">重新编辑</button>
      <button class="ctx-item" v-if="!msg.isDeleted" @click="emit('select'); closeMenu()">多选</button>
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
      <div class="msg-bubble" :class="{ mentioned: isMentioned(msg), 'is-image': msg.messageType === 1, 'is-markdown': msg.messageType === 3, 'is-forward': msg.messageType === 4, recalled: msg.isDeleted }">
        <span v-if="msg.isDeleted" class="msg-recalled-text">消息已撤回</span>
        <img v-else-if="msg.messageType === 1" :src="msg.content" class="msg-image" alt="图片"
          loading="lazy" @click.stop="selectMode ? emit('toggle-select') : emit('image-click', msg.content)" />
        <div v-else-if="msg.messageType === 3" class="msg-markdown" v-html="renderedMarkdown"></div>
        <!-- 「聊天记录」合并转发卡片：独立样式，点击展开只读明细 -->
        <div v-else-if="msg.messageType === 4" class="msg-forward" @click.stop="onForwardClick">
          <div class="fwd-head">
            <span class="fwd-ico">🗂</span>
            <span class="fwd-title">聊天记录</span>
            <span class="fwd-count" v-if="forwardItems">{{ forwardItems.length }} 条</span>
          </div>
          <div class="fwd-list" v-if="forwardItems">
            <div class="fwd-line" v-for="(it, i) in forwardItems.slice(0, 3)" :key="i">
              <span class="fwd-name">{{ it.n || '对方' }}：</span>
              <span class="fwd-text">{{ forwardItemPreview(it) }}</span>
            </div>
            <div class="fwd-more" v-if="forwardItems.length > 3">…共 {{ forwardItems.length }} 条</div>
          </div>
          <div class="fwd-broken" v-else>记录无法解析</div>
          <div class="fwd-foot">点击查看 ›</div>
        </div>
        <span v-else v-html="renderContent(msg.content)"></span>
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

.msg-forward {
  border: 1px solid var(--border, #e5e7eb);
  border-left: 3px solid var(--primary, #3b82f6);
  border-radius: 10px;
  background: var(--bg-panel, #fff);
  padding: 10px 12px;
  cursor: pointer;
  transition: background 0.15s;
}

.msg-forward:hover {
  background: var(--bg-hover);
}

.fwd-head {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: 6px;
}

.fwd-count {
  margin-left: auto;
  font-size: 11px;
  font-weight: normal;
  color: var(--text-secondary);
}

.fwd-list {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.fwd-line {
  display: flex;
  gap: 2px;
  font-size: 12.5px;
  color: var(--text-secondary);
  overflow: hidden;
  white-space: nowrap;
}

.fwd-name {
  color: var(--text-primary);
  flex-shrink: 0;
}

.fwd-text {
  overflow: hidden;
  text-overflow: ellipsis;
}

.fwd-more,
.fwd-broken {
  font-size: 12px;
  color: var(--text-secondary);
}

.fwd-foot {
  margin-top: 8px;
  font-size: 11px;
  color: var(--primary);
  text-align: right;
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

.msg-image {
  max-width: 260px;
  max-height: 260px;
  border-radius: 10px;
  display: block;
  cursor: pointer;
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

.msg-markdown {
  width: 100%;
  padding: 14px 18px;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-left: 3px solid var(--border);
  border-radius: 10px;
  font-size: 14px;
  line-height: 1.7;
  word-break: break-word;
}

/* 自己发出的文档：左侧主色条（不给整块染渐变，读起来正式些） */
.msg-row.mine .msg-markdown {
  border-left-color: var(--primary);
}

.msg-markdown :deep(> :first-child) { margin-top: 0; }
.msg-markdown :deep(> :last-child) { margin-bottom: 0; }

.msg-markdown :deep(p) {
  margin: 0 0 9px;
}

.msg-markdown :deep(strong) {
  font-weight: 600;
}

.msg-markdown :deep(del) {
  color: var(--text-secondary);
}

.msg-markdown :deep(h1),
.msg-markdown :deep(h2),
.msg-markdown :deep(h3),
.msg-markdown :deep(h4),
.msg-markdown :deep(h5),
.msg-markdown :deep(h6) {
  margin: 16px 0 8px;
  font-weight: 600;
  line-height: 1.35;
}

/* 文档式层级：h1/h2 带分隔线，而不是把字号粗暴放大 */
.msg-markdown :deep(h1) {
  font-size: 20px;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--border);
}

.msg-markdown :deep(h2) {
  font-size: 17.5px;
  padding-bottom: 4px;
  border-bottom: 1px solid var(--border);
}

.msg-markdown :deep(h3) {
  font-size: 15.5px;
}

.msg-markdown :deep(h4),
.msg-markdown :deep(h5),
.msg-markdown :deep(h6) {
  font-size: 14.5px;
  color: var(--text-secondary);
}

.msg-markdown :deep(ul),
.msg-markdown :deep(ol) {
  margin: 6px 0 10px;
  padding-left: 22px;
}

.msg-markdown :deep(li) {
  margin: 3px 0;
}

.msg-markdown :deep(li)::marker {
  color: var(--text-secondary);
}

.msg-markdown :deep(blockquote) {
  margin: 9px 0;
  padding: 7px 12px;
  border-left: 3px solid var(--primary-light);
  background: var(--bg-hover);
  border-radius: 0 6px 6px 0;
  color: var(--text-secondary);
}

/* 行内代码：浅色小色块，与代码块区分开 */
.msg-markdown :deep(code) {
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, 'Courier New', monospace;
  font-size: 12.5px;
  padding: 1.5px 5px;
  border-radius: 5px;
  background: var(--code-inline-bg);
  color: var(--code-inline-fg);
}

/* 代码块：深色代码面板 + 等宽 + 横向滚动（长行不换行、也不撑破面板） */
.msg-markdown :deep(pre) {
  margin: 10px 0;
  padding: 12px 14px;
  background: var(--code-bg);
  border-radius: 10px;
  overflow-x: auto;
  font-size: 12.5px;
  line-height: 1.65;
}

.msg-markdown :deep(pre code) {
  display: block;
  padding: 0;
  background: transparent;
  color: var(--code-fg);
  font-size: inherit;
  white-space: pre;
  tab-size: 4;
}

/* ---------- 语法高亮（highlight.js 的 .hljs-* 类） ---------- */
/* 代码面板在浅色/深色两种主题下都是深色，所以 token 配色不跟随主题 */
.msg-markdown :deep(.hljs-comment),
.msg-markdown :deep(.hljs-quote) {
  color: #8b93a3;
  font-style: italic;
}

.msg-markdown :deep(.hljs-keyword),
.msg-markdown :deep(.hljs-selector-tag),
.msg-markdown :deep(.hljs-literal),
.msg-markdown :deep(.hljs-doctag) {
  color: #c792ea;
}

.msg-markdown :deep(.hljs-string),
.msg-markdown :deep(.hljs-regexp),
.msg-markdown :deep(.hljs-addition),
.msg-markdown :deep(.hljs-attribute) {
  color: #a5e075;
}

.msg-markdown :deep(.hljs-number),
.msg-markdown :deep(.hljs-symbol),
.msg-markdown :deep(.hljs-bullet),
.msg-markdown :deep(.hljs-variable),
.msg-markdown :deep(.hljs-template-variable) {
  color: #f78c6c;
}

.msg-markdown :deep(.hljs-title),
.msg-markdown :deep(.hljs-name),
.msg-markdown :deep(.hljs-section),
.msg-markdown :deep(.hljs-selector-id),
.msg-markdown :deep(.hljs-selector-class) {
  color: #82aaff;
}

.msg-markdown :deep(.hljs-built_in),
.msg-markdown :deep(.hljs-type),
.msg-markdown :deep(.hljs-attr),
.msg-markdown :deep(.hljs-params) {
  color: #ffcb6b;
}

.msg-markdown :deep(.hljs-meta),
.msg-markdown :deep(.hljs-link) {
  color: #7f9cd6;
}

.msg-markdown :deep(.hljs-deletion) {
  color: #ff8a80;
}

.msg-markdown :deep(.hljs-emphasis) {
  font-style: italic;
}

.msg-markdown :deep(.hljs-strong) {
  font-weight: 600;
}

/* 深色底上的滚动条别太扎眼 */
.msg-markdown :deep(pre::-webkit-scrollbar) {
  height: 8px;
}

.msg-markdown :deep(pre::-webkit-scrollbar-thumb) {
  background: rgba(255, 255, 255, 0.22);
  border-radius: 4px;
}

.msg-markdown :deep(pre::-webkit-scrollbar-track) {
  background: transparent;
}

.msg-markdown :deep(table) {
  border-collapse: collapse;
  margin: 10px 0;
  font-size: 13px;
}

.msg-markdown :deep(th),
.msg-markdown :deep(td) {
  border: 1px solid var(--border);
  padding: 5px 10px;
}

.msg-markdown :deep(th) {
  background: var(--bg-hover);
  font-weight: 600;
}

.msg-markdown :deep(a) {
  color: var(--primary);
  text-decoration: none;
  border-bottom: 1px solid var(--primary-light);
}

.msg-markdown :deep(a:hover) {
  border-bottom-color: var(--primary);
}

.msg-markdown :deep(hr) {
  border: none;
  border-top: 1px solid var(--border);
  margin: 14px 0;
}

/* 高亮节点（@提及 / 搜索关键词）同样是脚本注入的，必须 :deep() */
.msg-markdown :deep(.mention) {
  color: var(--primary);
  font-weight: 600;
}

.msg-markdown :deep(.search-mark) {
  background: var(--search-mark-bg, #ffd54d);
  color: #333;
  border-radius: 3px;
  padding: 0 2px;
}

html[data-theme='dark'] .msg-markdown :deep(.search-mark) {
  background: #b8860b;
  color: #fff;
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
.msg-bubble.mentioned {
  box-shadow: 0 0 0 1.5px var(--primary-light);
}
</style>
