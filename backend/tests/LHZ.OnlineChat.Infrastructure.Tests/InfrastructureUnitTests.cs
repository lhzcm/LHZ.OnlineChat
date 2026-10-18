using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Infrastructure.Caching;
using LHZ.OnlineChat.Infrastructure.Common;
using LHZ.OnlineChat.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace LHZ.OnlineChat.Infrastructure.Tests;

/// <summary>
/// Redis 键位约定。
/// 这些键必须与改造前逐字一致 —— 否则灰度期间新旧版本各写一套键，
/// 会表现为「刚登录就被判定会话失效」「在线状态错乱」。
/// </summary>
public class RedisKeysTests
{
    [Fact(DisplayName = "邮箱验证码键")]
    public void EmailCode_Build_ProducesExpectedKey()
        => Assert.Equal("email:code:a@test.local", RedisKeys.EmailCode("a@test.local"));

    [Fact(DisplayName = "刷新令牌键按会话")]
    public void RefreshToken_Build_ProducesSessionScopedKey()
        => Assert.Equal("token:refresh:abc123", RedisKeys.RefreshToken("abc123"));

    [Fact(DisplayName = "刷新令牌反查键含哈希")]
    public void RefreshLookup_ContainsTokenHash()
        => Assert.Equal("token:refresh:lookup:deadbeef", RedisKeys.RefreshLookup("deadbeef"));

    [Fact(DisplayName = "会话元数据键")]
    public void SessionMeta_Build_ProducesExpectedKey()
        => Assert.Equal("sess:meta:abc123", RedisKeys.SessionMeta("abc123"));

    [Fact(DisplayName = "用户会话集合键")]
    public void UserSessions_Build_ProducesExpectedKey()
        => Assert.Equal("sess:10001", RedisKeys.UserSessions(10001));

    [Fact(DisplayName = "在线状态键")]
    public void OnlineStatus_Build_ProducesExpectedKey()
        => Assert.Equal("ws:online:10001", RedisKeys.Online(10001));

    [Fact(DisplayName = "群聊缓存键")]
    public void GroupChatCache_Build_ProducesExpectedKey()
        => Assert.Equal("chat:group:42", RedisKeys.GroupChat(42));

    [Theory(DisplayName = "私聊缓存键按小大ID归一化_双向命中同一键")]
    [InlineData(10001, 10002)]
    [InlineData(10002, 10001)]
    public void PrivateChatCache_NormalizesIdOrder_HitsSameKeyBothWays(int a, int b)
    {
        // 这是改造前就存在的约定：不归一化的话两人各写一份缓存，历史会不一致
        Assert.Equal("chat:private:10001:10002", RedisKeys.PrivateChat(a, b));
    }

    [Fact(DisplayName = "私聊缓存键_与自己对话时两端相同")]
    public void PrivateChatCache_SelfConversation_UsesSameKeyOnBothEnds()
        => Assert.Equal("chat:private:10001:10001", RedisKeys.PrivateChat(10001, 10001));
}

public class SystemClockTests
{
    [Fact(DisplayName = "返回UTC时间")]
    public void UtcNow_ReturnsTimestamp_WithUtcKind()
    {
        var now = new SystemClock().UtcNow;

        Assert.Equal(DateTimeKind.Utc, now.Kind);
        Assert.InRange(now, DateTime.UtcNow.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
    }
}

/// <summary>
/// 禁言提示语格式化。
/// 关键点：Windows 与 Linux 的时区 ID 不同（容器里只有 Asia/Shanghai），
/// 两个都找不到时必须回落固定 +8 偏移而不是抛异常让整条发言链路挂掉。
/// </summary>
public class MuteMessageFormatterTests
{
    private readonly MuteMessageFormatter _formatter =
        new(NullLogger<MuteMessageFormatter>.Instance);

    [Fact(DisplayName = "按东八区渲染截止时间")]
    public void Format_RendersDeadlineInChinaStandardTime()
    {
        var utc = new DateTime(2026, 3, 14, 4, 30, 0, DateTimeKind.Utc);

        var message = _formatter.Format(utc);

        // UTC 04:30 → 东八区 12:30
        Assert.Equal("你已被禁言至 03-14 12:30，期间无法在群里发言", message);
    }

    [Fact(DisplayName = "跨日边界正确")]
    public void Format_AcrossDayBoundary_KeepsCorrectDate()
    {
        var utc = new DateTime(2026, 3, 14, 20, 0, 0, DateTimeKind.Utc);

        var message = _formatter.Format(utc);

        // UTC 20:00 → 东八区次日 04:00
        Assert.Contains("03-15 04:00", message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "数据库读出的Unspecified时间也按UTC处理")]
    public void Format_UnspecifiedKindFromDatabase_TreatedAsUtc()
    {
        var unspecified = new DateTime(2026, 3, 14, 4, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal(
            _formatter.Format(new DateTime(2026, 3, 14, 4, 30, 0, DateTimeKind.Utc)),
            _formatter.Format(unspecified));
    }
}

public class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "lhz-storage-tests-" + Guid.NewGuid().ToString("N"));

