using System.Text.Json;

namespace LHZ.OnlineChat.Server.Services;

/// <summary>
/// Web 风格 JSON 选项（camelCase 属性名 + 大小写不敏感匹配），
/// 与 ASP.NET Core MVC 默认配置一致，保证 WebSocket 协议字段与前端 camelCase 兼容。
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}
