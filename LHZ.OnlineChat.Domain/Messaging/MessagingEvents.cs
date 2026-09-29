using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Messaging;

/// <summary>
/// 私聊消息已发出（已落库）。
/// 订阅方负责：写 Redis 最近消息缓存、向收发双方全部在线设备广播、触发机器人 Webhook。
/// </summary>
public sealed record PrivateMessageSent(
    long MessageId,
    int SenderId,
    int ReceiverId,
    DateTime OccurredAt) : IDomainEvent;

/// <summary>群聊消息已发出（已落库）</summary>
public sealed record GroupMessageSent(
    long MessageId,
    long GroupId,
    int SenderId,
    DateTime OccurredAt) : IDomainEvent;

/// <summary>
/// 消息被撤回（用户主动撤回或管理后台强制删除）。
/// 订阅方负责：从 Redis 缓存移除该条、向相关方广播 message_recalled。
/// </summary>
/// <param name="PublicMessageId">对外消息标识（客户端 ID 或数据库 ID）</param>
/// <param name="DatabaseId">数据库主键</param>
/// <param name="SessionType">私聊还是群聊</param>
/// <param name="SenderId">原消息发送者</param>
/// <param name="PeerUserId">私聊时为对方账号 ID，群聊时为 null</param>
/// <param name="GroupId">群聊时为群 ID，私聊时为 null</param>
/// <param name="ByAdmin">是否管理后台强制删除（区别于用户自行撤回）</param>
/// <param name="OccurredAt">发生时刻（UTC）</param>
public sealed record MessageRecalled(
    string PublicMessageId,
    long DatabaseId,
    ChatSessionType SessionType,
    int SenderId,
    int? PeerUserId,
    long? GroupId,
    bool ByAdmin,
    DateTime OccurredAt) : IDomainEvent;
