using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Messaging;

/// <summary>私聊消息仓储</summary>
public interface IPrivateMessageRepository
{
    Task<PrivateMessage?> FindByIdAsync(long id, CancellationToken ct = default);

    /// <summary>两人之间的历史消息（分页，时间倒序取页后由调用方正序展示）</summary>
    Task<(IReadOnlyList<PrivateMessage> Items, int Total)> PageBetweenAsync(
        int userId, int peerId, PageRequest page, CancellationToken ct = default);

    /// <summary>会话内搜索（内容模糊匹配，排除已撤回）</summary>
    Task<(IReadOnlyList<PrivateMessage> Items, int Total)> SearchBetweenAsync(
        int userId, int peerId, string keyword, PageRequest page, CancellationToken ct = default);

    /// <summary>全局搜索该用户参与的私聊（取前 N 条供跨表合并）</summary>
    Task<(IReadOnlyList<PrivateMessage> Items, int Total)> SearchOfUserAsync(
        int userId, string keyword, int take, CancellationToken ct = default);

    /// <summary>管理后台检索（可按用户过滤；取前 N 条供跨表合并）</summary>
    Task<(IReadOnlyList<PrivateMessage> Items, int Total)> SearchForAdminAsync(
        string? keyword, int? userId, int take, CancellationToken ct = default);

    /// <summary>
    /// 未读消息（上线补发离线消息），按时间倒序取最近 limit 条。
    /// 必须带上限：长期未登录的账号可能有上万条未读，全量返回会把响应体撑爆
    /// （群消息补发一直有 100 条/群的上限，私聊此前没有）。
    /// 取最近而非最早 —— 补发时最重要的是最新的消息。
    /// </summary>
    Task<IReadOnlyList<PrivateMessage>> ListUnreadForAsync(
        int userId, int limit, CancellationToken ct = default);

    /// <summary>未读总数</summary>
    Task<int> CountUnreadForAsync(int userId, CancellationToken ct = default);

    /// <summary>按发送者分组的未读数（会话列表角标）</summary>
    Task<IReadOnlyDictionary<int, int>> CountUnreadBySenderAsync(int userId, CancellationToken ct = default);

    /// <summary>该用户最近参与的私聊消息（会话列表聚合，取最近 N 条）</summary>
    Task<IReadOnlyList<PrivateMessage>> ListRecentOfUserAsync(int userId, int take, CancellationToken ct = default);

    /// <summary>找可撤回的消息：本人发出、未撤回、在时间窗内、messageId 匹配</summary>
    Task<PrivateMessage?> FindRecallableAsync(
        int senderId, int receiverId, string messageId, DateTime earliestSentAt, CancellationToken ct = default);

    /// <summary>
    /// 按客户端消息 ID 查已存在的消息（幂等发送）。
    /// ClientMessageId 是乐观发送的去重键，但重试时服务端若无这张查询表，
    /// 就会真的插入第二条 —— 前端按 messageId 去重只能掩盖表现层的重复。
    /// </summary>
    Task<PrivateMessage?> FindByClientMessageIdAsync(
        int senderId, string clientMessageId, CancellationToken ct = default);

    /// <summary>把某人发给我的全部未读标记为已读</summary>
    Task<int> MarkAllReadAsync(int senderId, int receiverId, CancellationToken ct = default);

    Task AddAsync(PrivateMessage message, CancellationToken ct = default);

    Task UpdateAsync(PrivateMessage message, CancellationToken ct = default);

    // ===== 统计（仪表盘） =====

    Task<long> CountAsync(CancellationToken ct = default);

    Task<long> CountSentBetweenAsync(DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default);

    Task<long> CountSentSinceAsync(DateTime since, CancellationToken ct = default);

    /// <summary>自某时刻起发过消息的去重发送者</summary>
    Task<IReadOnlyList<int>> ListDistinctSendersSinceAsync(DateTime since, CancellationToken ct = default);

    /// <summary>发送量 TOP N 的发送者</summary>
    Task<IReadOnlyDictionary<int, long>> TopSendersAsync(int take, CancellationToken ct = default);

    /// <summary>各用户的发送量（管理后台用户列表）</summary>
    Task<IReadOnlyDictionary<int, long>> CountBySenderAsync(IEnumerable<int> userIds, CancellationToken ct = default);

    /// <summary>近 N 小时按小时聚合的消息量（原生 SQL date_trunc）</summary>
    Task<IReadOnlyDictionary<DateTime, long>> CountByHourSinceAsync(int hours, CancellationToken ct = default);
}

/// <summary>群聊消息仓储</summary>
public interface IGroupMessageRepository
{
    Task<GroupMessage?> FindByIdAsync(long id, CancellationToken ct = default);

    /// <summary>群内最新消息 ID（0 表示还没有消息）；入群时用作已读游标初值</summary>
    Task<long> MaxIdOfGroupAsync(long groupId, CancellationToken ct = default);

