import { defineStore } from 'pinia'
import { ref, computed, watch } from 'vue'
import type { SessionInfo, ChatType, WsMessage, MessageDto } from '@/types'
import { messageApi } from '@/api/message'

export const useChatStore = defineStore('chat', () => {
  const sessions = ref<SessionInfo[]>([])
  const messages = ref<Map<string, WsMessage[]>>(new Map())
  // 未读数，key 与 messages 一致：`private_${id}` / `group_${id}`
  const unreadCounts = ref<Map<string, number>>(new Map())
  // 对方已读回执的私聊消息 ID 集合（自己发出的消息被对方已读）
  const readByPeer = ref<Set<string>>(new Set())
  const currentSession = ref<{ type: ChatType; id: number; name: string } | null>(null)

  const currentMessages = computed(() => {
    if (!currentSession.value) return []
    const key = `${currentSession.value.type}_${currentSession.value.id}`
    return messages.value.get(key) || []
  })

  function sessionKey(type: ChatType, id: number) {
    return `${type}_${id}`
  }

  // 标签页标题未读角标：(N) OnlineChat
  watch(unreadCounts, (map) => {
    let total = 0
    map.forEach(v => { total += v })
    document.title = total > 0 ? `(${total}) OnlineChat` : 'OnlineChat'
  }, { immediate: true })

  function mergeList(list: WsMessage[], incoming: WsMessage[]) {
    const seen = new Set(list.map(m => m.messageId).filter(Boolean))
    const merged = [...list]
    for (const m of incoming) {
      if (m.messageId && seen.has(m.messageId)) continue
      if (m.messageId) seen.add(m.messageId)
      merged.push(m)
    }
    return merged.sort((a, b) => a.timestamp - b.timestamp)
  }

  /**
   * 添加一条消息（按 messageId 去重）。
   * myUserId 用于私聊会话归属：发送者视角（自己的消息/回显）归到 to，接收者视角归到 from。
   * 返回 { key, isNew }，isNew=false 表示与已有消息重复（如服务端回显）。
   */
  function addMessage(msg: WsMessage, myUserId?: number): { key: string; isNew: boolean } {
    let sessionType: ChatType
    let sessionId: number
    if (msg.type === 'group_message') {
      sessionType = 'group'
      sessionId = Number(msg.to)
    } else {
      sessionType = 'private'
      sessionId = myUserId !== undefined && Number(msg.from) === myUserId
        ? Number(msg.to)
        : Number(msg.from)
    }
    const key = sessionKey(sessionType, sessionId)

    const list = messages.value.get(key) || []
    const isNew = !(msg.messageId && list.some(m => m.messageId === msg.messageId))
    if (isNew) {
      messages.value.set(key, mergeList(list, [msg]))
      // 同步会话列表的最后消息预览
      const idx = sessions.value.findIndex(s => s.type === sessionType && s.id === sessionId)
      if (idx >= 0) {
        sessions.value[idx] = {
          ...sessions.value[idx],
          lastMessage: msg.content,
          lastTime: new Date(msg.timestamp).toISOString()
        }
      }
    }
    return { key, isNew }
  }

  /** 未读数 +1 */
  function bumpUnread(key: string) {
    unreadCounts.value.set(key, (unreadCounts.value.get(key) || 0) + 1)
  }

  /** 设置未读数（取较大值，避免旧数据覆盖新计数） */
  function setUnreadCount(key: string, count: number) {
    const current = unreadCounts.value.get(key) || 0
    if (count > current) {
      unreadCounts.value.set(key, count)
    }
  }

  /** 收到对方已读回执：把该会话中我发出的消息标记为已读 */
  function markSessionReadByPeer(key: string) {
    const list = messages.value.get(key)
    if (!list) return
    for (const m of list) {
      if (m.messageId) readByPeer.value.add(m.messageId)
    }
  }

  /** 标记消息为已撤回（本地即时生效） */
  function markMessageRecalled(key: string, messageId: string) {
    const list = messages.value.get(key)
    if (!list) return
    const msg = list.find(m => m.messageId === messageId)
    if (msg) msg.isDeleted = true
  }

  /** 该消息是否已被对方已读 */
  function isReadByPeer(msgId: string | undefined): boolean {
    return !!msgId && readByPeer.value.has(msgId)
  }

  /**
   * 打开会话：清空未读；私聊同步服务端已读标记，群聊推进已读游标。
   */
  async function markSessionRead(type: ChatType, id: number) {
    unreadCounts.value.set(sessionKey(type, id), 0)
    try {
      if (type === 'private') {
        await messageApi.markAllAsRead(id)
      } else {
        await messageApi.markGroupRead(id)
      }
    } catch { /* 忽略失败，下次打开再试 */ }
  }

  /**
   * 拉取会话列表（服务端聚合），并同步未读数
   */
  async function fetchSessions() {
    const res = await messageApi.getSessions()
    if (res.success && res.data) {
      sessions.value = res.data
      for (const s of res.data) {
        if (s.unreadCount > 0) setUnreadCount(sessionKey(s.type, s.id), s.unreadCount)
      }
    }
  }

  /** 更新会话设置（置顶/免打扰），并同步本地列表 */
  async function updateSessionSetting(type: ChatType, id: number, patch: { isPinned?: boolean; muted?: boolean }) {
    const res = await messageApi.updateSessionSetting(type, id, patch)
    if (res.success) {
      const s = sessions.value.find(x => x.type === type && x.id === id)
      if (s) {
        if (patch.isPinned !== undefined) s.isPinned = patch.isPinned
        if (patch.muted !== undefined) s.muted = patch.muted
      }
    }
    return res
  }

  /** 会话是否已免打扰（静音） */
  function isSessionMuted(type: ChatType, id: number): boolean {
    return !!sessions.value.find(s => s.type === type && s.id === id)?.muted
  }

  function setCurrentSession(type: ChatType, id: number, name: string) {
    currentSession.value = { type, id, name }
  }

  async function loadHistory(type: ChatType, id: number, page = 1, myUserId?: number) {
    if (type === 'private') {
      const res = await messageApi.getPrivateHistory(id, page)
      if (res.success && res.data) {
        const key = `private_${id}`
        const existing = messages.value.get(key) || []
        const newMsgs = res.data.items.map(m => ({
          type: 'private_message',
          from: String(m.senderId),
          to: String(id),
          content: m.content,
          timestamp: new Date(m.sentAt).getTime(),
          messageId: m.messageId || String(m.id),
          messageType: m.messageType,
          senderName: m.senderName,
          senderAvatar: m.senderAvatar,
          isDeleted: m.isDeleted,
          replyTo: m.replyTo,
          replyContent: m.replyContent,
          replySender: m.replySender
        } as WsMessage))
        messages.value.set(key, mergeList(existing, newMsgs))
        // 历史中"我发出且已被对方已读"的消息标记已读状态
        if (myUserId !== undefined) {
          for (const m of res.data.items) {
            if (m.senderId === myUserId && m.isRead) {
              readByPeer.value.add(m.messageId || String(m.id))
            }
          }
        }
      }
    } else {
      const res = await messageApi.getGroupHistory(id, page)
      if (res.success && res.data) {
        const key = `group_${id}`
        const existing = messages.value.get(key) || []
        const newMsgs = res.data.items.map(m => ({
          type: 'group_message',
          from: String(m.senderId),
          to: String(m.groupId),
          content: m.content,
          timestamp: new Date(m.sentAt).getTime(),
          messageId: m.messageId || String(m.id),
          messageType: m.messageType,
          senderName: m.senderName,
          senderAvatar: m.senderAvatar,
          mentions: m.mentions || [],
          isDeleted: m.isDeleted,
          replyTo: m.replyTo,
          replyContent: m.replyContent,
          replySender: m.replySender
        } as WsMessage))
        messages.value.set(key, mergeList(existing, newMsgs))
      }
    }
  }

  /**
   * 导入离线消息（登录后拉取）。
   * 返回每个会话的未读数 Map（key → count），由调用方决定是否展示。
   */
  async function loadOfflineMessages(userId: number): Promise<Map<string, number>> {
    const counts = new Map<string, number>()
    const res = await messageApi.getOfflineMessages()
    if (!res.success || !res.data?.length) return counts

    const bySender = new Map<number, WsMessage[]>()
    for (const m of res.data) {
      const msg: WsMessage = {
        type: 'private_message',
        from: String(m.senderId),
        to: String(userId),
        content: m.content,
        timestamp: new Date(m.sentAt).getTime(),
        messageId: m.messageId || String(m.id),
        messageType: m.messageType,
        senderName: m.senderName,
        senderAvatar: m.senderAvatar
      }
      const list = bySender.get(m.senderId) || []
      list.push(msg)
      bySender.set(m.senderId, list)
    }

    for (const [senderId, list] of bySender) {
      const key = sessionKey('private', senderId)
      const existing = messages.value.get(key) || []
      messages.value.set(key, mergeList(existing, list))
      counts.set(key, list.length)
    }
    return counts
  }

  return {
    sessions, messages, unreadCounts, readByPeer, currentSession, currentMessages,
    sessionKey, addMessage, bumpUnread, setUnreadCount, markSessionReadByPeer, isReadByPeer, markMessageRecalled,
    markSessionRead, fetchSessions, updateSessionSetting, isSessionMuted,
    setCurrentSession, loadHistory, loadOfflineMessages
  }
})
