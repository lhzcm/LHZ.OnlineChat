using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Tests;

/// <summary>
/// 领域测试共用的固定时刻。
/// 领域方法一律接收外部传入的 now，所以测试里时间完全可控 —— 不需要任何时间替身框架。
/// </summary>
internal static class T
{
    /// <summary>基准时刻（UTC）</summary>
    internal static readonly DateTime Now = new(2026, 3, 14, 10, 30, 0, DateTimeKind.Utc);

    internal static DateTime Plus(TimeSpan offset) => Now + offset;

    internal static DateTime Minus(TimeSpan offset) => Now - offset;

    internal static DateTime PlusMinutes(double minutes) => Now.AddMinutes(minutes);

    internal static DateTime MinusMinutes(double minutes) => Now.AddMinutes(-minutes);
}

/// <summary>可控时钟（供需要 IClock 的少数领域协作者使用）</summary>
internal sealed class FixedClock : IClock
{
    public FixedClock(DateTime? utcNow = null) => UtcNow = utcNow ?? T.Now;

    public DateTime UtcNow { get; set; }
}
