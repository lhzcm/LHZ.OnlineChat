using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Tests.TestDoubles;

/// <summary>
/// 实时推送记录器：把每次推送记成一条结构化记录，供断言「推给了谁、推了什么」。
/// 改造前这些推送是业务方法里直接调 WsMessageHandler 的副作用，没法单测；
/// 现在它是一个端口，测试可以完整观察。
/// </summary>
internal sealed class RecordingRealtimeNotifier : IRealtimeNotifier
{
    internal List<PushRecord> Pushes { get; } = new();

    internal IEnumerable<PushRecord> OfKind(PushKind kind) => Pushes.Where(p => p.Kind == kind);

    internal PushRecord? Single(PushKind kind) => Pushes.SingleOrDefault(p => p.Kind == kind);

    internal bool Any(PushKind kind) => Pushes.Exists(p => p.Kind == kind);

    // ===== 好友 =====

    public Task NotifyFriendRequestAsync(int toUserId, int fromUserId, CancellationToken ct = default)
        => Record(PushKind.FriendRequest, toUserId, contextId: fromUserId);

    public Task NotifyFriendAcceptedAsync(int requesterId, int accepterId, CancellationToken ct = default)
    {
        // 真实实现是双向通知，记两条才能验证「双方都收到」
        Record(PushKind.FriendAccepted, requesterId, contextId: accepterId);
        return Record(PushKind.FriendAccepted, accepterId, contextId: requesterId);
    }

    public Task NotifyFriendRejectedAsync(int requesterId, int rejecterId, CancellationToken ct = default)
        => Record(PushKind.FriendRejected, requesterId, contextId: rejecterId);

    public Task NotifyPresenceAsync(
        int userId, IReadOnlyList<int> friendIds, bool online, CancellationToken ct = default)
    {
        foreach (var friendId in friendIds)
        {
            Record(PushKind.Presence, friendId, contextId: userId,
                content: online ? "online" : "offline");
        }
        return Task.CompletedTask;
    }

    public Task NotifyBlockedAsync(
        int toUserId, int fromUserId, string reason, CancellationToken ct = default)
        => Record(PushKind.Blocked, toUserId, contextId: fromUserId, content: reason);

    // ===== 群组 =====

    public Task NotifyGroupInvitedAsync(int toUserId, long groupId, CancellationToken ct = default)
        => Record(PushKind.GroupInvited, toUserId, contextId: groupId);

    public Task NotifyGroupDissolvedAsync(
        IReadOnlyList<int> memberIds, long groupId, string groupName, CancellationToken ct = default)
    {
        foreach (var memberId in memberIds)
        {
            Record(PushKind.GroupDissolved, memberId, contextId: groupId, content: groupName);
        }
        return Task.CompletedTask;
    }

    // ===== 消息 =====

    public Task PushPrivateMessageAsync(RealtimeMessage message, CancellationToken ct = default)
    {
        // 真实实现同时推给接收方与发送方全部设备（多端同步）
        Record(PushKind.PrivateMessage, (int)message.TargetId,
            contextId: message.SenderId, content: message.Content, message: message);
        return Record(PushKind.PrivateMessageEcho, message.SenderId,
            contextId: message.TargetId, content: message.Content, message: message);
    }

    public Task PushGroupMessageAsync(
        RealtimeMessage message, IReadOnlyList<int> memberIds, CancellationToken ct = default)
    {
        foreach (var memberId in memberIds)
        {
            Record(PushKind.GroupMessage, memberId,
                contextId: message.TargetId, content: message.Content, message: message);
        }
        return Task.CompletedTask;
    }

    public Task PushGroupBacklogAsync(
        int toUserId, IReadOnlyList<RealtimeMessage> messages, CancellationToken ct = default)
    {
        foreach (var message in messages)
        {
            Record(PushKind.GroupBacklog, toUserId,
                contextId: message.TargetId, content: message.Content, message: message);
        }
        return Task.CompletedTask;
    }

    public Task NotifyMessageRecalledAsync(
        MessageRecalled recalled, IReadOnlyList<int> audienceUserIds, CancellationToken ct = default)
    {
        foreach (var userId in audienceUserIds)
        {
            Record(PushKind.MessageRecalled, userId,
                contextId: recalled.DatabaseId, content: recalled.PublicMessageId);
        }
        return Task.CompletedTask;
    }

    public Task ForwardTypingAsync(int fromUserId, int toUserId, CancellationToken ct = default)
        => Record(PushKind.Typing, toUserId, contextId: fromUserId);

    public Task ForwardReadReceiptAsync(
        int readerId, int toUserId, string? messageId, CancellationToken ct = default)
        => Record(PushKind.ReadReceipt, toUserId, contextId: readerId, content: messageId);

    public Task NotifyMutedAsync(
        int userId, long groupId, string reason, string? messageId, CancellationToken ct = default)
        => Record(PushKind.Muted, userId, contextId: groupId, content: reason);

    public Task NotifyMessageBlockedAsync(
        int senderId, int receiverId, string reason, string? messageId, CancellationToken ct = default)
        => Record(PushKind.MessageBlocked, senderId, contextId: receiverId, content: reason);

    private Task Record(
        PushKind kind, int toUserId, long contextId = 0,
        string? content = null, RealtimeMessage? message = null)
    {
        Pushes.Add(new PushRecord(kind, toUserId, contextId, content, message));
        return Task.CompletedTask;
    }
}

internal enum PushKind
{
    FriendRequest,
    FriendAccepted,
    FriendRejected,
    Presence,
    Blocked,
    GroupInvited,
    GroupDissolved,
    PrivateMessage,
    PrivateMessageEcho,
    GroupMessage,
    GroupBacklog,
    MessageRecalled,
    Typing,
    ReadReceipt,
    Muted,
    MessageBlocked
}

/// <summary>一次推送的记录</summary>
/// <param name="Kind">推送种类</param>
/// <param name="ToUserId">推给谁</param>
/// <param name="ContextId">上下文标识（对端用户 / 群 / 消息）</param>
/// <param name="Content">文本载荷</param>
/// <param name="Message">消息类推送的完整载荷</param>
internal sealed record PushRecord(
    PushKind Kind,
    int ToUserId,
    long ContextId,
    string? Content,
    RealtimeMessage? Message);

/// <summary>连接登记替身：可编排谁在线</summary>
internal sealed class FakeConnectionRegistry : IConnectionRegistry
{
    private readonly HashSet<int> _online = new();

    public int ConnectionCount { get; set; }

    public int OnlineUserCount => _online.Count;

    internal List<string> ClosedSessions { get; } = new();

    public bool IsOnline(int userId) => _online.Contains(userId);

    public IReadOnlyDictionary<int, bool> GetOnlineStates(IEnumerable<int> userIds)
        => userIds.Distinct().ToDictionary(id => id, _online.Contains);

    public void CloseSession(string sessionId) => ClosedSessions.Add(sessionId);

    internal void SetOnline(params int[] userIds)
    {
        foreach (var id in userIds) _online.Add(id);
        ConnectionCount = _online.Count;
    }
}
