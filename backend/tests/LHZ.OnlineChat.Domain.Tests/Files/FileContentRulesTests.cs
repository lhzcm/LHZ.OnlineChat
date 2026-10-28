using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Files;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Domain.Tests.Files;

/// <summary>
/// 文件消息的内容规则与元数据。
///
/// 这一层的两条红线：**只认站内文件地址**（否则接收者浏览器会去请求发送者指定的外链）、
/// **拒绝打开即执行的扩展名**（上传目录与站点同源，html/svg 内联执行就是存储型 XSS）。
/// </summary>
public class FileContentRulesTests
{
    [Theory(DisplayName = "站内文件地址通过校验")]
    [InlineData("/uploads/files/3f2a.pdf")]
    [InlineData("/uploads/files/9c1b.zip")]
    [InlineData("/uploads/files/a.md")]
    [InlineData("/uploads/files/a.tar.gz")]
    public void EnsureValid_LocalFileUrl_Passes(string content)
    {
        MessageContentRules.EnsureValid(content, MessageKind.File);
    }

    [Theory(DisplayName = "非站内地址被拒（外链/相对协议/目录穿越/查询串）")]
    [InlineData("https://evil.example/a.pdf")]
    [InlineData("//evil.example/a.pdf")]
    [InlineData("/uploads/files/../secret.pdf")]
    [InlineData("/uploads/files/a.pdf?x=1")]
    [InlineData("/uploads/images/a.pdf")]
    [InlineData("/uploads/files/")]
    public void EnsureValid_ExternalOrMalformed_IsRejected(string content)
    {
        var ex = Assert.Throws<DomainException>(
            () => MessageContentRules.EnsureValid(content, MessageKind.File));

        Assert.Equal("图片、语音或文件地址不合法", ex.Message);
    }

    [Theory(DisplayName = "危险扩展名被拒（浏览器会内联执行或属可执行程序）")]
    [InlineData("/uploads/files/a.html")]
    [InlineData("/uploads/files/a.htm")]
    [InlineData("/uploads/files/a.svg")]
    [InlineData("/uploads/files/a.js")]
    [InlineData("/uploads/files/a.xhtml")]
    [InlineData("/uploads/files/a.swf")]
    [InlineData("/uploads/files/a.exe")]
    [InlineData("/uploads/files/a.bat")]
    [InlineData("/uploads/files/a.ps1")]
    [InlineData("/uploads/files/a.HTML")]
    public void EnsureValid_DangerousExtension_IsRejected(string content)
    {
        var ex = Assert.Throws<DomainException>(
            () => MessageContentRules.EnsureValid(content, MessageKind.File));

        Assert.Equal("该类型的文件不允许发送", ex.Message);
    }

    [Fact(DisplayName = "普通文件类型不受危险扩展名规则影响")]
    public void EnsureSafeFileExtension_CommonTypes_Pass()
    {
        MessageContentRules.EnsureSafeFileExtension("/uploads/files/a.pdf");
        MessageContentRules.EnsureSafeFileExtension("/uploads/files/a.docx");
        MessageContentRules.EnsureSafeFileExtension("/uploads/files/a.zip");
        MessageContentRules.EnsureSafeFileExtension("/uploads/files/a.xml");
    }
}

public class FileAttachmentTests
{
    private static FileAttachment Create(string name = "季度报告.pdf", long size = 1024)
        => FileAttachment.Create("/uploads/files/abc.pdf", name, size, "application/pdf", 10001, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact(DisplayName = "创建：保留原始文件名与大小")]
    public void Create_KeepsNameAndSize()
    {
        var attachment = Create();

        Assert.Equal("/uploads/files/abc.pdf", attachment.Url);
        Assert.Equal("季度报告.pdf", attachment.OriginalName);
        Assert.Equal(1024, attachment.SizeBytes);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(10001, attachment.UploaderId);
    }

    [Fact(DisplayName = "创建：地址必须是站内文件目录")]
    public void Create_ExternalUrl_IsRejected()
    {
        var ex = Assert.Throws<DomainException>(() => FileAttachment.Create(
            "https://evil.example/a.pdf", "a.pdf", 1, null, 10001, DateTime.UtcNow));

        Assert.Equal("文件地址不合法", ex.Message);
    }

    [Fact(DisplayName = "创建：空文件被拒")]
    public void Create_ZeroSize_IsRejected()
    {
        var ex = Assert.Throws<DomainException>(() => FileAttachment.Create(
            "/uploads/files/abc.pdf", "a.pdf", 0, null, 10001, DateTime.UtcNow));

        Assert.Equal("文件不能为空", ex.Message);
    }

    [Fact(DisplayName = "文件名清洗：去路径、去控制字符")]
    public void Create_NormalizesName()
    {
        var attachment = FileAttachment.Create(
            "/uploads/files/abc.pdf", "../../etc/pass\0word.pdf", 1, null, 10001, DateTime.UtcNow);

        Assert.Equal("password.pdf", attachment.OriginalName);
        Assert.DoesNotContain("/", attachment.OriginalName, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "文件名超长：截断但保留扩展名")]
    public void Create_LongName_TruncatedKeepingExtension()
    {
        var longName = new string('名', FileAttachment.MaxNameLength + 40) + ".pdf";

        var attachment = FileAttachment.Create(
            "/uploads/files/abc.pdf", longName, 1, null, 10001, DateTime.UtcNow);

        Assert.True(attachment.OriginalName.Length <= FileAttachment.MaxNameLength);
        Assert.EndsWith(".pdf", attachment.OriginalName, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "文件名为空：回退成 URL 里的文件名")]
    public void Create_EmptyName_FallsBackToUrl()
    {
        var attachment = FileAttachment.Create(
            "/uploads/files/abc.pdf", "   ", 1, null, 10001, DateTime.UtcNow);

        Assert.Equal("abc.pdf", attachment.OriginalName);
    }
}
