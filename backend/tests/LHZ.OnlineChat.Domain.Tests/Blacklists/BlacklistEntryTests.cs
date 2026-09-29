using LHZ.OnlineChat.Domain.Blacklists;
using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Tests.Blacklists;

public class BlacklistEntryTests
{
    [Fact]
    public void Create_记录拉黑方向()
    {
        var entry = BlacklistEntry.Create(userId: 10001, blockedUserId: 10002, T.Now);

        Assert.Equal(10001, entry.UserId);
        Assert.Equal(10002, entry.BlockedUserId);
        Assert.Equal(T.Now, entry.CreatedAt);
    }

    [Fact]
    public void Create_不能拉黑自己()
    {
        var ex = Assert.Throws<DomainException>(() => BlacklistEntry.Create(10001, 10001, T.Now));

        Assert.Equal("不能拉黑自己", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_被拉黑ID非法时抛出(int blockedUserId)
    {
        var ex = Assert.Throws<DomainException>(
            () => BlacklistEntry.Create(10001, blockedUserId, T.Now));

        Assert.Equal("无效的用户 ID", ex.Message);
    }

    [Fact]
    public void UserBlocked事件携带双方标识()
    {
        // 这个事件有两个订阅方：解除好友关系 + 推送 WS 通知。
        // 改造前这两件事一个写在 Service、一个写在 Controller，被劈成了两半。
        var e = new UserBlocked(BlockerId: 10001, BlockedUserId: 10002, T.Now);

        Assert.Equal(10001, e.BlockerId);
        Assert.Equal(10002, e.BlockedUserId);
        Assert.Equal(T.Now, e.OccurredAt);
        Assert.IsAssignableFrom<IDomainEvent>(e);
    }
}
