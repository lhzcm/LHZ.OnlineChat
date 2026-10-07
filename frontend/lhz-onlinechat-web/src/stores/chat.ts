import { defineStore } from 'pinia'
import { ref, computed, watch, reactive } from 'vue'
import type { SessionInfo, ChatType, WsMessage } from '@/types'
import { messageApi } from '@/api/message'

/** 乐观发送状态：sending=已上屏待服务端确认，sent=服务端已回显，failed=未写入连接（可重试） */
export type MessageStatus = 'sending' | 'sent' | 'failed'

/** 聊天消息：WS 协议消息 + 本地发送状态（status 只用于本地渲染，不上行） */
export type ChatMessage = WsMessage & { status?: MessageStatus }

// 常驻标签页 / 大量会话场景下的内存上限，避免下列集合无界增长
const MAX_SESSIONS = 200         // 会话列表最多保留条数（按最近活跃时间取前 N）
const MAX_MESSAGE_SESSIONS = 30  // 本地消息缓存保留的会话数（超出按 LRU 淘汰）
const MAX_HISTORY_META = 60      // 历史分页元数据条目上限
const MAX_READ_BY_PEER = 5000    // 对方已读回执 ID 上限（超出淘汰最早的记录）

export const useChatStore = defineStore('chat', () => {
  const sessions = ref<SessionInfo[]>([])
  const messages = ref<Map<string, ChatMessage[]>>(new Map())
  // 未读数，key 与 messages 一致：`private_${id}` / `group_${id}`
  const unreadCounts = ref<Map<string, number>>(new Map())
  // 对方已读回执的私聊消息 ID 集合（自己发出的消息被对方已读）
  const readByPeer = ref<Set<string>>(new Set())
  const currentSession = ref<{ type: ChatType; id: number; name: string } | null>(null)
  // 历史分页元数据：会话 key → { page, hasMore, loading }
  const historyMeta = ref<Map<string, { page: number; hasMore: boolean; loading: boolean }>>(new Map())

  const currentMessages = computed(() => {
    if (!currentSession.value) return []
    const key = `${currentSession.value.type}_${currentSession.value.id}`
    return messages.value.get(key) || []
  })

  function sessionKey(type: ChatType, id: number) {
    return `${type}_${id}`
  }

  /** 已读标记写入队列：会话 key → 该会话最后一次已读请求的 Promise */
  const readQueues = new Map<string, Promise<void>>()

  /** 当前会话 key（缓存淘汰时永不淘汰它） */
  function currentKey(): string {
    return currentSession.value ? sessionKey(currentSession.value.type, currentSession.value.id) : ''
  }

  /** 标记会话为最近使用（Map 迭代顺序即插入顺序，删后重插即移到末尾） */
  function touchSession(key: string) {
    const list = messages.value.get(key)
    if (!list) return
    messages.value.delete(key)
    messages.value.set(key, list)
  }

  /** 按 LRU 淘汰超出上限的会话消息缓存与历史分页元数据 */
  function evictStaleEntries() {
    const keep = currentKey()
    if (messages.value.size > MAX_MESSAGE_SESSIONS) {
      // 快照 key 再删除：迭代中删除虽然安全，但快照更直观
      for (const key of [...messages.value.keys()]) {
        if (messages.value.size <= MAX_MESSAGE_SESSIONS) break
        if (key === keep) continue
        messages.value.delete(key)
      }
    }
    if (historyMeta.value.size > MAX_HISTORY_META) {
      for (const key of [...historyMeta.value.keys()]) {
        if (historyMeta.value.size <= MAX_HISTORY_META) break
        if (key === keep) continue
        historyMeta.value.delete(key)
      }
    }
  }

  /** 已读回执按加入顺序淘汰最早的记录（只影响很久以前的消息是否显示"已读"） */
  function trimReadByPeer() {
    if (readByPeer.value.size <= MAX_READ_BY_PEER) return
    const overflow = readByPeer.value.size - MAX_READ_BY_PEER
    let i = 0
    for (const id of readByPeer.value) {
      if (i++ >= overflow) break
      readByPeer.value.delete(id)
    }
  }

  /** 会话列表上限：超出时保留最近活跃的 N 个，并维持服务端返回的展示顺序 */
  function boundSessions(list: SessionInfo[]): SessionInfo[] {
    if (list.length <= MAX_SESSIONS) return list
    const keep = new Set(
      [...list]
        .sort((a, b) => (new Date(b.lastTime).getTime() || 0) - (new Date(a.lastTime).getTime() || 0))
        .slice(0, MAX_SESSIONS)
        .map(s => sessionKey(s.type, s.id))
    )
    return list.filter(s => keep.has(sessionKey(s.type, s.id)))
  }

  // 标签页标题未读角标：(N) OnlineChat
  watch(unreadCounts, (map) => {
    let total = 0
    map.forEach(v => { total += v })
    document.title = total > 0 ? `(${total}) OnlineChat` : 'OnlineChat'
  }, { immediate: true })

  function mergeList(list: ChatMessage[], incoming: ChatMessage[]) {
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
  function addMessage(msg: ChatMessage, myUserId?: number): { key: string; isNew: boolean } {
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
    const dup = msg.messageId ? list.find(m => m.messageId === msg.messageId) : undefined
    const isNew = !dup
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
    } else if (dup && dup.status !== 'sent') {
      // 服务端回显（同 messageId）：把乐观发送中的消息确认为已发送
      dup.status = 'sent'
    }
    touchSession(key)
    evictStaleEntries()
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
    trimReadByPeer()
  }

  /** 标记消息为已撤回（本地即时生效） */
  function markMessageRecalled(key: string, messageId: string) {
    const list = messages.value.get(key)
    if (!list) return
    const msg = list.find(m => m.messageId === messageId)
    if (msg) msg.isDeleted = true
  }

  /** 从会话中移除一条消息（如被对方拉黑导致发送失败） */
  function removeMessage(key: string, messageId: string | undefined) {
    if (!messageId) return
    const list = messages.value.get(key)
    if (!list) return
    messages.value.set(key, list.filter(m => m.messageId !== messageId))
  }

  /** 该消息是否已被对方已读 */
  function isReadByPeer(msgId: string | undefined): boolean {
    return !!msgId && readByPeer.value.has(msgId)
  }

  /** 更新某条消息的本地发送状态（乐观发送 → 已发送 / 发送失败） */
  function setMessageStatus(key: string, messageId: string, status: MessageStatus) {
    if (!messageId) return
    const msg = messages.value.get(key)?.find(m => m.messageId === messageId)
    if (msg) msg.status = status
  }

  /**
   * 打开会话：清空未读；私聊同步服务端已读标记，群聊推进已读游标。
   * 按会话串行执行：新消息会连续触发标记，串行可避免多个 mark-all-read 请求乱序到达
   * （后发先至会让服务端把已读游标回退，未读数再次出现）。
   */
  async function markSessionRead(type: ChatType, id: number) {
    const key = sessionKey(type, id)
    unreadCounts.value.set(key, 0)
    const prev = readQueues.get(key) || Promise.resolve()
    const task = prev.catch(() => {}).then(async () => {
      try {
        if (type === 'private') {
          await messageApi.markAllAsRead(id)
        } else {
          await messageApi.markGroupRead(id)
        }
      } catch { /* 忽略失败，下次打开再试 */ }
    }).finally(() => {
      // 队尾任务完成后回收，避免队列 Map 随会话数无限增长
      if (readQueues.get(key) === task) readQueues.delete(key)
    })
    readQueues.set(key, task)
    return task
  }

  /**
   * 拉取会话列表（服务端聚合），并同步未读数
   */
  async function fetchSessions() {
    const res = await messageApi.getSessions()
    if (res.success && res.data) {
      // 会话列表本身也可能很长（大量群/机器人）：超出上限时只保留最近活跃的部分
      sessions.value = boundSessions(res.data)
      for (const s of sessions.value) {
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

  /** 获取（或初始化）会话的历史分页元数据 */
  function historyMetaOf(key: string): { page: number; hasMore: boolean; loading: boolean } {
    let meta = historyMeta.value.get(key)
    if (!meta) {
      // 必须用 reactive 包裹，否则后续属性变更不会被追踪（loading/hasMore 状态会卡死）
      meta = reactive({ page: 0, hasMore: false, loading: false })
      historyMeta.value.set(key, meta)
    }
    return meta
  }

  /**
   * 加载历史消息（分页），并记录 hasMore 状态。
   * 返回该页之后是否还有更早的消息。
   */
  async function loadHistory(type: ChatType, id: number, page = 1, myUserId?: number): Promise<boolean> {
    const key = sessionKey(type, id)
    const meta = historyMetaOf(key)
    // 入口即做在途判断（loadMoreHistory 也走这里）：
    // 否则并发调用会重复拉取同一页并在 mergeList 里交错写入
    if (meta.loading) return meta.hasMore
    meta.loading = true
    let hasMore = false
    try {
      if (type === 'private') {
        const res = await messageApi.getPrivateHistory(id, page)
        if (res.success && res.data) {
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
          const existing = messages.value.get(key) || []
          messages.value.set(key, mergeList(existing, newMsgs))
          touchSession(key)
          // 历史中"我发出且已被对方已读"的消息标记已读状态
          if (myUserId !== undefined) {
            for (const m of res.data.items) {
              if (m.senderId === myUserId && m.isRead) {
                readByPeer.value.add(m.messageId || String(m.id))
              }
            }
            trimReadByPeer()
          }
          hasMore = res.data.page * res.data.pageSize < res.data.total
        }
      } else {
        const res = await messageApi.getGroupHistory(id, page)
        if (res.success && res.data) {
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
          const existing = messages.value.get(key) || []
          messages.value.set(key, mergeList(existing, newMsgs))
          touchSession(key)
          hasMore = res.data.page * res.data.pageSize < res.data.total
        }
      }
    } finally {
      meta.page = page
      meta.hasMore = hasMore
      meta.loading = false
      evictStaleEntries()
    }
    return hasMore
  }

  /**
   * 加载更早的历史（上一页）。已在加载或没有更多时直接返回 false。
   */
  async function loadMoreHistory(type: ChatType, id: number, myUserId?: number): Promise<boolean> {
    const meta = historyMetaOf(sessionKey(type, id))
    if (meta.loading || !meta.hasMore) return false
    return loadHistory(type, id, meta.page + 1, myUserId)
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
    evictStaleEntries()
    return counts
  }

  /** 清空全部本地聊天缓存（登出 / 切换账号时调用，避免内存残留与串号） */
  function clearAll() {
    sessions.value = []
    messages.value = new Map()
    unreadCounts.value = new Map()
    readByPeer.value = new Set()
    historyMeta.value = new Map()
    readQueues.clear()
    currentSession.value = null
  }

  return {
    sessions, messages, unreadCounts, readByPeer, currentSession, currentMessages, historyMeta,
    sessionKey, addMessage, bumpUnread, setUnreadCount, markSessionReadByPeer, isReadByPeer, markMessageRecalled, removeMessage,
    setMessageStatus, markSessionRead, fetchSessions, updateSessionSetting, isSessionMuted,
    setCurrentSession, loadHistory, loadMoreHistory, loadOfflineMessages, clearAll
  }
})
