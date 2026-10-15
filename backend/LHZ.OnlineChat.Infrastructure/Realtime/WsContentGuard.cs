using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Messaging;

namespace LHZ.OnlineChat.Infrastructure.Realtime;

/// <summary>
/// 入站消息的**内容**预校验（在派发到用例之前单独做一次）。
///
/// 为什么要有单独这一道：内容不合法（超长、为空、类型未定义）是发送方自己输入的问题，
/// 可以明确回执给发送者 —— 否则消息会被静默丢弃，客户端那口气泡永远停在「未读」、
/// 刷新后连痕迹都没有（2026-10 实际发生过：5000 字上限拦住一条长消息，日志里只有一行
/// fail，用户完全不知道发生了什么）。而权限/关系类失败（不是好友、不在群里、被拉黑）
/// 必须静默，回了就成了探测工具（CODING_STANDARDS §4）。
///
/// 校验用的是与用例同一份领域规则，所以不会出现「前端放过了、服务端不认」的口径差。
/// </summary>
internal static class WsContentGuard
{
    /// <summary>合法返回 null；不合法返回可以直接回给发送者的原因</summary>
    internal static string? DescribeRejection(string? content, int messageType)
    {
        try
        {
            MessageContentRules.EnsureValid(content, (MessageKind)messageType);
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// 构造回给发送者的「发送被拒」帧：type + 原样的 messageId（客户端据此定位气泡）
    /// + content 里的原因。from/to 照抄入站报文，方便客户端按会话归类。
    /// </summary>
    internal static WsMessage BuildFailureFrame(WsMessage inbound, string reason) => new()
    {
        Type = WsMessageType.MessageFailed,
        From = inbound.From,
        To = inbound.To,
        Content = reason,
        MessageId = inbound.MessageId,
        MessageType = inbound.MessageType
    };
}
