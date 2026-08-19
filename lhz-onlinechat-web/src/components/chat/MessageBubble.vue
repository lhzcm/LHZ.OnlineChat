<script setup lang="ts">
import Avatar from '@/components/Avatar.vue'
import { useAuthStore } from '@/stores/auth'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import { formatMsgTime } from '@/utils/format'
import type { WsMessage, ChatType } from '@/types'

const props = defineProps<{
  msg: WsMessage
  chatType: ChatType
  chatId: number
  chatName: string
}>()
const emit = defineEmits<{ reply: [msg: WsMessage]; 'image-click': [url: string] }>()

const auth = useAuthStore()
const chatStore = useChatStore()
const ws = useWebSocketStore()

const mine = Number(props.msg.from) === auth.user?.id

/** 渲染消息内容：转义 HTML 后高亮 @提及（防 XSS） */
function renderContent(content: string): string {
  const esc = content
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
  return esc.replace(/@([^\s@，。！？!?,]+)/g, '<span class="mention">@$1</span>')
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
</script>

<template>
  <div class="msg-row" :class="{ mine }" :data-mid="msg.messageId">
    <Avatar class="msg-avatar" :name="msg.senderName || chatName" :url="msg.senderAvatar" size="sm" />
    <div class="msg-body">
      <span class="msg-sender" v-if="chatType === 'group' && !mine">
        {{ msg.senderName }}
      </span>
      <div class="reply-preview" v-if="msg.replyContent && !msg.isDeleted">
        <span class="reply-sender">{{ msg.replySender || '引用' }}</span>
        <span class="reply-text">{{ msg.replyContent }}</span>
      </div>
      <div class="msg-bubble" :class="{ mentioned: isMentioned(msg), 'is-image': msg.messageType === 1, recalled: msg.isDeleted }">
        <span v-if="msg.isDeleted" class="msg-recalled-text">消息已撤回</span>
        <img v-else-if="msg.messageType === 1" :src="msg.content" class="msg-image" alt="图片"
          loading="lazy" @click.stop="emit('image-click', msg.content)" />
        <span v-else v-html="renderContent(msg.content)"></span>
      </div>
      <div class="msg-actions">
        <button v-if="canReply(msg)" class="msg-recall-btn" @click.stop="startReply(msg)">回复</button>
        <button v-if="canRecall(msg)" class="msg-recall-btn" @click.stop="recallMessage(msg)">撤回</button>
      </div>
      <div class="msg-meta-line">
        <span class="msg-time">{{ formatMsgTime(msg.timestamp) }}</span>
        <span v-if="isMyPrivateMessage(msg)" class="msg-status"
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

.mention {
  color: var(--primary);
  font-weight: 500;
}

/* 被 @ 的消息主色描边 */
.msg-bubble.mentioned {
  box-shadow: 0 0 0 1.5px var(--primary-light);
}
</style>
