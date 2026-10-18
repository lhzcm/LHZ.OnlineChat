using LHZ.OnlineChat.Domain.Groups;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Groups;

/// <summary>
/// 把用户写进群成员的唯一入口。
///
/// 三条入群路径（被邀请 / 开放加入 / 凭邀请链接加入）都要做同一件事：
/// 已读游标 = 群当前最新消息 ID，避免把入群前的历史当成离线消息补发。
/// 改造前这段在 JoinGroupHandler 与 InviteGroupMembersHandler 各写了一遍，
/// 谁漏掉游标初始化，新成员上线就会被历史消息刷屏。
/// </summary>
internal static class GroupMembershipJoiner
{
    internal static async Task JoinAsync(
        IGroupMemberRepository members,
        IGroupMessageRepository messages,
        long groupId,
        int userId,
        DateTime now,
        CancellationToken ct)
    {
        var latestId = await messages.MaxIdOfGroupAsync(groupId, ct).ConfigureAwait(false);
        await members.AddAsync(GroupMember.Join(groupId, userId, latestId, now), ct).ConfigureAwait(false);
    }
}
