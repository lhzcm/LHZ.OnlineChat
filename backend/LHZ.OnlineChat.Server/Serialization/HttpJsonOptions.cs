using LHZ.FastJson;

namespace LHZ.OnlineChat.Server.Serialization;

/// <summary>
/// HTTP 入站 / 出站共用的 FastJson 选项。
///
/// DateTimeFormat 用 "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"：
/// - F 省略末尾 0（没有小数秒时连小数点一起省略）；
/// - K 按 DateTimeKind 输出 Z（Utc）/ 偏移（Local）/ 空（Unspecified）。
/// DateTime 输出与 System.Text.Json 逐字节一致，不再需要 IJsonCustomConverter。
///
/// 读取时 DateTimeFormat 只是 ISO 解析器之后的补充：ISO 8601 的 Z / 偏移 /
/// 无时区仍按 FastJson / STJ 的默认语义解析（Utc / Local / Unspecified）。
/// </summary>
internal static class HttpJsonOptions
{
    internal static readonly JsonConvertOptions Value = new()
    {
        CaseStyle = CaseStyle.CamelCase,
        DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    };
}
