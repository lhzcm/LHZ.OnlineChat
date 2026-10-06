using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Abstractions;

/// <summary>
/// 实时推送端口。
/// 方法按「业务意图」而非「协议报文」命名 —— 应用层不知道 WsMessage 长什么样、
/// 也不知道一个用户有几台设备在线；协议封包与多端广播都在基础设施实现里。
/// 这替换了原先被 FriendService / GroupService / BlacklistService 直接依赖的 WsMessageHandler。
/// </summary>
public interface IRealtimeNotifier
{
    // ===== 好友 =====

    /// <summary>收到新的好友申请</summary>
    Task NotifyFriendRequestAsync(int toUserId, int fromUserId, CancellationToken ct = default);

    /// <summary>好友申请被接受（双向通知，双方刷新好友列表）</summary>
    Task NotifyFriendAcceptedAsync(int requesterId, int accepterId, CancellationToken ct = default);

    /// <summary>好友申请被拒绝</summary>
    Task NotifyFriendRejectedAsync(int requesterId, int rejecterId, CancellationToken ct = default);

    /// <summary>好友关系被删除（双向通知，双方刷新好友列表）</summary>
    Task NotifyFriendRemovedAsync(int removerId, int removedId, CancellationToken ct = default);

    /// <summary>上线/下线状态广播给在线好友</summary>
    Task NotifyPresenceAsync(int userId, IReadOnlyList<int> friendIds, bool online, CancellationToken ct = default);

    /// <summary>被对方拉黑</summary>
    Task NotifyBlockedAsync(int toUserId, int fromUserId, string reason, CancellationToken ct = default);

    // ===== 群组 =====

    /// <summary>被邀请加入群组</summary>
    Task NotifyGroupInvitedAsync(int toUserId, long groupId, CancellationToken ct = default);

    /// <summary>群被解散（客户端自动退出该会话）</summary>
    Task NotifyGroupDissolvedAsync(IReadOnlyList<int> memberIds, long groupId, string groupName,
        CancellationToken ct = default);

    /// <summary>被移出群（客户端自动退出该会话），与解散同样是「让客户端主动退出」</summary>
    Task NotifyRemovedFromGroupAsync(int toUserId, long groupId, CancellationToken ct = default);

    // ===== 消息 =====

    /// <summary>推送一条私聊消息给收发双方的全部在线设备（多端同步）</summary>
    Task PushPrivateMessageAsync(RealtimeMessage message, CancellationToken ct = default);

    /// <summary>推送一条群消息给全部在线成员的全部设备</summary>
    Task PushGroupMessageAsync(RealtimeMessage message, IReadOnlyList<int> memberIds,
        CancellationToken ct = default);

    /// <summary>补发离线群消息给指定用户（仅推该用户）</summary>
    Task PushGroupBacklogAsync(int toUserId, IReadOnlyList<RealtimeMessage> messages,
        CancellationToken ct = default);

    /// <summary>广播撤回通知</summary>
    Task NotifyMessageRecalledAsync(MessageRecalled recalled, IReadOnlyList<int> audienceUserIds,
        CancellationToken ct = default);

    /// <summary>转发「正在输入」</summary>
    Task ForwardTypingAsync(int fromUserId, int toUserId, CancellationToken ct = default);

    /// <summary>转发已读回执给被读方</summary>
    Task ForwardReadReceiptAsync(int readerId, int toUserId, string? messageId, CancellationToken ct = default);

    /// <summary>发言被拒（禁言中），只回发起者当前连接</summary>
    Task NotifyMutedAsync(int userId, long groupId, string reason, string? messageId,
        CancellationToken ct = default);

    /// <summary>私聊被拒（已被对方拉黑），只回发送者</summary>
    Task NotifyMessageBlockedAsync(int senderId, int receiverId, string reason, string? messageId,
        CancellationToken ct = default);
}

/// <summary>
/// 推送给客户端的一条消息（应用层视角的中性模型，不含协议细节）。
/// </summary>
public sealed class RealtimeMessage
{
    public required ChatSessionType SessionType { get; init; }

    /// <summary>发送者账号 ID</summary>
    public required int SenderId { get; init; }

    /// <summary>私聊为接收者账号 ID，群聊为群 ID</summary>
    public required long TargetId { get; init; }

    public required string Content { get; init; }

    public required MessageKind Kind { get; init; }

    /// <summary>对外消息标识（客户端 ID 或数据库 ID）</summary>
    public required string MessageId { get; init; }

    public required string SenderName { get; init; }

    public string? SenderAvatar { get; init; }

    /// <summary>发送时刻（UTC）</summary>
    public required DateTime SentAt { get; init; }

    public MentionList Mentions { get; init; } = MentionList.Empty;

    public MessageReply? Reply { get; init; }
}

/// <summary>
/// 在线状态与连接登记端口（WS 连接表的只读视图 + 踢下线能力）。
/// </summary>
public interface IConnectionRegistry
{
    /// <summary>当前 WS 连接总数（仪表盘）</summary>
    int ConnectionCount { get; }

    /// <summary>当前在线用户数（去重）</summary>
    int OnlineUserCount { get; }

    /// <summary>该用户是否有任一连接在线</summary>
    bool IsOnline(int userId);

    /// <summary>批量查在线状态（好友列表/成员列表，避免 N 次往返）</summary>
    IReadOnlyDictionary<int, bool> GetOnlineStates(IEnumerable<int> userIds);

    /// <summary>踢掉指定会话的连接（先推 kicked 通知再断开）</summary>
    void CloseSession(string sessionId);
}
