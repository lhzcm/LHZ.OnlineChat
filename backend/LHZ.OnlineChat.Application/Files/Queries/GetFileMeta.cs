using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Files;
using LHZ.OnlineChat.Domain.Messaging;
using MediatR;

namespace LHZ.OnlineChat.Application.Files.Queries;

/// <summary>
/// 按 URL 批量取文件元数据（原始文件名 + 大小）。
/// 前端一次问一屏的消息，避免"每个文件一个请求"的 N+1。
/// </summary>
public sealed class GetFileMetaQuery : IQuery<ApiResponse<List<FileMetaDto>>>
{
    /// <summary>一次最多问多少个（调用方按屏取，超出的下一批）</summary>
    public const int MaxUrls = 50;

    public List<string> Urls { get; set; } = new();
}

internal sealed class GetFileMetaHandler
    : IRequestHandler<GetFileMetaQuery, ApiResponse<List<FileMetaDto>>>
{
    private readonly IFileAttachmentRepository _attachments;

    public GetFileMetaHandler(IFileAttachmentRepository attachments) => _attachments = attachments;

    public async Task<ApiResponse<List<FileMetaDto>>> Handle(
        GetFileMetaQuery query, CancellationToken ct)
    {
        // 只认本站文件目录：别让这个接口变成任意 URL 的探针
        var urls = query.Urls
            .Select(u => (u ?? string.Empty).Trim())
            .Where(u => u.StartsWith(MessageContentRules.FilePathPrefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(GetFileMetaQuery.MaxUrls)
            .ToList();

        if (urls.Count == 0) return ApiResponse<List<FileMetaDto>>.Ok(new List<FileMetaDto>());

        var attachments = await _attachments.ListByUrlsAsync(urls, ct).ConfigureAwait(false);
        var items = attachments.Select(a => new FileMetaDto
        {
            Url = a.Url,
            Name = a.OriginalName,
            Size = a.SizeBytes
        }).ToList();

        return ApiResponse<List<FileMetaDto>>.Ok(items);
    }
}
