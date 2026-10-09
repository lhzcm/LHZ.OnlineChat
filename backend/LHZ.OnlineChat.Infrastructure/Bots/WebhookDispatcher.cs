using System.Net;
using System.Net.Sockets;
using System.Text;
using LHZ.FastJson;
using LHZ.FastJson.Json.Attributes;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
using LHZ.OnlineChat.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace LHZ.OnlineChat.Infrastructure.Bots;

/// <summary>
/// 机器人 Webhook 调度：POST 事件 + HMAC 签名 + 超时 + 失败重试 1 次。
/// </summary>
internal sealed class WebhookDispatcher : IWebhookDispatcher
{
    /// <summary>签名请求头</summary>
    internal const string SignatureHeader = "X-Bot-Signature";

    /// <summary>总尝试次数（首次 + 重试 1 次）</summary>
    private const int MaxAttempts = 2;

    /// <summary>
    /// 读取响应体的大小上限。对端可能是任意地址，不设上限时一个超大响应就能把内存拉满；
    /// 正常用法只需要 {"content":"..."}，截断不会影响它。
    /// </summary>
    private const int MaxResponseBytes = 64 * 1024;

    /// <summary>HttpClient 命名</summary>
    internal const string HttpClientName = "bot";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebhookSigner _signer;
    private readonly IWebhookTargetPolicy _targets;
    private readonly ILogger<WebhookDispatcher> _logger;

    public WebhookDispatcher(
        IHttpClientFactory httpClientFactory,
        IWebhookSigner signer,
        IWebhookTargetPolicy targets,
        ILogger<WebhookDispatcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _signer = signer;
        _targets = targets;
        _logger = logger;
    }

    public async Task<WebhookDispatchResult> DispatchAsync(
        Robot robot, WebhookEvent payload, CancellationToken ct = default)
    {
        // 发起调用前的最后一道 SSRF 闸门：见 DescribeBlockedTargetAsync 的说明
        var blocked = await DescribeBlockedTargetAsync(robot.WebhookUrlValue, ct).ConfigureAwait(false);
        if (blocked is not null)
        {
            _logger.LogWarning(
                "Webhook 目标被拒绝（机器人 {RobotId}）：{Reason}", robot.Id, blocked);
            return new WebhookDispatchResult { Success = false, Message = blocked };
        }

        var body = JsonConvert.Serialize(WebhookPayload.Create(payload));
        var signature = robot.WebhookSecret is null ? null : _signer.Sign(robot.WebhookSecret, body);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, robot.WebhookUrlValue)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                if (signature is not null)
                    request.Headers.TryAddWithoutValidation(SignatureHeader, signature);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(robot.TimeoutMs);

                using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Webhook 返回 {StatusCode}（第 {Attempt} 次，机器人 {RobotId}）",
                        (int)response.StatusCode, attempt, robot.Id);
                    continue;
                }

                var responseBody = await ReadBodyLimitedAsync(response.Content, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(responseBody))
                    return new WebhookDispatchResult { Success = true };

                var parsed = JsonConvert.Deserialize<WebhookReply>(responseBody);
                var content = parsed?.Content?.Trim();

                return new WebhookDispatchResult
                {
                    Success = true,
                    Reply = string.IsNullOrEmpty(content) ? null : content
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex, "Webhook 调用失败（第 {Attempt} 次，机器人 {RobotId}）", attempt, robot.Id);
            }
        }

        return new WebhookDispatchResult
        {
            Success = false,
            Message = "Webhook 调用失败（已重试 1 次）"
        };
    }

    /// <summary>
    /// 目标是否指向内网/本机（返回拒绝原因，放行返回 null）。
    ///
    /// 写配置时已经按字符串校验过一次，这里必须再查一次，因为「域名可以解析到内网」：
    /// `http://evil.example.com` 完全可以把 A 记录指向 127.0.0.1 或 10.x。
    /// 同时它也兜住了本次修复之前写进库里的历史内网地址。
    ///
    /// 注意这仍有 TOCTOU 窗口（校验用一次解析、HttpClient 连接时再解析一次）——
    /// 彻底堵住需要固定 IP 连接，代价与收益不成比例；重定向已关闭，
    /// 常见的内网探测路径已经被这条规则挡住。
    /// </summary>
    private async Task<string?> DescribeBlockedTargetAsync(string url, CancellationToken ct)
    {
        if (_targets.AllowPrivateTargets) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "Webhook 地址非法";

        var host = uri.DnsSafeHost.Trim('[', ']');
        if (IPAddress.TryParse(host, out var literal))
        {
            return WebhookUrl.IsBlockedAddress(literal) ? WebhookUrl.BlockedTargetMessage : null;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            return addresses.Any(WebhookUrl.IsBlockedAddress) ? WebhookUrl.BlockedTargetMessage : null;
        }
        catch (SocketException)
        {
            // 解析不了就交给 HttpClient 去失败：那条路径已经有日志与重试，不在这里重复拦
            return null;
        }
        catch (ArgumentException)
        {
            return "Webhook 地址非法";
        }
    }

    /// <summary>读取响应体，超过上限即截断（见 <see cref="MaxResponseBytes"/> 的说明）</summary>
    private static async Task<string> ReadBodyLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var buffer = new byte[MaxResponseBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct)
                .ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }
}

