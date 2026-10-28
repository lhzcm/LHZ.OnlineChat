namespace LHZ.OnlineChat.Application.Files;

/// <summary>文件元数据（展示用：原始文件名 + 大小）</summary>
public sealed class FileMetaDto
{
    /// <summary>文件地址（= 消息 content）</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>原始文件名（已清洗，可用于下载名）</summary>
    public string Name { get; set; } = string.Empty;

    public long Size { get; set; }
}
