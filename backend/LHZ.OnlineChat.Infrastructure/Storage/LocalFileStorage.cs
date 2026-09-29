using LHZ.OnlineChat.Application.Abstractions;

namespace LHZ.OnlineChat.Infrastructure.Storage;

/// <summary>文件存储配置</summary>
public sealed class FileStorageOptions
{
    /// <summary>上传根目录的绝对路径（宿主注入 ContentRootPath/uploads）</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>对外访问前缀</summary>
    public string PublicPrefix { get; set; } = "/uploads";
}

/// <summary>
/// 本地磁盘文件存储。
/// 文件名用 GUID 重新生成 —— 不使用客户端提供的文件名，避免路径穿越与覆盖。
/// </summary>
internal sealed class LocalFileStorage : IFileStorage
{
    private readonly FileStorageOptions _options;

    public LocalFileStorage(FileStorageOptions options) => _options = options;

    public async Task<string> SaveAsync(
        FileUpload upload, string subdirectory, CancellationToken ct = default)
    {
        var directory = string.IsNullOrWhiteSpace(subdirectory)
            ? _options.RootPath
            : Path.Combine(_options.RootPath, subdirectory);

        Directory.CreateDirectory(directory);

        var fileName = $"{Guid.NewGuid():N}{upload.Extension}";
        var savePath = Path.Combine(directory, fileName);

        await using (var stream = File.Create(savePath))
        {
            await upload.Content.CopyToAsync(stream, ct).ConfigureAwait(false);
        }

        // URL 一律用正斜杠，不受运行平台路径分隔符影响
        return string.IsNullOrWhiteSpace(subdirectory)
            ? $"{_options.PublicPrefix}/{fileName}"
            : $"{_options.PublicPrefix}/{subdirectory}/{fileName}";
    }
}
