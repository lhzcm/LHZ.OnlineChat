/**
 * 入站消息的未读策略（红点 / 提示音 / 桌面通知 / 标已读 的共同入口）。
 *
 * 为什么单独抽成一个纯函数：这段判断原来散在 `ChatLayout.handleWsMessage` 的 `if/else` 里，
 * 既要看会话 key、又要看发送者、还要看免打扰 —— 一改就容易漏掉某个分支，
 * 而且写在 600 行的组件里没法单测。踩过的坑就是下面第二条。
 */
export type IncomingAction = 'read' | 'notify' | 'ignore'

export interface IncomingContext {
  /** 这条消息所属会话是不是当前打开的会话 */
  isCurrentSession: boolean
  /** 是不是自己发出的（多端同步回显） */
  isMine: boolean
  /** 本地此前没有这条消息（按 messageId 去重后） */
  isNew: boolean
  /** 该会话是否免打扰 */
  muted: boolean
  /** 用户此刻是否真的在看页面（可见 **且** 窗口有焦点） */
  isUserLooking: boolean
}

/**
 * 判定一条入站消息该怎么处理：
 * - `read`：属于当前会话且用户确实在看 → 清未读、落库已读、回已读回执；
 * - `notify`：用户没在看 → 加未读（红点）+ 提示音 + 桌面通知；
 * - `ignore`：重复消息、自己的回显、免打扰会话。
 *
 * **关键点：打开了会话 ≠ 看到了消息。**
 * 窗口切到后台、最小化、被别的窗口挡住时，`isUserLooking` 为 false ——
 * 此时若还按"已读"处理（历史行为），用户事后既看不到红点，发送方那边还会显示"已读"，
 * 等于把消息静默吞掉了。
 */
export function decideIncomingAction(ctx: IncomingContext): IncomingAction {
  // 同一 messageId 重复到达：历史拉取 / 离线补发 / 服务端回显都会造成重复
  if (!ctx.isNew) return 'ignore'

  // 自己发出的消息（多端同步）：不该提示，也不该由"收到自己的回显"去标记已读
  if (ctx.isMine) return 'ignore'

  // 会话开着且窗口真的在前台：这才是"看过了"
  if (ctx.isCurrentSession && ctx.isUserLooking) return 'read'

  // 免打扰：不打扰，但也别替用户清掉未读
  if (ctx.muted) return 'ignore'

  return 'notify'
}
