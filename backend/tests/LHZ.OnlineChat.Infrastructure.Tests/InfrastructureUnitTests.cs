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
    [Fact]
    public void 邮箱验证码键()
        => Assert.Equal("email:code:a@test.local", RedisKeys.EmailCode("a@test.local"));

    [Fact]
    public void 刷新令牌键按会话()
        => Assert.Equal("token:refresh:abc123", RedisKeys.RefreshToken("abc123"));

    [Fact]
    public void 刷新令牌反查键含哈希()
        => Assert.Equal("token:refresh:lookup:deadbeef", RedisKeys.RefreshLookup("deadbeef"));

    [Fact]
    public void 会话元数据键()
        => Assert.Equal("sess:meta:abc123", RedisKeys.SessionMeta("abc123"));

    [Fact]
    public void 用户会话集合键()
        => Assert.Equal("sess:10001", RedisKeys.UserSessions(10001));

    [Fact]
    public void 在线状态键()
        => Assert.Equal("ws:online:10001", RedisKeys.Online(10001));

    [Fact]
    public void 群聊缓存键()
        => Assert.Equal("chat:group:42", RedisKeys.GroupChat(42));

    [Theory]
    [InlineData(10001, 10002)]
    [InlineData(10002, 10001)]
    public void 私聊缓存键按小大ID归一化_双向命中同一键(int a, int b)
    {
        // 这是改造前就存在的约定：不归一化的话两人各写一份缓存，历史会不一致
        Assert.Equal("chat:private:10001:10002", RedisKeys.PrivateChat(a, b));
    }

    [Fact]
    public void 私聊缓存键_与自己对话时两端相同()
        => Assert.Equal("chat:private:10001:10001", RedisKeys.PrivateChat(10001, 10001));
}

public class SystemClockTests
{
    [Fact]
    public void 返回UTC时间()
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

    [Fact]
    public void 按东八区渲染截止时间()
    {
        var utc = new DateTime(2026, 3, 14, 4, 30, 0, DateTimeKind.Utc);

        var message = _formatter.Format(utc);

        // UTC 04:30 → 东八区 12:30
        Assert.Equal("你已被禁言至 03-14 12:30，期间无法在群里发言", message);
    }

    [Fact]
    public void 跨日边界正确()
    {
        var utc = new DateTime(2026, 3, 14, 20, 0, 0, DateTimeKind.Utc);

        var message = _formatter.Format(utc);

        // UTC 20:00 → 东八区次日 04:00
        Assert.Contains("03-15 04:00", message, StringComparison.Ordinal);
    }

    [Fact]
    public void 数据库读出的Unspecified时间也按UTC处理()
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

    [Fact]
    public async Task 保存到根目录并返回公开地址()
    {
        var url = await Storage().SaveAsync(Upload("avatar.png"), string.Empty);

        Assert.StartsWith("/uploads/", url, StringComparison.Ordinal);
        Assert.EndsWith(".png", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 保存到子目录时地址含子目录()
    {
        var url = await Storage().SaveAsync(Upload("chat.jpg"), "images");

        Assert.StartsWith("/uploads/images/", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 文件实际写入磁盘且内容一致()
    {
        var url = await Storage().SaveAsync(Upload("a.png", "hello"), string.Empty);

        var fileName = url.Split('/')[^1];
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(_root, fileName)));
    }

    [Fact]
    public async Task 文件名用GUID重新生成_不使用客户端文件名()
    {
        // 直接用客户端文件名会带来路径穿越与互相覆盖的风险
        var url = await Storage().SaveAsync(Upload("../../etc/passwd.png"), string.Empty);

        Assert.DoesNotContain("..", url, StringComparison.Ordinal);
        Assert.DoesNotContain("passwd", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 同名文件不互相覆盖()
    {
        var storage = Storage();

        var first = await storage.SaveAsync(Upload("same.png", "first"), string.Empty);
        var second = await storage.SaveAsync(Upload("same.png", "second"), string.Empty);

        Assert.NotEqual(first, second);
        Assert.Equal(2, Directory.GetFiles(_root).Length);
    }

    [Fact]
    public async Task 子目录不存在时自动创建()
    {
        await Storage().SaveAsync(Upload("a.png"), "deep");

        Assert.True(Directory.Exists(Path.Combine(_root, "deep")));
    }

    [Fact]
    public async Task 返回的地址一律用正斜杠_不受平台路径分隔符影响()
    {
        var url = await Storage().SaveAsync(Upload("a.png"), "images");

        Assert.DoesNotContain('\\', url);
    }

    [Fact]
    public async Task 保留原始扩展名()
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

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".gif")]
    [InlineData(".webp")]
    public void 白名单扩展名通过(string ext)
    {
        UploadRules.EnsureValidImage(Upload($"a{ext}", 1024), UploadRules.MaxAvatarBytes, "2MB");
    }

    [Theory]
    [InlineData(".JPG")]
    [InlineData(".PNG")]
    public void 扩展名大小写不敏感(string ext)
    {
        UploadRules.EnsureValidImage(Upload($"a{ext}", 1024), UploadRules.MaxAvatarBytes, "2MB");
    }

    [Theory]
    [InlineData(".bmp")]
    [InlineData(".svg")]
    [InlineData(".exe")]
    [InlineData(".php")]
    [InlineData("")]
    public void 非白名单扩展名被拒(string ext)
    {
        var ex = Assert.Throws<Domain.Common.DomainException>(() => UploadRules.EnsureValidImage(
            Upload($"a{ext}", 1024), UploadRules.MaxAvatarBytes, "2MB"));

        Assert.Equal("仅支持 jpg / png / gif / webp 格式图片", ex.Message);
    }

    [Fact]
    public void 未选文件被拒()
    {
        var ex = Assert.Throws<Domain.Common.DomainException>(
            () => UploadRules.EnsureValidImage(null, UploadRules.MaxAvatarBytes, "2MB"));

        Assert.Equal("请选择图片文件", ex.Message);
    }

    [Fact]
    public void 空文件被拒()
    {
        var ex = Assert.Throws<Domain.Common.DomainException>(() => UploadRules.EnsureValidImage(
            Upload("a.png", 0), UploadRules.MaxAvatarBytes, "2MB"));

        Assert.Equal("请选择图片文件", ex.Message);
    }

    [Fact]
    public void 超出大小上限被拒_提示语带可读上限()
    {
        var ex = Assert.Throws<Domain.Common.DomainException>(() => UploadRules.EnsureValidImage(
            Upload("a.png", UploadRules.MaxAvatarBytes + 1), UploadRules.MaxAvatarBytes, "2MB"));

        Assert.Equal("图片大小不能超过 2MB", ex.Message);
    }

    [Fact]
    public void 恰好达到上限时通过()
    {
        UploadRules.EnsureValidImage(
            Upload("a.png", UploadRules.MaxAvatarBytes), UploadRules.MaxAvatarBytes, "2MB");
    }

    [Fact]
    public void 头像与聊天图片的上限不同()
    {
        Assert.Equal(2 * 1024 * 1024, UploadRules.MaxAvatarBytes);
        Assert.Equal(5 * 1024 * 1024, UploadRules.MaxChatImageBytes);
    }
}
