using System.Text;
using LHZ.FastJson;
using LHZ.FastJson.Json.Attributes;
using LHZ.OnlineChat.Application.Abstractions;
using LHZ.OnlineChat.Domain.Messaging;
using LHZ.OnlineChat.Domain.Robots;
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

    /// <summary>HttpClient 命名</summary>
    internal const string HttpClientName = "bot";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebhookSigner _signer;
    private readonly ILogger<WebhookDispatcher> _logger;

    public WebhookDispatcher(
        IHttpClientFactory httpClientFactory,
        IWebhookSigner signer,
        ILogger<WebhookDispatcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _signer = signer;
        _logger = logger;
    }

    public async Task<WebhookDispatchResult> DispatchAsync(
        Robot robot, WebhookEvent payload, CancellationToken ct = default)
    {
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

                var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
