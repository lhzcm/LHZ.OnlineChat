using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Friends;

/// <summary>好友申请已发出 —— 订阅方推送 WS friend_request 给被申请方</summary>
public sealed record FriendRequestSent(int RequesterId, int TargetId, DateTime OccurredAt) : IDomainEvent;

/// <summary>好友申请被接受 —— 订阅方双向推送 friend_accepted（双方刷新好友列表）</summary>
public sealed record FriendRequestAccepted(int RequesterId, int AccepterId, DateTime OccurredAt) : IDomainEvent;

/// <summary>好友申请被拒绝 —— 订阅方推送 friend_rejected 给申请人</summary>
public sealed record FriendRequestRejected(int RequesterId, int RejecterId, DateTime OccurredAt) : IDomainEvent;

/// <summary>好友关系已解除</summary>
public sealed record FriendRemoved(int UserId, int FriendId, DateTime OccurredAt) : IDomainEvent;