    Task<(IReadOnlyList<GroupMessage> Items, int Total)> PageOfGroupAsync(
        long groupId, PageRequest page, CancellationToken ct = default);

    Task<(IReadOnlyList<GroupMessage> Items, int Total)> SearchInGroupAsync(
        long groupId, string keyword, PageRequest page, CancellationToken ct = default);

    /// <summary>在我加入的群里全局搜索（取前 N 条供跨表合并）</summary>
    Task<(IReadOnlyList<GroupMessage> Items, int Total)> SearchInGroupsAsync(
        IReadOnlyList<long> groupIds, string keyword, int take, CancellationToken ct = default);

    Task<(IReadOnlyList<GroupMessage> Items, int Total)> SearchForAdminAsync(
        string? keyword, int? senderId, long? groupId, int take, CancellationToken ct = default);

    /// <summary>游标之后的群消息（上线补发，每群上限 limit 条防游标异常刷屏）</summary>
    Task<IReadOnlyList<GroupMessage>> ListAfterCursorAsync(
        long groupId, long afterMessageId, int limit, CancellationToken ct = default);

    /// <summary>
    /// 该用户所有群的待补发消息 ID（每群上限 <paramref name="perGroupLimit"/> 条）。
    ///
    /// 存在的意义是「一次查询覆盖所有群」：上线补发原本按群循环查询，
    /// 20 个群就是 20 次数据库往返，而绝大多数连接一条补发都没有。
    /// 游标直接取自 GroupMember.LastReadMessageId，
    /// 不需要调用方把每个群的游标传进来（也就不需要拼动态 SQL）。
    /// </summary>
    Task<IReadOnlyList<long>> ListBacklogIdsAsync(
        int userId, int perGroupLimit, CancellationToken ct = default);

    /// <summary>按 ID 批量取消息（配合 <see cref="ListBacklogIdsAsync"/> 使用，顺序不保证）</summary>
    Task<IReadOnlyList<GroupMessage>> ListByIdsAsync(
        IReadOnlyList<long> ids, CancellationToken ct = default);

    /// <summary>游标之后的未读条数</summary>
    Task<int> CountAfterCursorAsync(long groupId, long afterMessageId, CancellationToken ct = default);

    /// <summary>这些群各自的最后一条消息（会话列表聚合）</summary>
    Task<IReadOnlyDictionary<long, GroupMessage>> LatestOfGroupsAsync(
        IReadOnlyList<long> groupIds, CancellationToken ct = default);

    Task<GroupMessage?> FindRecallableAsync(
        long groupId, int senderId, string messageId, DateTime earliestSentAt, CancellationToken ct = default);

    /// <summary>按客户端消息 ID 查已存在的群消息（幂等发送，理由同私聊）</summary>
    Task<GroupMessage?> FindByClientMessageIdAsync(
        int senderId, string clientMessageId, CancellationToken ct = default);

    Task AddAsync(GroupMessage message, CancellationToken ct = default);

    Task UpdateAsync(GroupMessage message, CancellationToken ct = default);

    Task DeleteAllOfGroupAsync(long groupId, CancellationToken ct = default);

    // ===== 统计（仪表盘） =====

    Task<long> CountAsync(CancellationToken ct = default);

    Task<long> CountSentBetweenAsync(DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default);

    Task<long> CountSentSinceAsync(DateTime since, CancellationToken ct = default);

    Task<IReadOnlyList<int>> ListDistinctSendersSinceAsync(DateTime since, CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, long>> TopSendersAsync(int take, CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, long>> CountBySenderAsync(IEnumerable<int> userIds, CancellationToken ct = default);

    /// <summary>消息量 TOP N 的群</summary>
    Task<IReadOnlyDictionary<long, long>> TopGroupsAsync(int take, CancellationToken ct = default);

    /// <summary>各群消息数（管理后台群列表）</summary>
    Task<IReadOnlyDictionary<long, long>> CountByGroupAsync(IEnumerable<long> groupIds, CancellationToken ct = default);

    Task<IReadOnlyDictionary<DateTime, long>> CountByHourSinceAsync(int hours, CancellationToken ct = default);
}

/// <summary>会话设置仓储</summary>
public interface ISessionSettingRepository
{
    Task<SessionSetting?> FindAsync(int userId, ChatSessionType type, long sessionId, CancellationToken ct = default);

    /// <summary>我的全部会话设置，键为 "类型_会话ID"</summary>
    Task<IReadOnlyDictionary<string, SessionSetting>> ListOfUserAsync(int userId, CancellationToken ct = default);

    Task AddAsync(SessionSetting setting, CancellationToken ct = default);

    Task UpdateAsync(SessionSetting setting, CancellationToken ct = default);

    /// <summary>群解散时清理该群的全部会话设置</summary>
    Task DeleteBySessionAsync(ChatSessionType type, long sessionId, CancellationToken ct = default);
}
