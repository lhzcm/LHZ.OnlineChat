using System.Text.Json;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Application.Messaging;

/// <summary>
/// 「聊天记录」合并转发（messageType = 4）的载荷校验。
///
/// content 是一段 JSON 快照：
/// <code>
/// {"v":1,"items":[{"n":"张三","c":"内容","t":0,"ts":1700000000000}]}
/// </code>
/// 放在应用层而不是 Domain：Domain 不允许任何 JSON 依赖（连 System.Text.Json 都不行），
/// Domain 的 <see cref="MessageContentRules"/> 只负责"非空 + 长度"。
///
/// 为什么是快照而不是转发明细表：
///   - 原消息可能被撤回/删除，卡片必须保持转发当时的样子；
///   - 历史第一页要 JOIN 就为了一张只读卡片，代价不成比例；
///   - Redis 最近消息缓存存的是消息快照（content 原样），JSON 方案天然跟着缓存走。
/// 代价：搜索会匹配到 JSON 文本（前端与结果列表都按「聊天记录」占位展示），已在 README 说明。
/// </summary>
public static class ForwardBundle
{
    /// <summary>一次最多合并多少条（再多就该换成"导出聊天记录"）</summary>
    public const int MaxItems = 20;

    /// <summary>单条被转发内容的长度上限（卡片正文按摘要展示，不是原文库）</summary>
    public const int MaxItemLength = 2000;

    /// <summary>来源昵称长度上限（与昵称本身的限制同量级）</summary>
    public const int MaxSenderNameLength = 64;

    /// <summary>卡片里允许出现的消息类型：文字 / 图片 / Markdown（不允许嵌套转发、不允许文件）</summary>
    private static readonly int[] AllowedItemTypes =
    [
        (int)MessageKind.Text,
        (int)MessageKind.Image,
        (int)MessageKind.Markdown
    ];

    /// <summary>校验转发载荷；不合法时抛 <see cref="DomainException"/>（消息会原样回给发送者）</summary>
    public static void EnsureValid(string? content)
    {
        var json = (content ?? string.Empty).Trim();

        DomainException.Ensure(json.Length > 0, "转发内容不能为空");
        DomainException.Ensure(
            json.Length <= MessageContentRules.MaxLength,
            $"消息内容不能超过 {MessageContentRules.MaxLength} 字");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new DomainException("转发内容格式不正确");
        }

        using (document)
        {
            var root = document.RootElement;
            DomainException.Ensure(root.ValueKind == JsonValueKind.Object, "转发内容格式不正确");
            DomainException.Ensure(
                root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array,
                "转发内容格式不正确");

            var count = items.GetArrayLength();
            DomainException.Ensure(count > 0, "转发内容不能为空");
            DomainException.Ensure(count <= MaxItems, $"一次最多转发 {MaxItems} 条消息");

            foreach (var item in items.EnumerateArray())
            {
                DomainException.Ensure(item.ValueKind == JsonValueKind.Object, "转发内容格式不正确");

                var text = item.TryGetProperty("c", out var contentElement)
                           && contentElement.ValueKind == JsonValueKind.String
                    ? contentElement.GetString()
                    : null;
                DomainException.Ensure(!string.IsNullOrWhiteSpace(text), "转发内容不能为空");
                DomainException.Ensure(
                    text!.Length <= MaxItemLength,
                    $"转发的单条内容不能超过 {MaxItemLength} 字");

                var senderName = item.TryGetProperty("n", out var nameElement)
                                 && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;
                DomainException.Ensure(
                    (senderName ?? string.Empty).Length <= MaxSenderNameLength,
                    "转发来源昵称过长");

                // t 缺省按文字处理（宽容老客户端）；但不允许出现协议未定义或不允许的类型
                var kind = item.TryGetProperty("t", out var kindElement)
                           && kindElement.TryGetInt32(out var kindValue)
                    ? kindValue
                    : (int)MessageKind.Text;
                DomainException.Ensure(AllowedItemTypes.Contains(kind), "转发的消息类型不支持");
            }
        }
    }
}
