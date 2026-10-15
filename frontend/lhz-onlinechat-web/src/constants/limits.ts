/**
 * 单条消息内容上限。
 *
 * 必须与后端 `MessageContentRules.MaxLength` 保持一致
 * （backend/LHZ.OnlineChat.Domain/Messaging/MessageValueObjects.cs）。
 * 前端这层只负责「提前拦住并说清原因」，真正的判定仍在服务端 ——
 * 即使这里被绕过，服务端也会回一条 `message_failed` 让气泡变成「发送失败」，
 * 不会再出现消息静默消失。
 */
export const MAX_MESSAGE_LENGTH = 50000
