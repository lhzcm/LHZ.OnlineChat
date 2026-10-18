using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Groups;

namespace LHZ.OnlineChat.Domain.Tests.Groups;

/// <summary>
/// 邀请链接与入群审批的领域规则。
/// 这几条都是"写错就出事"的地方：token 校验松了等于可枚举，过期判定漏了等于链接永久有效，
/// 审批状态机能被重复处理就会出现"通过两次、写两条成员记录"。
/// </summary>
public class GroupInviteLinkTests
{
    private static Group NewGroup() => Group.Create("技术群", null, 10001, T.Now);

    private static string ValidToken() => new('a', Group.InviteTokenMinLength);

    [Fact(DisplayName = "新群默认：未开启链接、凭链接入群需要审批")]
    public void NewGroup_HasNoInviteLink_AndRequiresApproval()
    {
        var group = NewGroup();

        Assert.Null(group.InviteToken);
        Assert.Null(group.InviteTokenExpiresAt);
        Assert.True(group.JoinApprovalRequired);
        Assert.False(group.HasActiveInviteLink(T.Now));
    }

    [Fact(DisplayName = "生成链接：写入 token 与 7 天有效期")]
    public void EnableInviteLink_SetsTokenAndExpiry()
    {
        var group = NewGroup();

        group.EnableInviteLink(ValidToken(), T.Now);

        Assert.Equal(ValidToken(), group.InviteToken);
        Assert.Equal(T.Now.AddDays(Group.InviteLinkTtlDays), group.InviteTokenExpiresAt);
        Assert.True(group.HasActiveInviteLink(T.Now));
    }

    [Fact(DisplayName = "重置链接：token 被覆盖，旧链接立即失效")]
    public void EnableInviteLink_Again_ReplacesToken()
    {
        var group = NewGroup();
        group.EnableInviteLink(new string('a', 43), T.Now);

        group.EnableInviteLink(new string('b', 43), T.Now);

        Assert.Equal(new string('b', 43), group.InviteToken);
        // 旧 token 已不是当前值 —— 查库时自然查不到
        Assert.NotEqual(new string('a', 43), group.InviteToken);
    }

    [Theory(DisplayName = "token 长度不合法被拒")]
    [InlineData("")]
    [InlineData("short")]
    public void EnableInviteLink_BadLength_Throws(string token)
    {
        var group = NewGroup();

        var ex = Assert.Throws<DomainException>(() => group.EnableInviteLink(token, T.Now));

        Assert.Equal("邀请链接 token 长度不合法", ex.Message);
    }

    [Fact(DisplayName = "token 过长被拒")]
    public void EnableInviteLink_TooLong_Throws()
    {
        var group = NewGroup();
        var token = new string('a', Group.InviteTokenMaxLength + 1);

        Assert.Throws<DomainException>(() => group.EnableInviteLink(token, T.Now));
    }

    [Theory(DisplayName = "token 字符集只允许 base64url 安全字符")]
    [InlineData("aaaaaaaaaaaaaaaa+++/")]
    [InlineData("aaaaaaaaaaaaaaaa aaa")]
    public void EnableInviteLink_BadCharset_Throws(string token)
    {
        var group = NewGroup();

        var ex = Assert.Throws<DomainException>(() => group.EnableInviteLink(token, T.Now));

        Assert.Contains("只能包含字母、数字", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "过期后不可用，且提示统一为「已失效」")]
    public void EnsureInviteLinkUsable_Expired_Throws()
    {
        var group = NewGroup();
        group.EnableInviteLink(ValidToken(), T.Now);

        var later = T.Now.AddDays(Group.InviteLinkTtlDays).AddSeconds(1);

        Assert.False(group.HasActiveInviteLink(later));
        var ex = Assert.Throws<DomainException>(() => group.EnsureInviteLinkUsable(later));
        Assert.Equal("邀请链接已失效，请让群主重新生成", ex.Message);
    }

    [Fact(DisplayName = "未开启链接时校验直接提示已失效（不泄露群是否存在）")]
    public void EnsureInviteLinkUsable_NoToken_ThrowsSameMessage()
    {
        var group = NewGroup();

        var ex = Assert.Throws<DomainException>(() => group.EnsureInviteLinkUsable(T.Now));

        Assert.Equal("邀请链接已失效，请让群主重新生成", ex.Message);
    }

    [Fact(DisplayName = "关闭链接：清空 token 与有效期")]
    public void DisableInviteLink_ClearsToken()
    {
        var group = NewGroup();
        group.EnableInviteLink(ValidToken(), T.Now);

        group.DisableInviteLink();

        Assert.Null(group.InviteToken);
        Assert.Null(group.InviteTokenExpiresAt);
        Assert.False(group.HasActiveInviteLink(T.Now));
    }

    [Fact(DisplayName = "审批开关可关闭：关闭后凭链接直接入群")]
    public void SetJoinApprovalRequired_Toggles()
    {
        var group = NewGroup();

        group.SetJoinApprovalRequired(false);

        Assert.False(group.JoinApprovalRequired);
    }
}

/// <summary>入群申请状态机</summary>
public class GroupJoinRequestTests
{
    private static GroupJoinRequest NewRequest(string? message = "想进群交流")
        => GroupJoinRequest.Submit(7, 10002, message, T.Now);

    [Fact(DisplayName = "提交：初始为待审批，记录群/用户/附言")]
    public void Submit_StartsPending()
    {
        var request = NewRequest();

        Assert.Equal(7, request.GroupId);
        Assert.Equal(10002, request.UserId);
        Assert.Equal(JoinRequestStatus.Pending, request.Status);
        Assert.Equal("想进群交流", request.Message);
        Assert.Equal(T.Now, request.CreatedAt);
        Assert.Null(request.HandledAt);
    }

    [Theory(DisplayName = "附言：空白变 null，超长截断")]
    [InlineData("   ", null)]
    [InlineData("  想进群  ", "想进群")]
    public void Submit_NormalizesMessage(string input, string? expected)
    {
        var request = GroupJoinRequest.Submit(7, 10002, input, T.Now);

        Assert.Equal(expected, request.Message);
    }

    [Fact(DisplayName = "附言超长被截断到上限")]
    public void Submit_TooLongMessage_IsTruncated()
    {
        var request = GroupJoinRequest.Submit(
            7, 10002, new string('x', GroupJoinRequest.MaxMessageLength + 20), T.Now);

        Assert.Equal(GroupJoinRequest.MaxMessageLength, request.Message!.Length);
    }

    [Fact(DisplayName = "通过：记录处理人与时间，不能重复处理")]
    public void Approve_SetsHandled_AndIsTerminal()
    {
        var request = NewRequest();

        request.Approve(10001, T.Now);

        Assert.Equal(JoinRequestStatus.Approved, request.Status);
        Assert.Equal(10001, request.HandledBy);
        Assert.Equal(T.Now, request.HandledAt);
        Assert.Throws<DomainException>(() => request.Approve(10001, T.Now));
        Assert.Throws<DomainException>(() => request.Reject(10001, T.Now));
    }

    [Fact(DisplayName = "拒绝：记录状态，不能重复处理")]
    public void Reject_SetsStatus_AndIsTerminal()
    {
        var request = NewRequest();

        request.Reject(10001, T.Now);

        Assert.Equal(JoinRequestStatus.Rejected, request.Status);
        Assert.Throws<DomainException>(() => request.Reject(10001, T.Now));
        Assert.Throws<DomainException>(() => request.Approve(10001, T.Now));
    }
}
