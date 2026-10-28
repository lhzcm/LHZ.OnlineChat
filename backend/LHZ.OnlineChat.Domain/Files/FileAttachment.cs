using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Files;

/// <summary>
/// 上传文件的元数据（原始文件名 / 大小 / 类型）。
///
/// 为什么单独存一张表：消息的 content 只放 URL（与图片/语音一致，不塞 JSON），
/// 但"文件名 + 大小"是展示必需的信息 —— 服务端落盘用的是 GUID 名，URL 里没有原始名。
/// 元数据放这里，前端按 URL 批量取，刷新页面/换设备/转发详情都能拿到。
/// </summary>
public sealed class FileAttachment : AggregateRoot<long>
{
    /// <summary>展示名长度上限（再长的名字也显示不全，截断但保留扩展名）</summary>
    public const int MaxNameLength = 120;

    private FileAttachment() { }

    /// <summary>文件地址（与消息 content 一致，唯一）</summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>用户看到的原始文件名（已清洗）</summary>
    public string OriginalName { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>浏览器上报的 MIME（只用于展示/排查，不作为信任依据）</summary>
    public string? ContentType { get; private set; }

    public int UploaderId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static FileAttachment Create(
        string url,
        string originalName,
        long sizeBytes,
        string? contentType,
        int uploaderId,
        DateTime now)
    {
        var normalizedUrl = (url ?? string.Empty).Trim();
        DomainException.Ensure(normalizedUrl.StartsWith("/uploads/files/", StringComparison.Ordinal),
            "文件地址不合法");
        DomainException.Ensure(sizeBytes > 0, "文件不能为空");

        var type = (contentType ?? string.Empty).Trim();
        if (type.Length > 120) type = type[..120];

        return new FileAttachment
        {
            Url = normalizedUrl,
            OriginalName = NormalizeName(originalName, normalizedUrl),
            SizeBytes = sizeBytes,
            ContentType = type.Length == 0 ? null : type,
            UploaderId = uploaderId,
            CreatedAt = now
        };
    }

    /// <summary>
    /// 清洗展示名：去掉客户端可能带上的路径、控制字符与首尾空白；
    /// 超长时截断但**保留扩展名**（用户靠它判断文件类型）。
    /// 全部清洗后为空则回退成 URL 里的文件名。
    /// </summary>
    private static string NormalizeName(string? originalName, string url)
    {
        var raw = (originalName ?? string.Empty).Trim();
        raw = Path.GetFileName(raw);
        raw = new string(raw.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (raw.Length == 0) raw = Path.GetFileName(url);

        if (raw.Length <= MaxNameLength) return raw;

        var extension = Path.GetExtension(raw);
        var keep = Math.Max(1, MaxNameLength - extension.Length);
        return raw[..keep] + extension;
    }
}
