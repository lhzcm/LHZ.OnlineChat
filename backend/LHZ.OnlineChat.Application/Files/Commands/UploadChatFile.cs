using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Users;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Files;
using MediatR;

namespace LHZ.OnlineChat.Application.Files.Commands;

/// <summary>上传聊天文件（文档/压缩包/代码等，≤50MB）</summary>
public sealed class UploadChatFileCommand : ICommand<ApiResponse<UploadResponse>>
{
    public FileUpload? File { get; set; }

    public int UploaderId { get; set; }
}

internal sealed class UploadChatFileHandler
    : IRequestHandler<UploadChatFileCommand, ApiResponse<UploadResponse>>
{
    private const string FilesSubdirectory = "files";

    private readonly IFileStorage _storage;
    private readonly IFileAttachmentRepository _attachments;
    private readonly IClock _clock;

    public UploadChatFileHandler(
        IFileStorage storage, IFileAttachmentRepository attachments, IClock clock)
    {
        _storage = storage;
        _attachments = attachments;
        _clock = clock;
    }

    public async Task<ApiResponse<UploadResponse>> Handle(
        UploadChatFileCommand command, CancellationToken ct)
    {
        UploadRules.EnsureValidFile(command.File);

        var file = command.File!;
        // 落盘用 GUID 名（避免路径穿越/覆盖），原始名只进元数据表 —— 消息 content 只放 URL
        var url = await _storage.SaveAsync(file, FilesSubdirectory, ct).ConfigureAwait(false);

        var attachment = FileAttachment.Create(
            url, file.FileName, file.Length, file.ContentType, command.UploaderId, _clock.UtcNow);
        await _attachments.AddAsync(attachment, ct).ConfigureAwait(false);

        return ApiResponse<UploadResponse>.Ok(new UploadResponse
        {
            Url = attachment.Url,
            Name = attachment.OriginalName,
            Size = attachment.SizeBytes
        }, "上传成功");
    }
}
