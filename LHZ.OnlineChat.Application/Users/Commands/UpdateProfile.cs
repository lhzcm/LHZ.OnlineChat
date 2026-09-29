using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;

namespace LHZ.OnlineChat.Application.Users.Commands;

/// <summary>修改昵称</summary>
public sealed class UpdateNicknameCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    public string Nickname { get; set; } = string.Empty;
}

internal sealed class UpdateNicknameHandler : IRequestHandler<UpdateNicknameCommand, ApiResponse>
{
    private readonly IUserRepository _users;
    private readonly IClock _clock;

    public UpdateNicknameHandler(IUserRepository users, IClock clock)
    {
        _users = users;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(UpdateNicknameCommand command, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(command.UserId, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException("用户不存在");

        user.Rename(command.Nickname, _clock.UtcNow);
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        return ApiResponse.Ok("昵称修改成功");
    }
}

/// <summary>换绑邮箱（需新邮箱验证码 + 新邮箱未被其他账号占用）</summary>
public sealed class ChangeEmailCommand : ICommand<ApiResponse>
{
    public int UserId { get; set; }

    public string NewEmail { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

internal sealed class ChangeEmailHandler : IRequestHandler<ChangeEmailCommand, ApiResponse>
{
    private readonly IUserRepository _users;
    private readonly IVerificationCodeStore _codes;
    private readonly IDomainEventDispatcher _events;
    private readonly IClock _clock;

    public ChangeEmailHandler(
        IUserRepository users,
        IVerificationCodeStore codes,
        IDomainEventDispatcher events,
        IClock clock)
    {
        _users = users;
        _codes = codes;
        _events = events;
        _clock = clock;
    }

    public async Task<ApiResponse> Handle(ChangeEmailCommand command, CancellationToken ct)
    {
        var newEmail = Email.Parse(command.NewEmail);

        var codeOk = await _codes.ValidateAndConsumeAsync(newEmail, command.Code, ct).ConfigureAwait(false);
        DomainException.Ensure(codeOk, "验证码错误或已过期");

        var taken = await _users.EmailExistsAsync(newEmail, excludeUserId: command.UserId, ct: ct)
            .ConfigureAwait(false);
        DomainException.Ensure(!taken, "该邮箱已被其他账号绑定");

        var user = await _users.FindByIdAsync(command.UserId, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException("用户不存在");

        user.ChangeEmail(newEmail, _clock.UtcNow);
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);
        await _events.DispatchEventsOfAsync(user, ct).ConfigureAwait(false);

        return ApiResponse.Ok("邮箱修改成功");
    }
}

/// <summary>上传头像（512×512 裁剪由前端完成，这里只做大小/格式校验与落盘）</summary>
public sealed class UploadAvatarCommand : ICommand<ApiResponse<AvatarResponse>>
{
    public int UserId { get; set; }

    public FileUpload? File { get; set; }
}

internal sealed class UploadAvatarHandler : IRequestHandler<UploadAvatarCommand, ApiResponse<AvatarResponse>>
{
    private const string AvatarSubdirectory = "";

    private readonly IUserRepository _users;
    private readonly IFileStorage _storage;
    private readonly IClock _clock;

    public UploadAvatarHandler(IUserRepository users, IFileStorage storage, IClock clock)
    {
        _users = users;
        _storage = storage;
        _clock = clock;
    }

    public async Task<ApiResponse<AvatarResponse>> Handle(UploadAvatarCommand command, CancellationToken ct)
    {
        UploadRules.EnsureValidImage(command.File, UploadRules.MaxAvatarBytes, "2MB");

        var user = await _users.FindByIdAsync(command.UserId, ct).ConfigureAwait(false)
                   ?? throw new EntityNotFoundException("用户不存在");

        var url = await _storage.SaveAsync(command.File!, AvatarSubdirectory, ct).ConfigureAwait(false);

        user.ChangeAvatar(url, _clock.UtcNow);
        await _users.UpdateAsync(user, ct).ConfigureAwait(false);

        return ApiResponse<AvatarResponse>.Ok(new AvatarResponse { Avatar = url }, "头像修改成功");
    }
}

/// <summary>上传聊天图片</summary>
public sealed class UploadChatImageCommand : ICommand<ApiResponse<UploadResponse>>
{
    public FileUpload? File { get; set; }
}

internal sealed class UploadChatImageHandler : IRequestHandler<UploadChatImageCommand, ApiResponse<UploadResponse>>
{
    private const string ImageSubdirectory = "images";

    private readonly IFileStorage _storage;

    public UploadChatImageHandler(IFileStorage storage) => _storage = storage;

    public async Task<ApiResponse<UploadResponse>> Handle(UploadChatImageCommand command, CancellationToken ct)
    {
        UploadRules.EnsureValidImage(command.File, UploadRules.MaxChatImageBytes, "5MB");

        var url = await _storage.SaveAsync(command.File!, ImageSubdirectory, ct).ConfigureAwait(false);
        return ApiResponse<UploadResponse>.Ok(new UploadResponse { Url = url }, "上传成功");
    }
}
