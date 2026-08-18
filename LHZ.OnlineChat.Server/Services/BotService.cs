using LHZ.FastJson;
using LHZ.OnlineChat.Server.Models.DTOs;
using LHZ.OnlineChat.Server.Models.Entities;
using LHZ.WebSocket.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// 机器人服务
/// 机器人 = User(IsBot=true) + RobotProfile(Webhook 配置)
/// 收到消息 → POST Webhook 事件（HMAC 签名）→ 同步响应 200 {"content":...} 或调用 /api/robots/{id}/reply 异步回复
/// </summary>
public class BotService
{
    private readonly IFreeSql _fsql;
    private readonly RedisService _redis;
    private readonly WsConnectionManager _connectionManager;
    private readonly IHttpClientFactory _httpClientFactory;

    public const string SignatureHeader = "X-Bot-Signature";

    public BotService(IFreeSql fsql, RedisService redis, WsConnectionManager connectionManager, IHttpClientFactory httpClientFactory)
    {
        _fsql = fsql;
        _redis = redis;
        _connectionManager = connectionManager;
        _httpClientFactory = httpClientFactory;
    }

    // ==================== 机器人管理 ====================

    /// <summary>
    /// 创建机器人：生成机器人账号（IsBot）+ 配置 + 与创建者建立好友关系
    /// </summary>
    public async Task<ApiResponse<RobotInfo>> CreateRobotAsync(int ownerId, CreateRobotRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return ApiResponse<RobotInfo>.Fail("机器人名称不能为空");
        if (name.Length > 50)
            return ApiResponse<RobotInfo>.Fail("机器人名称不能超过 50 个字符");

        var webhookUrl = (request.WebhookUrl ?? string.Empty).Trim();
        if (!IsValidWebhookUrl(webhookUrl))
            return ApiResponse<RobotInfo>.Fail("Webhook 地址必须是 http/https 开头");
        if (webhookUrl.Length > 500)
            return ApiResponse<RobotInfo>.Fail("Webhook 地址过长");

        var timeout = request.TimeoutMs is > 0 and <= 60000 ? request.TimeoutMs.Value : 10000;

        // 创建机器人账号
        var botUser = new User
        {
            Email = null,
            PasswordHash = null,
            Nickname = name,
            Avatar = request.Avatar,
            IsBot = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var botUserId = (int)await _fsql.Insert(botUser).ExecuteIdentityAsync();

        var profile = new RobotProfile
        {
            UserId = botUserId,
            OwnerId = ownerId,
            Name = name,
            Avatar = request.Avatar,
            WebhookUrl = webhookUrl,
            WebhookSecret = string.IsNullOrWhiteSpace(request.WebhookSecret) ? null : request.WebhookSecret.Trim(),
            TimeoutMs = timeout,
            Enabled = true,
            CreatedAt = DateTime.UtcNow
        };
        var id = await _fsql.Insert(profile).ExecuteIdentityAsync();

        // 与创建者建立双向好友关系（一条记录，查询双向匹配）
        await _fsql.Insert(new Friend
        {
            UserId = ownerId,
            FriendId = botUserId,
            Status = 1,
            CreatedAt = DateTime.UtcNow
        }).ExecuteAffrowsAsync();

        return ApiResponse<RobotInfo>.Ok(new RobotInfo
        {
            Id = id,
            UserId = botUserId,
            Name = name,
            Avatar = request.Avatar,
            WebhookUrl = webhookUrl,
            WebhookSecret = profile.WebhookSecret,
            TimeoutMs = timeout,
            Enabled = true,
            CreatedAt = profile.CreatedAt
        }, "机器人创建成功");
    }

    /// <summary>
    /// 获取我的机器人列表
    /// </summary>
    public async Task<ApiResponse<List<RobotInfo>>> GetMyRobotsAsync(int ownerId)
    {
        var profiles = await _fsql.Select<RobotProfile>()
            .Where(p => p.OwnerId == ownerId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var result = profiles.Select(p => new RobotInfo
        {
            Id = p.Id,
            UserId = p.UserId,
            Name = p.Name,
            Avatar = p.Avatar,
            WebhookUrl = p.WebhookUrl,
            WebhookSecret = p.WebhookSecret,
            TimeoutMs = p.TimeoutMs,
            Enabled = p.Enabled,
            CreatedAt = p.CreatedAt
        }).ToList();

        return ApiResponse<List<RobotInfo>>.Ok(result);
    }

    /// <summary>
    /// 更新机器人配置（仅创建者）
    /// </summary>
    public async Task<ApiResponse<RobotInfo>> UpdateRobotAsync(int ownerId, long robotId, UpdateRobotRequest request)
    {
        var profile = await _fsql.Select<RobotProfile>()
            .Where(p => p.Id == robotId && p.OwnerId == ownerId)
            .FirstAsync();
        if (profile == null)
            return ApiResponse<RobotInfo>.Fail("机器人不存在");

        if (request.Name != null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0) return ApiResponse<RobotInfo>.Fail("机器人名称不能为空");
            if (name.Length > 50) return ApiResponse<RobotInfo>.Fail("机器人名称不能超过 50 个字符");
            profile.Name = name;
        }

        if (request.WebhookUrl != null)
        {
            var url = request.WebhookUrl.Trim();
            if (!IsValidWebhookUrl(url)) return ApiResponse<RobotInfo>.Fail("Webhook 地址必须是 http/https 开头");
            profile.WebhookUrl = url;
        }

        if (request.Avatar != null) profile.Avatar = request.Avatar;
        if (request.WebhookSecret != null) profile.WebhookSecret = request.WebhookSecret.Trim().Length == 0 ? null : request.WebhookSecret.Trim();
        if (request.TimeoutMs is > 0 and <= 60000) profile.TimeoutMs = request.TimeoutMs.Value;
        if (request.Enabled.HasValue) profile.Enabled = request.Enabled.Value;

        await _fsql.Update<RobotProfile>().SetSource(profile).ExecuteAffrowsAsync();

        // 同步机器人账号昵称
        if (request.Name != null)
        {
            await _fsql.Update<User>()
                .Set(u => u.Nickname, profile.Name)
                .Set(u => u.UpdatedAt, DateTime.UtcNow)
                .Where(u => u.Id == profile.UserId)
                .ExecuteAffrowsAsync();
        }

        return ApiResponse<RobotInfo>.Ok(new RobotInfo
        {
            Id = profile.Id,
            UserId = profile.UserId,
            Name = profile.Name,
            Avatar = profile.Avatar,
            WebhookUrl = profile.WebhookUrl,
            WebhookSecret = profile.WebhookSecret,
            TimeoutMs = profile.TimeoutMs,
            Enabled = profile.Enabled,
            CreatedAt = profile.CreatedAt
        }, "已保存");
    }

    /// <summary>
    /// 删除机器人（清理账号、好友关系、群成员）
    /// </summary>
    public async Task<ApiResponse> DeleteRobotAsync(int ownerId, long robotId)
    {
        var profile = await _fsql.Select<RobotProfile>()
            .Where(p => p.Id == robotId && p.OwnerId == ownerId)
            .FirstAsync();
        if (profile == null)
            return ApiResponse.Fail("机器人不存在");

        await _fsql.Delete<RobotProfile>().Where(p => p.Id == robotId).ExecuteAffrowsAsync();
        await _fsql.Delete<Friend>()
            .Where(f => f.UserId == profile.UserId || f.FriendId == profile.UserId)
            .ExecuteAffrowsAsync();
        await _fsql.Delete<GroupMember>()
            .Where(m => m.UserId == profile.UserId)
            .ExecuteAffrowsAsync();
        await _fsql.Delete<User>().Where(u => u.Id == profile.UserId).ExecuteAffrowsAsync();

        return ApiResponse.Ok("机器人已删除");
    }

    // ==================== 群机器人管理 ====================

    /// <summary>
    /// 添加机器人到群（群主/管理员，只能添加自己的机器人）
    /// </summary>
    public async Task<ApiResponse> AddGroupRobotAsync(long groupId, int operatorId, int robotUserId)
    {
        var operatorMember = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == operatorId)
            .FirstAsync();
        if (operatorMember == null)
            return ApiResponse.Fail("你不是该群组成员");
        if (operatorMember.Role > 1)
            return ApiResponse.Fail("只有群主或管理员可以添加机器人");

        var profile = await _fsql.Select<RobotProfile>()
            .Where(p => p.UserId == robotUserId && p.OwnerId == operatorId)
            .FirstAsync();
        if (profile == null)
            return ApiResponse.Fail("只能添加自己创建的机器人");

        var exists = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == robotUserId)
            .AnyAsync();
        if (exists)
            return ApiResponse.Fail("该机器人已在群中");

