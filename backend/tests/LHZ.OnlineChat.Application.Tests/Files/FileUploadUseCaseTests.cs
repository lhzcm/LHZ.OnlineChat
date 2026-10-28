using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Application.Files.Commands;
using LHZ.OnlineChat.Application.Files.Queries;
using LHZ.OnlineChat.Application.Messaging;
using LHZ.OnlineChat.Application.Messaging.Commands;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Tests.Files;

/// <summary>
/// 文件上传 / 元数据 / 发送 / 转发。
/// 关键点：落盘名由服务端生成（原始名只进元数据表）、类型白名单、危险扩展名被拒、
/// 消息 content 只放站内地址。
/// </summary>
public class FileUploadUseCaseTests
{
    private readonly ApplicationTestContext _ctx = new();

    private UploadChatFileHandler Handler()
        => new(_ctx.Files, _ctx.FileAttachments, _ctx.Clock);

    private static FileUpload Upload(string name, long length = 1024, string? contentType = null)
        => new()
        {
            FileName = name,
            Length = length,
            Content = new MemoryStream(new byte[1]),
            ContentType = contentType
        };

    [Fact(DisplayName = "上传文件：存到 files/ 子目录、落库元数据、返回原始名与大小")]
    public async Task Upload_File_StoredAndPersisted()
    {
        var user = _ctx.GivenUser();

        var result = await Handler().Handle(new UploadChatFileCommand
        {
            File = Upload("季度报告.pdf", 2048, "application/pdf"),
            UploaderId = user.Id
        }, default);

        Assert.True(result.Success);
        Assert.Equal("/uploads/files/stored.pdf", result.Data!.Url);
        Assert.Equal("季度报告.pdf", result.Data.Name);
        Assert.Equal(2048, result.Data.Size);

        Assert.Equal("files", _ctx.Files.Saved[0].Subdirectory);
        var row = Assert.Single(_ctx.FileAttachments.All);
        Assert.Equal("/uploads/files/stored.pdf", row.Url);
        Assert.Equal(user.Id, row.UploaderId);
    }

    [Fact(DisplayName = "超过 50MB 被拒")]
    public async Task Upload_TooLarge_IsRejected()
    {
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new UploadChatFileCommand
            {
                File = Upload("big.zip", UploadRules.MaxChatFileBytes + 1),
                UploaderId = user.Id
            }, default));

        Assert.Equal("文件大小不能超过 50MB", ex.Message);
        Assert.Empty(_ctx.FileAttachments.All);
        Assert.Empty(_ctx.Files.Saved);
    }

    [Theory(DisplayName = "危险/不支持的类型被拒")]
    [InlineData("a.exe")]
    [InlineData("a.html")]
    [InlineData("a.svg")]
    [InlineData("a.js")]
    [InlineData("a.ps1")]
    public async Task Upload_DangerousExtension_IsRejected(string name)
    {
        var user = _ctx.GivenUser();

        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new UploadChatFileCommand { File = Upload(name), UploaderId = user.Id }, default));

        Assert.StartsWith("不支持该类型的文件", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "空文件被拒")]
    public async Task Upload_Empty_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(
            new UploadChatFileCommand { File = null, UploaderId = 10001 }, default));

        Assert.Equal("请选择要发送的文件", ex.Message);
    }
}

public class FileMetaUseCaseTests
{
    private readonly ApplicationTestContext _ctx = new();

    private GetFileMetaHandler Handler() => new(_ctx.FileAttachments);