    private LocalFileStorage Storage()
        => new(new FileStorageOptions { RootPath = _root, PublicPrefix = "/uploads" });

    private static FileUpload Upload(string fileName, string content = "data")
        => new()
        {
            FileName = fileName,
            Length = content.Length,
            Content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))
        };

    [Fact(DisplayName = "保存到根目录并返回公开地址")]
    public async Task Save_ToRoot_ReturnsPublicUrl()
    {
        var url = await Storage().SaveAsync(Upload("avatar.png"), string.Empty);

        Assert.StartsWith("/uploads/", url, StringComparison.Ordinal);
        Assert.EndsWith(".png", url, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "保存到子目录时地址含子目录")]
    public async Task Save_ToSubdirectory_UrlIncludesSubdirectory()
    {
        var url = await Storage().SaveAsync(Upload("chat.jpg"), "images");

        Assert.StartsWith("/uploads/images/", url, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "文件实际写入磁盘且内容一致")]
    public async Task Save_WritesFileToDisk_WithSameContent()
    {
        var url = await Storage().SaveAsync(Upload("a.png", "hello"), string.Empty);

        var fileName = url.Split('/')[^1];
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(_root, fileName)));
    }

    [Fact(DisplayName = "文件名用GUID重新生成_不使用客户端文件名")]
    public async Task Save_RegeneratesFileNameWithGuid_IgnoresClientName()
    {
        // 直接用客户端文件名会带来路径穿越与互相覆盖的风险
        var url = await Storage().SaveAsync(Upload("../../etc/passwd.png"), string.Empty);

        Assert.DoesNotContain("..", url, StringComparison.Ordinal);
        Assert.DoesNotContain("passwd", url, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "同名文件不互相覆盖")]
    public async Task Save_SameFileName_DoesNotOverwrite()
    {
        var storage = Storage();

        var first = await storage.SaveAsync(Upload("same.png", "first"), string.Empty);
        var second = await storage.SaveAsync(Upload("same.png", "second"), string.Empty);

        Assert.NotEqual(first, second);
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    [Fact(DisplayName = "子目录不存在时自动创建")]
    public async Task Save_MissingSubdirectory_CreatesItAutomatically()
    {
        await Storage().SaveAsync(Upload("a.png"), "deep");

        Assert.True(Directory.Exists(Path.Combine(_root, "deep")));
    }

    [Fact(DisplayName = "返回的地址一律用正斜杠_不受平台路径分隔符影响")]
    public async Task Save_ReturnedUrl_UsesForwardSlashesOnAllPlatforms()
    {
        var url = await Storage().SaveAsync(Upload("a.png"), "images");

        Assert.DoesNotContain('\\', url);
    }

    [Fact(DisplayName = "保留原始扩展名")]
    public async Task Save_PreservesOriginalExtension()
    {
        foreach (var ext in new[] { ".png", ".jpg", ".gif", ".webp" })
        {
            var url = await Storage().SaveAsync(Upload($"file{ext}"), string.Empty);
            Assert.EndsWith(ext, url, StringComparison.Ordinal);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}

public class UploadRulesTests
{
    private static FileUpload Upload(string fileName, long length)
        => new() { FileName = fileName, Length = length, Content = new MemoryStream() };

    [Theory(DisplayName = "白名单扩展名通过")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".gif")]
    [InlineData(".webp")]
    public void EnsureValidImage_WhitelistedExtension_IsAccepted(string ext)
    {
        UploadRules.EnsureValidImage(Upload($"a{ext}", 1024), UploadRules.MaxAvatarBytes, "2MB");
    }

    [Theory(DisplayName = "扩展名大小写不敏感")]
    [InlineData(".JPG")]
    [InlineData(".PNG")]
    public void EnsureValidImage_ExtensionCase_IsIgnored(string ext)
    {
        UploadRules.EnsureValidImage(Upload($"a{ext}", 1024), UploadRules.MaxAvatarBytes, "2MB");
    }

    [Theory(DisplayName = "非白名单扩展名被拒")]
    [InlineData(".bmp")]
    [InlineData(".svg")]
    [InlineData(".exe")]
    [InlineData(".php")]
    [InlineData("")]
    public void EnsureValidImage_NonWhitelistedExtension_IsRejected(string ext)
    {
        var ex = Assert.Throws<Domain.Common.DomainException>(() => UploadRules.EnsureValidImage(
            Upload($"a{ext}", 1024), UploadRules.MaxAvatarBytes, "2MB"));

        Assert.Equal("仅支持 jpg / png / gif / webp 格式图片", ex.Message);
    }

    [Fact(DisplayName = "未选文件被拒")]
    public void EnsureValidImage_NullFile_IsRejected()
    {
        var ex = Assert.Throws<Domain.Common.DomainException>(
            () => UploadRules.EnsureValidImage(null, UploadRules.MaxAvatarBytes, "2MB"));

        Assert.Equal("请选择图片文件", ex.Message);
    }

    [Fact(DisplayName = "空文件被拒")]
    public void EnsureValidImage_EmptyFile_IsRejected()
    {
        var ex = Assert.Throws<Domain.Common.DomainException>(() => UploadRules.EnsureValidImage(
            Upload("a.png", 0), UploadRules.MaxAvatarBytes, "2MB"));

        Assert.Equal("请选择图片文件", ex.Message);
    }

    [Fact(DisplayName = "超出大小上限被拒_提示语带可读上限")]
    public void EnsureValidImage_OverSizeLimit_RejectedWithReadableLimit()
    {
        var ex = Assert.Throws<Domain.Common.DomainException>(() => UploadRules.EnsureValidImage(
            Upload("a.png", UploadRules.MaxAvatarBytes + 1), UploadRules.MaxAvatarBytes, "2MB"));

        Assert.Equal("图片大小不能超过 2MB", ex.Message);
    }

    [Fact(DisplayName = "恰好达到上限时通过")]
    public void EnsureValidImage_ExactlyAtLimit_IsAccepted()
    {
        UploadRules.EnsureValidImage(
            Upload("a.png", UploadRules.MaxAvatarBytes), UploadRules.MaxAvatarBytes, "2MB");
    }

    [Fact(DisplayName = "头像与聊天图片的上限不同")]
    public void Limits_AvatarAndChatImage_Differ()
    {
        Assert.Equal(2 * 1024 * 1024, UploadRules.MaxAvatarBytes);
        Assert.Equal(5 * 1024 * 1024, UploadRules.MaxChatImageBytes);
    }

    [Theory(DisplayName = "语音白名单扩展名通过（含浏览器与 iOS 的产出格式）")]
    [InlineData(".webm")]
    [InlineData(".ogg")]
    [InlineData(".mp3")]
    [InlineData(".m4a")]
    [InlineData(".mp4")]
    [InlineData(".wav")]
    public void EnsureValidVoice_WhitelistedExtension_IsAccepted(string ext)
    {
        UploadRules.EnsureValidVoice(Upload($"a{ext}", 1024));
    }

    [Theory(DisplayName = "语音扩展名大小写不敏感")]
    [InlineData(".WEBM")]
    [InlineData(".M4A")]
    public void EnsureValidVoice_ExtensionCase_IsIgnored(string ext)
    {
        UploadRules.EnsureValidVoice(Upload($"a{ext}", 1024));
    }

    [Theory(DisplayName = "非语音扩展名被拒")]
    [InlineData(".png")]
    [InlineData(".svg")]
    [InlineData(".exe")]
    [InlineData("")]
    public void EnsureValidVoice_NonWhitelistedExtension_IsRejected(string ext)
    {
        var ex = Assert.Throws<Domain.Common.DomainException>(
            () => UploadRules.EnsureValidVoice(Upload($"a{ext}", 1024)));

        Assert.Equal("仅支持 webm / ogg / mp3 / m4a / mp4 / wav 格式语音", ex.Message);
    }

    [Fact(DisplayName = "语音未选文件/空文件被拒")]
    public void EnsureValidVoice_NullOrEmpty_IsRejected()
    {
        Assert.Equal(
            "请先录音再发送",
            Assert.Throws<Domain.Common.DomainException>(() => UploadRules.EnsureValidVoice(null)).Message);
        Assert.Equal(
            "请先录音再发送",
            Assert.Throws<Domain.Common.DomainException>(
                () => UploadRules.EnsureValidVoice(Upload("a.webm", 0))).Message);
    }

    [Fact(DisplayName = "语音上限为10MB，超一点就拒")]
    public void EnsureValidVoice_SizeLimit_IsEnforced()
    {
        UploadRules.EnsureValidVoice(Upload("a.webm", UploadRules.MaxVoiceBytes));

        var ex = Assert.Throws<Domain.Common.DomainException>(
            () => UploadRules.EnsureValidVoice(Upload("a.webm", UploadRules.MaxVoiceBytes + 1)));

        Assert.Equal("语音大小不能超过 10MB", ex.Message);
    }
}