        // 已读游标 = 当前最新消息，避免把入群前的历史当作离线消息补发给机器人
        var maxId = await _fsql.Select<GroupMessage>()
            .Where(gm => gm.GroupId == groupId)
            .MaxAsync(gm => gm.Id);

        await _fsql.Insert(new GroupMember
        {
            GroupId = groupId,
            UserId = robotUserId,
            Role = 2,
            LastReadMessageId = maxId,
            JoinedAt = DateTime.UtcNow
        }).ExecuteAffrowsAsync();

        return ApiResponse.Ok($"已添加机器人「{profile.Name}」");
    }

    /// <summary>
    /// 从群移除机器人（群主/管理员）
    /// </summary>
    public async Task<ApiResponse> RemoveGroupRobotAsync(long groupId, int operatorId, int robotUserId)
    {
        var operatorMember = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == operatorId)
            .FirstAsync();
        if (operatorMember == null)
            return ApiResponse.Fail("你不是该群组成员");
        if (operatorMember.Role > 1)
            return ApiResponse.Fail("只有群主或管理员可以移除机器人");

        var affected = await _fsql.Delete<GroupMember>()
            .Where(m => m.GroupId == groupId && m.UserId == robotUserId)
            .ExecuteAffrowsAsync();

        return affected > 0 ? ApiResponse.Ok("已移除机器人") : ApiResponse.Fail("该机器人不在群中");
    }

    /// <summary>
    /// 获取群内机器人列表
    /// </summary>
    public async Task<ApiResponse<List<RobotInfo>>> GetGroupRobotsAsync(long groupId)
    {
        var robotMembers = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId)
            .ToListAsync();
        var robotIds = robotMembers.Select(m => m.UserId).ToList();
        if (robotIds.Count == 0)
            return ApiResponse<List<RobotInfo>>.Ok(new List<RobotInfo>());

        var profiles = await _fsql.Select<RobotProfile>()
            .Where(p => robotIds.Contains(p.UserId))
            .ToListAsync();

        var result = profiles.Select(p => new RobotInfo
        {
            Id = p.Id,
            UserId = p.UserId,
            Name = p.Name,
            Avatar = p.Avatar,
            WebhookUrl = p.WebhookUrl,
            WebhookSecret = p.WebhookSecret,
            TimeoutMs = p.TimeoutMs,
            Enabled = p.Enabled,
            CreatedAt = p.CreatedAt
        }).ToList();

        return ApiResponse<List<RobotInfo>>.Ok(result);
    }

    // ==================== 触发（由 WsMessageHandler 调用） ====================

    /// <summary>
    /// 私聊触发：接收者是启用中的机器人且发送者不是机器人 → 异步调度 Webhook
    /// </summary>
    public async Task TryDispatchPrivateAsync(int senderId, int receiverId, WsMessage message)
    {
        try
        {
            var profile = await _fsql.Select<RobotProfile>()
                .Where(p => p.UserId == receiverId && p.Enabled)
                .FirstAsync();
            if (profile == null) return;

            // 机器人之间互不触发（防死循环）
            var sender = await _fsql.Select<User>().Where(u => u.Id == senderId).FirstAsync();
            if (sender?.IsBot == true) return;

            var senderName = sender?.Nickname ?? "未知";
            var receiverName = profile.Name;

            var evt = new BotWebhookEvent
            {
                Robot = new BotWebhookActor { UserId = receiverId, Name = receiverName, Avatar = profile.Avatar, IsBot = true },
                Session = new BotWebhookSession { Type = "private", Id = senderId, Name = senderName },
                From = new BotWebhookActor { UserId = senderId, Name = senderName, Avatar = sender?.Avatar },
                Message = new BotWebhookMessage
                {
                    MessageId = message.MessageId,
                    Content = message.Content,
                    MessageType = message.MessageType,
                    Timestamp = message.Timestamp
                }
            };

            Console.WriteLine($"[BOT] 私聊触发: {senderId} → 机器人 {receiverId}");
            _ = Task.Run(() => DispatchAndReplyAsync(profile, evt, reply =>
                SendPrivateReplyAsync(profile.UserId, senderId, reply.Content, evt.Message.MessageId, evt.Message.Content, senderName)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] 私聊触发失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 群聊触发：消息 @ 了群内启用中的机器人且发送者不是机器人 → 异步调度 Webhook
    /// </summary>
    public async Task TryDispatchGroupAsync(int senderId, long groupId, WsMessage message)
    {
        try
        {
            if (message.Mentions == null || message.Mentions.Count == 0) return;

            var group = await _fsql.Select<Group_>().Where(g => g.Id == groupId).FirstAsync();
            if (group == null) return;

            var robotIds = message.Mentions.ToList();
            var profiles = await _fsql.Select<RobotProfile>()
                .Where(p => robotIds.Contains(p.UserId) && p.Enabled)
                .ToListAsync();
            if (profiles.Count == 0) return;

            // 机器人之间互不触发（防死循环）
            var sender = await _fsql.Select<User>().Where(u => u.Id == senderId).FirstAsync();
            if (sender?.IsBot == true) return;

            var senderName = sender?.Nickname ?? "未知";

            foreach (var profile in profiles)
            {
                var evt = new BotWebhookEvent
                {
                    Robot = new BotWebhookActor { UserId = profile.UserId, Name = profile.Name, Avatar = profile.Avatar, IsBot = true },
                    Session = new BotWebhookSession { Type = "group", Id = groupId, Name = group.Name },
                    From = new BotWebhookActor { UserId = senderId, Name = senderName, Avatar = sender?.Avatar },
                    Message = new BotWebhookMessage
                    {
                        MessageId = message.MessageId,
                        Content = message.Content,
                        MessageType = message.MessageType,
                        Timestamp = message.Timestamp
                    },
                    Mentions = message.Mentions
                };

                Console.WriteLine($"[BOT] 群触发: {senderId} → 机器人 {profile.UserId} @群 {groupId}");
                _ = Task.Run(() => DispatchAndReplyAsync(profile, evt, reply =>
                    SendGroupReplyAsync(profile.UserId, groupId, reply.Content, evt.Message.MessageId, evt.Message.Content, senderName)));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] 群触发失败: {ex.Message}");
        }
    }

    // ==================== Webhook 调度 ====================

    /// <summary>
    /// POST 事件到 Webhook，解析同步响应并发送机器人回复；失败重试 1 次
    /// </summary>
    public async Task<RobotTestResult> DispatchAsync(RobotProfile profile, BotWebhookEvent evt)
    {
        var body = JsonConvert.Serialize(evt);
        var secret = profile.WebhookSecret;
        var signature = secret == null ? null : ComputeHmac(secret, body);
        var client = _httpClientFactory.CreateClient("bot");

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, profile.WebhookUrl);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                if (signature != null)
                    req.Headers.TryAddWithoutValidation(SignatureHeader, signature);

                using var cts = new CancellationTokenSource(profile.TimeoutMs);
                using var resp = await client.SendAsync(req, cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[BOT] Webhook 返回 {(int)resp.StatusCode} (第 {attempt} 次)");
                    continue;
                }

                var respBody = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(respBody)) return new RobotTestResult { Success = true };

                var parsed = JsonConvert.Deserialize<BotWebhookResponse>(respBody);
                var content = parsed?.Content?.Trim();
                if (string.IsNullOrEmpty(content))
                    return new RobotTestResult { Success = true };

                return new RobotTestResult { Success = true, Reply = content };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BOT] Webhook 调用失败 (第 {attempt} 次): {ex.Message}");
            }
        }

        return new RobotTestResult { Success = false, Message = "Webhook 调用失败（已重试 1 次）" };
    }

    /// <summary>
    /// 调度 + 自动回复（同步响应）
    /// </summary>
    private async Task DispatchAndReplyAsync(RobotProfile profile, BotWebhookEvent evt, Action<BotWebhookResponse> sendReply)
    {
        try
        {
            var result = await DispatchAsync(profile, evt);
            if (result.Success && !string.IsNullOrEmpty(result.Reply))
            {
                sendReply(new BotWebhookResponse { Content = result.Reply });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOT] 调度失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 异步回复：验签通过后以机器人身份发送消息
    /// </summary>
    public async Task<ApiResponse> HandleAsyncReplyAsync(long robotId, string rawBody, string? signature)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return ApiResponse.Fail("请求体不能为空");

        var profile = await _fsql.Select<RobotProfile>().Where(p => p.Id == robotId).FirstAsync();
        if (profile == null)
            return ApiResponse.Fail("机器人不存在");
        if (string.IsNullOrEmpty(profile.WebhookSecret))
            return ApiResponse.Fail("该机器人未配置签名密钥，无法异步回复");

        // 验签：X-Bot-Signature = HMAC-SHA256(secret, rawBody) 十六进制
        var expected = ComputeHmac(profile.WebhookSecret, rawBody);
        if (string.IsNullOrEmpty(signature) || !FixedTimeEquals(signature, expected))
            return ApiResponse.Fail("签名验证失败");

        var request = JsonConvert.Deserialize<BotReplyRequest>(rawBody);
        if (request == null)
            return ApiResponse.Fail("请求格式错误");
        if (string.IsNullOrWhiteSpace(request.Content))
            return ApiResponse.Fail("回复内容不能为空");
        if (request.Content.Length > 5000)
            return ApiResponse.Fail("回复内容过长");

        if (request.SessionType == "private")
        {
            var targetId = (int)request.SessionId;
            // 必须是好友关系，防止任意用户被机器人私聊骚扰
            var isFriend = await _fsql.Select<Friend>()
                .Where(f => f.Status == 1 &&
                    ((f.UserId == targetId && f.FriendId == profile.UserId) ||
                     (f.UserId == profile.UserId && f.FriendId == targetId)))
                .AnyAsync();
            if (!isFriend)
                return ApiResponse.Fail("目标用户与机器人不是好友关系");

            await SendPrivateReplyAsync(profile.UserId, targetId, request.Content, request.ReplyTo);
        }
        else if (request.SessionType == "group")
        {
            var isMember = await _fsql.Select<GroupMember>()
                .Where(m => m.GroupId == request.SessionId && m.UserId == profile.UserId)
                .AnyAsync();
            if (!isMember)
                return ApiResponse.Fail("机器人不在该群中");

            await SendGroupReplyAsync(profile.UserId, request.SessionId, request.Content, request.ReplyTo);
        }
        else
        {
            return ApiResponse.Fail("无效的会话类型");
        }

        return ApiResponse.Ok("已发送");
    }

    /// <summary>
    /// 测试触发：以创建者为发送者构造私聊事件
    /// </summary>
    public async Task<ApiResponse<RobotTestResult>> TestRobotAsync(int ownerId, long robotId, string content)
    {
        var profile = await _fsql.Select<RobotProfile>()
            .Where(p => p.Id == robotId && p.OwnerId == ownerId)
            .FirstAsync();
        if (profile == null)
            return ApiResponse<RobotTestResult>.Fail("机器人不存在");

        var owner = await _fsql.Select<User>().Where(u => u.Id == ownerId).FirstAsync();

        var evt = new BotWebhookEvent
        {
            Robot = new BotWebhookActor { UserId = profile.UserId, Name = profile.Name, Avatar = profile.Avatar, IsBot = true },
            Session = new BotWebhookSession { Type = "private", Id = ownerId, Name = owner?.Nickname ?? "我" },
            From = new BotWebhookActor { UserId = ownerId, Name = owner?.Nickname ?? "我", Avatar = owner?.Avatar },
            Message = new BotWebhookMessage
            {
                MessageId = Guid.NewGuid().ToString("N"),
                Content = string.IsNullOrWhiteSpace(content) ? "你好" : content,
                MessageType = 0,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }
        };

        var result = await DispatchAsync(profile, evt);
        return ApiResponse<RobotTestResult>.Ok(result, result.Success ? "测试完成" : result.Message);
    }

    // ==================== 机器人回复（复用消息通道：落库 + 缓存 + 广播） ====================

    /// <summary>
    /// 机器人回复私聊：以机器人身份发送消息给 targetId
    /// </summary>
    public async Task SendPrivateReplyAsync(int botUserId, int targetId, string content, string? replyTo = null, string? replyContent = null, string? replySender = null)
    {
        var msg = new WsMessage
        {
            Type = WsMessageType.PrivateMessage,
            From = botUserId.ToString(),
            To = targetId.ToString(),
            Content = content,
            MessageType = 0,
            SenderName = string.Empty,
            SenderAvatar = null,
            ReplyTo = replyTo ?? string.Empty,
            ReplyContent = replyContent ?? string.Empty,
            ReplySender = replySender ?? string.Empty
        };

        var privateMsg = new PrivateMessage
        {
            SenderId = botUserId,
            ReceiverId = targetId,
            Content = content,
            MessageType = 0,
            IsRead = false,
            ClientMessageId = null,
            ReplyMessageId = string.IsNullOrEmpty(replyTo) ? null : replyTo,
            ReplyContent = string.IsNullOrEmpty(replyContent) ? null : replyContent,
            ReplySenderName = string.IsNullOrEmpty(replySender) ? null : replySender,
            SentAt = DateTime.UtcNow
        };
        var msgId = await _fsql.Insert(privateMsg).ExecuteIdentityAsync();
        msg.MessageId = msgId.ToString();
        msg.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var botUser = await _fsql.Select<User>().Where(u => u.Id == botUserId).FirstAsync();
        msg.SenderName = botUser?.Nickname ?? profileFallbackName(botUserId);
        msg.SenderAvatar = botUser?.Avatar;

        var cacheKey = GetPrivateChatCacheKey(botUserId, targetId);
        await _redis.ListLeftPushAsync(cacheKey, JsonConvert.Serialize(msg));
        await _redis.ListTrimAsync(cacheKey, 0, 49);

        var client = _connectionManager.GetConnection(targetId);
        if (client != null && client.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
        {
            client.SendMessage(JsonConvert.Serialize(msg));
            Console.WriteLine($"[BOT] 机器人 {botUserId} → {targetId} (已送达)");
        }
        else
        {
            Console.WriteLine($"[BOT] 机器人 {botUserId} → {targetId} (离线，已存库)");
        }
    }

    /// <summary>
    /// 机器人回复群聊：以机器人身份发送消息到群
    /// </summary>
    public async Task SendGroupReplyAsync(int botUserId, long groupId, string content, string? replyTo = null, string? replyContent = null, string? replySender = null)
    {
        var msg = new WsMessage
        {
            Type = WsMessageType.GroupMessage,
            From = botUserId.ToString(),
            To = groupId.ToString(),
            Content = content,
            MessageType = 0,
            SenderName = string.Empty,
            SenderAvatar = null,
            Mentions = new List<int>(),
            ReplyTo = replyTo ?? string.Empty,
            ReplyContent = replyContent ?? string.Empty,
            ReplySender = replySender ?? string.Empty
        };

        var groupMsg = new GroupMessage
        {
            GroupId = groupId,
            SenderId = botUserId,
            Content = content,
            MessageType = 0,
            ClientMessageId = null,
            Mentions = null,
            ReplyMessageId = string.IsNullOrEmpty(replyTo) ? null : replyTo,
            ReplyContent = string.IsNullOrEmpty(replyContent) ? null : replyContent,
            ReplySenderName = string.IsNullOrEmpty(replySender) ? null : replySender,
            SentAt = DateTime.UtcNow
        };
        var msgId = await _fsql.Insert(groupMsg).ExecuteIdentityAsync();
        msg.MessageId = msgId.ToString();
        msg.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var botUser = await _fsql.Select<User>().Where(u => u.Id == botUserId).FirstAsync();
        msg.SenderName = botUser?.Nickname ?? profileFallbackName(botUserId);
        msg.SenderAvatar = botUser?.Avatar;

        var cacheKey = $"chat:group:{groupId}";
        await _redis.ListLeftPushAsync(cacheKey, JsonConvert.Serialize(msg));
        await _redis.ListTrimAsync(cacheKey, 0, 49);

        var members = await _fsql.Select<GroupMember>()
            .Where(m => m.GroupId == groupId)
            .ToListAsync();

        foreach (var member in members)
        {
            var client = _connectionManager.GetConnection(member.UserId);
            if (client != null && client.Status == LHZ.WebSocket.Enums.ClientStatus.Opend)
            {
                client.SendMessage(JsonConvert.Serialize(msg));
            }
        }
        Console.WriteLine($"[BOT] 机器人 {botUserId} → 群 {groupId} ({members.Count} 人)");
    }

    private static string profileFallbackName(int botUserId) => $"机器人{botUserId}";

    // ==================== 工具 ====================

    private static string GetPrivateChatCacheKey(int userId1, int userId2)
    {
        var minId = Math.Min(userId1, userId2);
        var maxId = Math.Max(userId1, userId2);
        return $"chat:private:{minId}:{maxId}";
    }

    private static bool IsValidWebhookUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string ComputeHmac(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
