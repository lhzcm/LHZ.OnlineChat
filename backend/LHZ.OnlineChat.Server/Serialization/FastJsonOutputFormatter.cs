using System.Text;
using LHZ.FastJson.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace LHZ.OnlineChat.Server.Serialization;

/// <summary>
/// 用 LHZ.FastJson 序列化 HTTP 响应体，与入站 formatter 成对。
///
/// DateTime 统一由 <see cref="HttpJsonOptions"/> 的 DateTimeFormat
/// "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK" 输出：F 省略末尾 0，K 按 DateTimeKind
/// 输出 Z / 偏移 / 空，与 System.Text.Json 逐字节一致。
///
/// ProblemDetails 也走 FastJson，但先投影成字典：2.1.2 已能把
/// IDictionary 属性写成对象，投影负责把 Extensions 平铺到顶层、省略值为 null 的
/// type/title/status/detail/instance，保持 RFC 7807 的标准形状。
/// </summary>
public sealed class FastJsonOutputFormatter : TextOutputFormatter
{
    public FastJsonOutputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/json"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/json"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/*+json"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/problem+json"));

        SupportedEncodings.Add(Encoding.UTF8);
    }

    /// <summary>
    /// 只拦截 JSON 对象。
    /// - string / Stream / null 让给 MVC 原有的 String/Stream/NoContent 格式化器，
    ///   避免把 text/plain、文件流或 204 改写成 JSON。
    /// </summary>
    public override bool CanWriteResult(OutputFormatterCanWriteContext context)
    {
        if (context.Object is null or string or Stream) return false;
        return base.CanWriteResult(context);
    }

    public override async Task WriteResponseBodyAsync(
        OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selectedEncoding);

        // ObjectResult 的 Value 为空时按 JSON null 输出，与 STJ 输出格式化器一致
        var value = context.Object is ProblemDetails problem
            ? NormalizeProblemDetails(problem)
            : context.Object;

        var json = value is null
            ? "null"
            : new JsonSerializer(value, HttpJsonOptions.Value).Serialize();

        await context.HttpContext.Response.WriteAsync(json, selectedEncoding);
    }

    /// <summary>
    /// 把 ProblemDetails 投影成 RFC 7807 的标准形状：
    /// 可选字段为 null 时省略，Extensions 平铺到顶层，ValidationProblemDetails 保留 errors。
    /// 2.1.2 起 IDictionary 属性按对象输出，errors 不会再变成 [{key,value}]。
    /// </summary>
    private static Dictionary<string, object?> NormalizeProblemDetails(ProblemDetails problem)
    {
        var payload = new Dictionary<string, object?>();

        if (problem.Type is not null) payload["type"] = problem.Type;
        if (problem.Title is not null) payload["title"] = problem.Title;
        if (problem.Status is not null) payload["status"] = problem.Status;
        if (problem.Detail is not null) payload["detail"] = problem.Detail;
        if (problem.Instance is not null) payload["instance"] = problem.Instance;

        if (problem is ValidationProblemDetails validation)
        {
            payload["errors"] = validation.Errors;
        }

        foreach (var extension in problem.Extensions)
        {
            payload[extension.Key] = extension.Value;
        }

        return payload;
    }
}
