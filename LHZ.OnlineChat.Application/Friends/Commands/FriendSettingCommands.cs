using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Friends;
using MediatR;

namespace LHZ.OnlineChat.Application.Friends.Commands;

/// <summary>设置好友备注（空 = 清除）</summary>
public sealed class SetFriendRemarkCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    public int FriendId { get; set; }

    public string? Remark { get; set; }
}

internal sealed class SetFriendRemarkHandler : IRequestHandler<SetFriendRemarkCommand, ApiResponse>
{
    private readonly FriendSettingWriter _writer;

    public SetFriendRemarkHandler(FriendSettingWriter writer) => _writer = writer;

    public async Task<ApiResponse> Handle(SetFriendRemarkCommand command, CancellationToken ct)
    {
        await _writer
            .UpdateAsync(command.UserId, command.FriendId, s => s.SetRemark(command.Remark, _writer.Now), ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok(string.IsNullOrWhiteSpace(command.Remark) ? "已清除备注" : "备注已保存");
    }
}

/// <summary>设置好友分类标签（空 = 清除，未分组）</summary>
public sealed class SetFriendCategoryCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    public int FriendId { get; set; }

    public string? Category { get; set; }
}

internal sealed class SetFriendCategoryHandler : IRequestHandler<SetFriendCategoryCommand, ApiResponse>
{
    private readonly FriendSettingWriter _writer;

    public SetFriendCategoryHandler(FriendSettingWriter writer) => _writer = writer;

    public async Task<ApiResponse> Handle(SetFriendCategoryCommand command, CancellationToken ct)
    {
        await _writer
            .UpdateAsync(command.UserId, command.FriendId, s => s.SetCategory(command.Category, _writer.Now), ct)
            .ConfigureAwait(false);

        return ApiResponse.Ok(string.IsNullOrWhiteSpace(command.Category) ? "已清除分类" : "分类已保存");
    }
}

/// <summary>
/// 好友设置的 upsert 协作者。
/// 「校验好友关系 → 取设置，没有就建 → 改 → 存」这套动作备注和分类共用，
/// 原先 FriendService 里是一个接收 Func&lt;FriendTag, Task&gt; 的 UpsertTagAsync，
/// 回调里还要自己拼 FreeSql 的 Update 语句。
/// </summary>
public sealed class FriendSettingWriter
{
    private readonly IFriendshipRepository _friendships;
    private readonly IFriendSettingRepository _settings;
    private readonly IClock _clock;

    public FriendSettingWriter(
        IFriendshipRepository friendships, IFriendSettingRepository settings, IClock clock)
    {
        _friendships = friendships;
        _settings = settings;
        _clock = clock;
    }

    internal DateTime Now => _clock.UtcNow;

    internal async Task UpdateAsync(
        int userId, int friendId, Action<FriendSetting> mutate, CancellationToken ct)
    {
        var areFriends = await _friendships.AreFriendsAsync(userId, friendId, ct).ConfigureAwait(false);
        DomainException.Ensure(areFriends, "好友关系不存在");

        var setting = await _settings.FindAsync(userId, friendId, ct).ConfigureAwait(false);
        if (setting is null)
        {
            setting = FriendSetting.CreateFor(userId, friendId, Now);
            mutate(setting);
            await _settings.AddAsync(setting, ct).ConfigureAwait(false);
            return;
        }

        mutate(setting);
        await _settings.UpdateAsync(setting, ct).ConfigureAwait(false);
    }
}