/// <summary>Webhook 出站目标策略：直接读机器人配置里的开关</summary>
internal sealed class RobotWebhookTargetPolicy : IWebhookTargetPolicy
{
    public RobotWebhookTargetPolicy(RobotOptions options) => AllowPrivateTargets = options.AllowPrivateWebhookTargets;

    public bool AllowPrivateTargets { get; }
}

// ==================== 出站 JSON 契约（camelCase，对第三方公开，不可随意更名） ====================

internal sealed class WebhookPayload
{
    [JsonProperty("event")]
    public string Event { get; set; } = "message";

    [JsonProperty("robot")]
    public WebhookActorPayload Robot { get; set; } = new();

    [JsonProperty("session")]
    public WebhookSessionPayload Session { get; set; } = new();

    [JsonProperty("from")]
    public WebhookActorPayload From { get; set; } = new();

    [JsonProperty("message")]
    public WebhookMessagePayload Message { get; set; } = new();

    [JsonProperty("mentions")]
    public List<int> Mentions { get; set; } = new();

    internal static WebhookPayload Create(WebhookEvent source) => new()
    {
        Robot = WebhookActorPayload.Create(source.Robot),
        Session = new WebhookSessionPayload
        {
            Type = source.Session.Type.ToStorage(),
            Id = source.Session.Id,
            Name = source.Session.Name
        },
        From = WebhookActorPayload.Create(source.From),
        Message = new WebhookMessagePayload
        {
            MessageId = source.Message.MessageId,
            Content = source.Message.Content,
            MessageType = (int)source.Message.Kind,
            Timestamp = source.Message.Timestamp
        },
        Mentions = source.Mentions.ToList()
    };
}

internal sealed class WebhookActorPayload
{
    [JsonProperty("userId")]
    public int UserId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("avatar")]
    public string? Avatar { get; set; }

    [JsonProperty("isBot")]
    public bool IsBot { get; set; }

    internal static WebhookActorPayload Create(WebhookActor actor) => new()
    {
        UserId = actor.UserId,
        Name = actor.Name,
        Avatar = actor.Avatar,
        IsBot = actor.IsBot
    };
}

internal sealed class WebhookSessionPayload
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
}

internal sealed class WebhookMessagePayload
{
    [JsonProperty("messageId")]
    public string MessageId { get; set; } = string.Empty;

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    [JsonProperty("messageType")]
    public int MessageType { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }
}

/// <summary>Webhook 同步响应：200 + {"content":"回复文本"}</summary>
internal sealed class WebhookReply
{
    [JsonProperty("content")]
    public string? Content { get; set; }
}

/// <summary>第三方主动推送的请求体（入站，camelCase）</summary>
internal sealed class RobotReplyRequestPayload
{
    [JsonProperty("sessionType")]
    public string SessionType { get; set; } = "private";

    [JsonProperty("sessionId")]
    public long SessionId { get; set; }

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;

    [JsonProperty("replyTo")]
    public string? ReplyTo { get; set; }
}
