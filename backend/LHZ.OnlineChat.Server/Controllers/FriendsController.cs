using LHZ.OnlineChat.Application.Blacklists;
using LHZ.OnlineChat.Application.Friends.Commands;
using LHZ.OnlineChat.Application.Friends.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LHZ.OnlineChat.Server.Controllers;

/// <summary>好友管理</summary>
[Route("api/[controller]")]
[Authorize]
public sealed class FriendsController : ApiControllerBase
{
    /// <summary>好友列表（含在线状态、备注、分类）</summary>
    [HttpGet]
    public Task<IActionResult> GetFriends(CancellationToken ct)
        => Send(new GetFriendsQuery { UserId = UserId }, ct);

    /// <summary>待处理的好友申请</summary>
    [HttpGet("pending")]
    public Task<IActionResult> GetPendingRequests(CancellationToken ct)
        => Send(new GetPendingFriendRequestsQuery { UserId = UserId }, ct);

    /// <summary>发送好友申请（按账号 ID）</summary>
    [HttpPost("request")]
    public Task<IActionResult> SendRequest([FromBody] AddFriendRequest body, CancellationToken ct)
        => Send(new SendFriendRequestCommand { RequesterId = UserId, AccountId = body.AccountId }, ct);

    /// <summary>接受好友申请</summary>
    [HttpPut("accept/{requestId:long}")]
    public Task<IActionResult> AcceptRequest(long requestId, CancellationToken ct)
        => Send(new AcceptFriendRequestCommand { RequestId = requestId, OperatorId = UserId }, ct);

    /// <summary>拒绝好友申请</summary>
    [HttpDelete("reject/{requestId:long}")]
    public Task<IActionResult> RejectRequest(long requestId, CancellationToken ct)
        => Send(new RejectFriendRequestCommand { RequestId = requestId, OperatorId = UserId }, ct);

    /// <summary>删除好友</summary>
    [HttpDelete("{friendId:int}")]
    public Task<IActionResult> DeleteFriend(int friendId, CancellationToken ct)
        => Send(new RemoveFriendCommand { UserId = UserId, FriendId = friendId }, ct);

    /// <summary>设置好友备注（空 = 清除）</summary>
    [HttpPut("{friendId:int}/remark")]
    public Task<IActionResult> SetRemark(
        int friendId, [FromBody] SetFriendRemarkRequest body, CancellationToken ct)
        => Send(new SetFriendRemarkCommand
        {
            UserId = UserId,
            FriendId = friendId,
            Remark = body.Remark
        }, ct);

    /// <summary>设置好友分类（空 = 未分组）</summary>
    [HttpPut("{friendId:int}/category")]
    public Task<IActionResult> SetCategory(
        int friendId, [FromBody] SetFriendCategoryRequest body, CancellationToken ct)
        => Send(new SetFriendCategoryCommand
        {
            UserId = UserId,
            FriendId = friendId,
            Category = body.Category
        }, ct);
}

/// <summary>黑名单</summary>
[Route("api/[controller]")]
[Authorize]
public sealed class BlacklistController : ApiControllerBase
{
    /// <summary>我的黑名单</summary>
    [HttpGet]
    public Task<IActionResult> GetBlacklist(CancellationToken ct)
        => Send(new GetBlacklistQuery { UserId = UserId }, ct);

    /// <summary>拉黑（自动解除好友关系并通知对方）</summary>
    [HttpPost]
    public Task<IActionResult> Block([FromBody] AddFriendRequest body, CancellationToken ct)
        => Send(new BlockUserCommand { UserId = UserId, BlockedUserId = body.AccountId }, ct);

    /// <summary>解除拉黑</summary>
    [HttpDelete("{userId:int}")]
    public Task<IActionResult> Unblock(int userId, CancellationToken ct)
        => Send(new UnblockUserCommand { UserId = UserId, BlockedUserId = userId }, ct);
}

// ==================== 请求体（表现层契约，字段名与改造前一致） ====================

/// <summary>按账号 ID 操作（好友申请 / 拉黑共用）</summary>
public sealed class AddFriendRequest
{
    public int AccountId { get; set; }
}

public sealed class SetFriendRemarkRequest
{
    /// <summary>备注名；空字符串表示清除</summary>
    public string? Remark { get; set; }
}

public sealed class SetFriendCategoryRequest
{
    /// <summary>分类标签；空字符串表示清除（未分组）</summary>
    public string? Category { get; set; }
}
