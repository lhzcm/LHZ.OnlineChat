namespace LHZ.OnlineChat.Domain.Files;

/// <summary>上传文件元数据仓储</summary>
public interface IFileAttachmentRepository
{
    /// <summary>按地址批量取（前端一次问多个文件，避免逐条查询）</summary>
    Task<IReadOnlyList<FileAttachment>> ListByUrlsAsync(
        IEnumerable<string> urls, CancellationToken ct = default);

    Task<FileAttachment?> FindByUrlAsync(string url, CancellationToken ct = default);

    Task AddAsync(FileAttachment attachment, CancellationToken ct = default);
}
