using Microsoft.AspNetCore.StaticFiles;

namespace LHZ.OnlineChat.Server.Configuration;

/// <summary>
/// 上传目录（`/uploads`）的 Content-Type 映射。
///
/// **为什么必须兜底成 application/octet-stream**：`StaticFileMiddleware` 在
/// `IContentTypeProvider.TryGetContentType` 返回 false 时不会发送文件，而是把请求交给
/// 后续中间件 —— 后端没有 `/uploads` 的兜底路由，最终就是一个 404。
/// 而默认映射表并不覆盖我们允许上传的全部扩展名（实测 56 个里有 20 个缺失，
/// 例如 `.cs .vue .yaml .log .7z .sql .mkv .odt`），于是"文件传上去了但点下载 404"。
///
/// 注意：`FileExtensionContentTypeProvider.TryGetContentType` 不是 virtual，
/// 所以这里直接实现 `IContentTypeProvider` 而不是继承覆盖。
///
/// 文件目录本来就强制 `Content-Disposition: attachment`（见 Program.cs），
/// 类型声明成 octet-stream 只是让浏览器愿意接收，不会带来内联执行风险。
/// </summary>
internal sealed class UploadContentTypeProvider : IContentTypeProvider
{
    private readonly FileExtensionContentTypeProvider _inner = new();

    public UploadContentTypeProvider()
    {
        // 语音消息：默认映射没覆盖 m4a/webm 的音频 Content-Type，Safari 会拒绝播放
        _inner.Mappings[".webm"] = "audio/webm";
        _inner.Mappings[".ogg"] = "audio/ogg";
        _inner.Mappings[".m4a"] = "audio/mp4";
        _inner.Mappings[".mp4"] = "audio/mp4";
        _inner.Mappings[".mp3"] = "audio/mpeg";
        _inner.Mappings[".wav"] = "audio/wav";
    }

    /// <summary>未知扩展名一律按二进制流返回（文件目录强制下载，不会内联渲染）</summary>
    public bool TryGetContentType(string subpath, out string contentType)
    {
        if (_inner.TryGetContentType(subpath, out var known))
        {
            contentType = known;
            return true;
        }

        contentType = "application/octet-stream";
        return true;
    }
}