    private async Task SeedAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await _ctx.FileAttachments.AddAsync(Domain.Files.FileAttachment.Create(
                $"/uploads/files/f{i}.pdf", $"文件{i}.pdf", 100 + i, null, 10001, _ctx.Clock.UtcNow));
        }
    }

    [Fact(DisplayName = "批量取元数据：只回已知的站内文件地址")]
    public async Task Meta_ReturnsOnlyKnownLocalFiles()
    {
        await SeedAsync(2);

        var result = await Handler().Handle(new GetFileMetaQuery
        {
            Urls =
            [
                "/uploads/files/f0.pdf",
                "/uploads/files/f1.pdf",
                "/uploads/files/missing.pdf",
                "https://evil.example/a.pdf",
                "/uploads/images/a.png"
            ]
        }, default);

        Assert.Equal(2, result.Data!.Count);
        Assert.Contains(result.Data, m => m.Name == "文件0.pdf" && m.Size == 100);
        Assert.DoesNotContain(result.Data, m => m.Url.Contains("evil", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "批量取元数据：去重且最多 50 个")]
    public async Task Meta_DedupesAndCapsAtFifty()
    {
        await SeedAsync(60);

        var urls = Enumerable.Range(0, 60).Select(i => $"/uploads/files/f{i}.pdf")
            .Concat(Enumerable.Repeat("/uploads/files/f0.pdf", 5))
            .ToList();

        var result = await Handler().Handle(new GetFileMetaQuery { Urls = urls }, default);

        Assert.Equal(GetFileMetaQuery.MaxUrls, result.Data!.Count);
    }

    [Fact(DisplayName = "批量取元数据：非法地址不触发查询")]
    public async Task Meta_NoValidUrls_ReturnsEmpty()
    {
        var result = await Handler().Handle(new GetFileMetaQuery
        {
            Urls = ["https://evil.example/a.pdf", "../../etc/passwd"]
        }, default);

        Assert.Empty(result.Data!);
    }
}

public class FileMessageSendTests
{
    private readonly ApplicationTestContext _ctx = new();

    private SendPrivateMessageHandler Handler()
        => new(_ctx.PrivateMessages, _ctx.Blacklist, _ctx.Friendships, _ctx.Users,
            _ctx.Cache, _ctx.Notifier, _ctx.Events, _ctx.Clock);

    [Fact(DisplayName = "发送文件消息：站内文件地址落库且类型为 File")]
    public async Task Send_LocalFile_Succeeds()
    {
        var (a, b) = _ctx.GivenFriends();

        await Handler().Handle(new SendPrivateMessageCommand
        {
            SenderId = a.Id, ReceiverId = b.Id, Content = "/uploads/files/a.pdf", Kind = MessageKind.File
        }, default);

        Assert.Equal(MessageKind.File, Assert.Single(_ctx.PrivateMessages.All).Kind);
    }

    [Theory(DisplayName = "发送文件消息：外链或危险类型被拒")]
    [InlineData("https://evil.example/a.pdf")]
    [InlineData("/uploads/files/a.html")]
    public async Task Send_InvalidFileContent_IsRejected(string content)
    {
        var (a, b) = _ctx.GivenFriends();

        await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(new SendPrivateMessageCommand
        {
            SenderId = a.Id, ReceiverId = b.Id, Content = content, Kind = MessageKind.File
        }, default));

        Assert.Empty(_ctx.PrivateMessages.All);
    }
}

public class ForwardBundleFileTests
{
    private static string Bundle(string url, int kind)
        => $$"""{"v":1,"items":[{"n":"张三","c":{{System.Text.Json.JsonSerializer.Serialize(url)}},"t":{{kind}},"ts":1}]}""";

    [Fact(DisplayName = "转发卡片允许 t=2 的站内文件地址")]
    public void EnsureValid_FileItem_Passes()
    {
        ForwardBundle.EnsureValid(Bundle("/uploads/files/a.pdf", 2));
    }

    [Fact(DisplayName = "转发卡片拒绝外链文件地址")]
    public void EnsureValid_ExternalFile_IsRejected()
    {
        var ex = Assert.Throws<DomainException>(
            () => ForwardBundle.EnsureValid(Bundle("https://evil.example/a.pdf", 2)));

        Assert.Equal("图片、语音或文件地址不合法", ex.Message);
    }

    [Fact(DisplayName = "转发卡片拒绝危险文件扩展名")]
    public void EnsureValid_DangerousFile_IsRejected()
    {
        var ex = Assert.Throws<DomainException>(
            () => ForwardBundle.EnsureValid(Bundle("/uploads/files/a.html", 2)));

        Assert.Equal("该类型的文件不允许发送", ex.Message);
    }
}
