using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Groups;

/// <summary>群组已创建</summary>
public sealed record GroupCreated(long GroupId, string Name, int OwnerId, DateTime OccurredAt) : IDomainEvent;

/// <summary>好友被邀请入群 —— 订阅方向每个被邀请者推送 group_invited</summary>
public sealed record GroupMembersInvited(long GroupId, IReadOnlyList<int> InvitedUserIds, int InviterId, DateTime OccurredAt) : IDomainEvent;

/// <summary>群公告变更（HasAnnouncement=false 表示被清除）</summary>
public sealed record GroupAnnouncementChanged(long GroupId, int EditedBy, bool HasAnnouncement, DateTime OccurredAt) : IDomainEvent;

/// <summary>群主转让</summary>
public sealed record GroupOwnershipTransferred(long GroupId, int PreviousOwnerId, int NewOwnerId, DateTime OccurredAt) : IDomainEvent;

/// <summary>群被解散 —— 订阅方向解散前的成员推送 group_dissolved，客户端自动退出会话</summary>
public sealed record GroupDissolved(long GroupId, string Name, IReadOnlyList<int> MemberIds, DateTime OccurredAt) : IDomainEvent;

/// <summary>成员被移出群</summary>
public sealed record GroupMemberRemoved(long GroupId, int UserId, DateTime OccurredAt) : IDomainEvent;

/// <summary>成员禁言状态变更（MutedUntil=null 表示解除）</summary>
public sealed record GroupMemberMuteChanged(long GroupId, int UserId, DateTime? MutedUntil, DateTime OccurredAt) : IDomainEvent;
