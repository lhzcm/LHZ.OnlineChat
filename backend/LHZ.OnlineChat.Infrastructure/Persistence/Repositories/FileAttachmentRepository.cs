using LHZ.OnlineChat.Domain.Files;

namespace LHZ.OnlineChat.Infrastructure.Persistence.Repositories;

/// <summary>上传文件元数据仓储（FreeSql）</summary>
internal sealed class FileAttachmentRepository : IFileAttachmentRepository
{
    private readonly DbSession _db;

    public FileAttachmentRepository(DbSession db) => _db = db;

    public async Task<IReadOnlyList<FileAttachment>> ListByUrlsAsync(
        IEnumerable<string> urls, CancellationToken ct = default)
    {
        var list = urls.Distinct(StringComparer.Ordinal).ToList();
        if (list.Count == 0) return Array.Empty<FileAttachment>();

        return await _db.Select<FileAttachment>()
            .Where(a => list.Contains(a.Url))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public Task<FileAttachment?> FindByUrlAsync(string url, CancellationToken ct = default)
        => _db.Select<FileAttachment>().Where(a => a.Url == url).FirstAsync(ct)!;

    public Task AddAsync(FileAttachment attachment, CancellationToken ct = default)
        => _db.InsertWithLongIdentityAsync(attachment, ct);
}
