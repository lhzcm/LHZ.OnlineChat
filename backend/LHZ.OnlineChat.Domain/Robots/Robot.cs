using System.Net;
using System.Net.Sockets;
using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Robots;

/// <summary>
/// Webhook 回调地址值对象。空地址是合法状态 —— 表示「纯推送模式」：
/// 不接收消息回调，只由第三方通过 /api/robots/{令牌}/reply 主动推送。
/// </summary>
public sealed class WebhookUrl : ValueObject
{
    public const int MaxLength = 500;

    /// <summary>命中内网/本机地址时的统一提示</summary>
    public const string BlockedTargetMessage =
        "Webhook 地址不能指向内网或本机地址（自建服务确需回调内网时，请让管理员开启 Robot:AllowPrivateWebhookTargets）";

    private WebhookUrl(string value) => Value = value;

    public string Value { get; }

    /// <summary>空地址（纯推送模式）</summary>
    public static WebhookUrl None { get; } = new(string.Empty);

    public bool IsConfigured => Value.Length > 0;

    public static WebhookUrl Parse(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length == 0) return None;
        DomainException.Ensure(trimmed.Length <= MaxLength, "Webhook 地址过长");
        DomainException.Ensure(
            Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
            "Webhook 地址必须是 http/https 开头");
        return new WebhookUrl(trimmed);
    }

    /// <summary>
    /// 出站目标安全校验（SSRF 防护）。
    ///
    /// 只在**写入配置时**与**真正发起调用前**使用。<see cref="Parse"/> 故意不做这层校验：
    /// 它同时被读取用的 <see cref="Robot.Webhook"/> 视图调用，而库里的历史数据可能合法地
    /// 指向内网（自建部署的常见形态）—— 让读取路径抛异常会把「查一个机器人」变成 500。
    ///
    /// 为什么必须拦：这个地址是**服务端主动 POST** 的目标，而它由任意注册用户填写。
    /// 不拦的话，填 `http://169.254.169.254/...`（云元数据）或 `http://postgres:5432/`
    /// 就能让服务端替攻击者探测内网，`/test` 接口还会把响应内容回显给他。
    /// 自建 Webhook 确实需要内网地址时，由部署方用 `Robot:AllowPrivateWebhookTargets=true` 显式打开。
    /// </summary>
    public static void EnsureTargetAllowed(string? raw, bool allowPrivateTargets)
    {
        if (allowPrivateTargets) return;

        var trimmed = (raw ?? string.Empty).Trim();
        if (trimmed.Length == 0) return;

        DomainException.Ensure(trimmed.Length <= MaxLength, "Webhook 地址过长");

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new DomainException("Webhook 地址必须是 http/https 开头");
        }

        DomainException.Ensure(
            uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp,
            "Webhook 地址必须是 http/https 开头");

        // DnsSafeHost 对 IPv6 字面量会带方括号（[::1]），去掉才能被 IPAddress 解析
        var host = uri.DnsSafeHost.Trim('[', ']');

        if (IPAddress.TryParse(host, out var literal))
        {
            DomainException.Ensure(!IsBlockedAddress(literal), BlockedTargetMessage);
            return;
        }

        // 域名：单标签主机名（postgres / redis / mybot / 无后缀内网名）只在容器网络里可解析，
        // 公网可达的 Webhook 一定是带点的 FQDN；保留域名后缀是常见的内部命名习惯。
        DomainException.Ensure(host.Contains('.'), BlockedTargetMessage);
        DomainException.Ensure(!HasInternalSuffix(host), BlockedTargetMessage);
    }

    /// <summary>
    /// 是否属于「不允许服务端主动访问」的地址段：环回、私网、链路本地（含云元数据 169.254.169.254）、
    /// CGNAT、组播与保留段。两层共用同一份判定，避免写时与调用时的规则漂移。
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        // ::ffff:127.0.0.1 这类映射地址先还原成 IPv4，否则会绕过 IPv4 段判定
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal      // fe80::/10
                   || address.IsIPv6SiteLocal   // fec0::/10（已废弃，仍可能被用作内网别名）
                   || address.IsIPv6UniqueLocal // fc00::/7
                   || address.IsIPv6Multicast
                   || address.Equals(IPAddress.IPv6Any);
        }

        var b = address.GetAddressBytes();
        return b[0] switch
        {
            0 => true,                       // 0.0.0.0/8       本网络
            10 => true,                      // 10.0.0.0/8      私网
            100 => b[1] is >= 64 and <= 127, // 100.64.0.0/10   CGNAT
            127 => true,                     // 127.0.0.0/8     环回
            169 => b[1] == 254,              // 169.254.0.0/16  链路本地
            172 => b[1] is >= 16 and <= 31,  // 172.16.0.0/12   私网
            192 => b[1] == 168               // 192.168.0.0/16  私网
                   || (b[1] == 0 && b[2] == 0), // 192.0.0.0/24 协议分配
            198 => b[1] is 18 or 19,         // 198.18.0.0/15   基准测试
            >= 224 => true,                  // 组播 + 保留 + 广播
            _ => false
        };
    }

    private static bool HasInternalSuffix(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;

        string[] suffixes =
        {
            ".localhost", ".local", ".internal", ".lan", ".home.arpa", ".intranet", ".corp", ".test"
        };

        return suffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }

    public override string ToString() => Value;
}

/// <summary>
/// 机器人聚合根（原 RobotProfile）。
/// 机器人 = 本聚合（Webhook 配置）+ Users 上下文里一个 IsBot=true 的 User 账号。
/// </summary>
public sealed class Robot : AggregateRoot<long>
{
    public const int MaxNameLength = 50;
    public const int DefaultTimeoutMs = 10000;
    public const int MaxTimeoutMs = 60000;
    public const int MaxReplyLength = 5000;

