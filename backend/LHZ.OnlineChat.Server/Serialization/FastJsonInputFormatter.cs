using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using LHZ.FastJson;
using LHZ.FastJson.Exceptions;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace LHZ.OnlineChat.Server.Serialization;

/// <summary>
/// 用 LHZ.FastJson 解析 HTTP JSON 请求体的输入格式化器。
///
/// MVC 的 [FromBody] 只认 InputFormatters，框架没有提供 AddFastJson() 之类的开关，
/// 因此要让入站 JSON 也走自研实现，只能替换格式化器。
///
/// 三处必须知道的取舍：
/// 1. 命名风格固定 camelCase。LHZ.FastJson 按所选风格精确匹配，不做大小写不敏感匹配：
///    PascalCase 的请求体不会报错，而是整段落到默认值 —— 这比解析失败更难排查。
/// 2. LHZ.FastJson 2.1.2 会执行构造函数，但随后给模型的每个属性赋值：请求体缺省的
///    字段会落成默认值（null / 0），构造函数和属性初始化器里的默认值会被覆盖。
///    [ApiController] 的隐式必填校验因此会把缺省的非空引用属性判成 400，
///    所以「前端可以不传」的字段必须声明成可空（见 SendVerificationCodeCommand.Purpose）。
/// 3. DateTime 由 FastJson 2.1.1 的 ISO 8601 解析器处理：Z → Utc、带偏移 → Local、
///    无时区 → Unspecified，与 System.Text.Json 的默认语义一致。
///    这里不再用 IJsonCustomConverter 把偏移 / 无时区强制归一到 UTC；当前只有
///    AdminMuteRequest.MutedUntil 一个日期入参，管理后台发的是 toISOString() 的 Z。
///
/// 出站由 <see cref="FastJsonOutputFormatter"/> 负责，两者共用
/// <see cref="HttpJsonOptions"/>（camelCase + 与 STJ 一致的 DateTimeFormat）。
/// </summary>
public sealed class FastJsonInputFormatter : TextInputFormatter
{
    private static readonly MethodInfo DeserializeCoreMethod = typeof(FastJsonInputFormatter)
        .GetMethod(nameof(DeserializeCore), BindingFlags.NonPublic | BindingFlags.Static)!;

    // 每个请求模型类型只做一次 MakeGenericMethod + CreateDelegate；
    // 直接 MethodInfo.Invoke 会把每次请求的异常包成 TargetInvocationException。
    private static readonly ConcurrentDictionary<Type, Func<string, object?>> Deserializers = new();

    public FastJsonInputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/json"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/json"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/*+json"));

        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(
        InputFormatterContext context, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(encoding);

        var request = context.HttpContext.Request;
        using var reader = new StreamReader(
            request.Body,
            encoding,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);

        var body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);

        // 空 body 不是「格式错误」：MVC 用它区分「没传」与「传了 null」，
        // 可选的 [FromBody] 参数（如 JoinByTokenRequest?）依赖 NoValue。
        if (body.Length == 0) return await InputFormatterResult.NoValueAsync();

        try
        {
            var model = GetDeserializer(context.ModelType)(body);
            return await InputFormatterResult.SuccessAsync(model);
        }
        catch (Exception exception) when (IsMalformedJson(exception))
        {
            // 交给 [ApiController] 转成 400，不能让解析异常冒成 500
            context.ModelState.TryAddModelError(context.ModelName, exception.Message);
            return await InputFormatterResult.FailureAsync();
        }
    }

    private static Func<string, object?> GetDeserializer(Type modelType)
        => Deserializers.GetOrAdd(
            modelType,
            static type => DeserializeCoreMethod
                .MakeGenericMethod(type)
                .CreateDelegate<Func<string, object?>>());

    private static object? DeserializeCore<T>(string body)
        => JsonConvert.Deserialize<T>(body, HttpJsonOptions.Value);

    private static bool IsMalformedJson(Exception exception)
        => exception is JsonReadException or FormatException or OverflowException or InvalidCastException;
}