    private Robot() { }

    /// <summary>机器人账号 ID（User.Id，IsBot=true）</summary>
    public int UserId { get; private set; }

    /// <summary>创建者账号 ID</summary>
    public int OwnerId { get; private set; }

    /// <summary>显示名（与 User.Nickname 同步）</summary>
    public string Name { get; private set; } = string.Empty;

    public string? Avatar { get; private set; }

    /// <summary>Webhook 回调地址（沿用既有表结构的 string 列）</summary>
    public string WebhookUrlValue { get; private set; } = string.Empty;

    /// <summary>回调地址视图</summary>
    public WebhookUrl Webhook => WebhookUrl.Parse(WebhookUrlValue);

    /// <summary>HMAC-SHA256 签名密钥；null 表示不验签（仅靠加密令牌鉴权）</summary>
    public string? WebhookSecret { get; private set; }

    /// <summary>同步回调超时（毫秒）</summary>
    public int TimeoutMs { get; private set; } = DefaultTimeoutMs;

    /// <summary>对外令牌（加密 ID，创建时生成后稳定不变）</summary>
    public string? Token { get; private set; }

    public bool Enabled { get; private set; } = true;

    /// <summary>主动推送累计次数</summary>
    public long PushCount { get; private set; }

    /// <summary>回调失败累计次数</summary>
    public long CallbackFailCount { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>是否强制验签：配置了密钥才验</summary>
    public bool SignatureRequired => !string.IsNullOrEmpty(WebhookSecret);

    /// <summary>是否会被消息触发（纯推送模式不触发回调）</summary>
    public bool RespondsToMessages => Enabled && Webhook.IsConfigured;

    // ==================== 工厂 ====================

    public static Robot Create(
        int ownerId,
        int botUserId,
        string name,
        string? avatar,
        WebhookUrl webhook,
        string? webhookSecret,
        int? timeoutMs,
        DateTime now)
        => new()
        {
            OwnerId = ownerId,
            UserId = botUserId,
            Name = NormalizeName(name),
            Avatar = avatar,
            WebhookUrlValue = webhook.Value,
            WebhookSecret = NormalizeSecret(webhookSecret),
            TimeoutMs = NormalizeTimeout(timeoutMs),
            Enabled = true,
            CreatedAt = now
        };

    /// <summary>持久化拿到主键后绑定对外令牌（令牌由 ID 加密而来）</summary>
    public void AssignToken(string token)
    {
        DomainException.Ensure(!string.IsNullOrWhiteSpace(token), "机器人令牌不能为空");
        Token = token;
    }

    // ==================== 领域行为 ====================

    /// <summary>局部更新配置：null 表示该项不变。返回名称是否发生变化（需同步 User.Nickname）</summary>
    public bool UpdateConfiguration(
        string? name,
        string? avatar,
        string? webhookUrl,
        string? webhookSecret,
        int? timeoutMs,
        bool? enabled)
    {
        var nameChanged = false;

        if (name is not null)
        {
            Name = NormalizeName(name);
            nameChanged = true;
        }

        if (webhookUrl is not null) WebhookUrlValue = WebhookUrl.Parse(webhookUrl).Value;
        if (avatar is not null) Avatar = avatar;
        if (webhookSecret is not null) WebhookSecret = NormalizeSecret(webhookSecret);
        if (timeoutMs is > 0 and <= MaxTimeoutMs) TimeoutMs = timeoutMs.Value;
        if (enabled.HasValue) Enabled = enabled.Value;

        return nameChanged;
    }

    /// <summary>仅创建者可管理</summary>
    public void EnsureOwnedBy(int ownerId)
    {
        if (OwnerId != ownerId) throw new EntityNotFoundException("机器人不存在");
    }

    public void SetEnabled(bool enabled) => Enabled = enabled;

    /// <summary>测试触发前置校验</summary>
    public void EnsureTestable()
    {
        DomainException.Ensure(Webhook.IsConfigured,
            "该机器人未配置 Webhook 地址，仅支持第三方主动推送");
    }

    /// <summary>校验第三方推送的回复内容</summary>
    public static void EnsureReplyContentValid(string? content)
    {
        DomainException.Ensure(!string.IsNullOrWhiteSpace(content), "回复内容不能为空");
        DomainException.Ensure(content!.Length <= MaxReplyLength, "回复内容过长");
    }

    /// <summary>推送埋点</summary>
    public void RecordPush() => PushCount++;

    /// <summary>回调失败埋点</summary>
    public void RecordCallbackFailure() => CallbackFailCount++;

    // ==================== 归一化 ====================

    /// <summary>
    /// 机器人名称的归一化与校验。
    /// 公开是因为创建流程要先建机器人账号（User）再建配置，
    /// 而账号那边校验的是「昵称」规则、提示语也是「昵称不能为空」——
    /// 对正在创建机器人的用户来说是错的语境。调用方须先用这里校验一次。
    /// </summary>
    public static string NormalizeName(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        DomainException.Ensure(trimmed.Length > 0, "机器人名称不能为空");
        DomainException.Ensure(trimmed.Length <= MaxNameLength, $"机器人名称不能超过 {MaxNameLength} 个字符");
        return trimmed;
    }

    private static string? NormalizeSecret(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static int NormalizeTimeout(int? raw)
        => raw is > 0 and <= MaxTimeoutMs ? raw.Value : DefaultTimeoutMs;
}
