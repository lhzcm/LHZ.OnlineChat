using System.Text.RegularExpressions;
using LHZ.OnlineChat.Domain.Common;

namespace LHZ.OnlineChat.Domain.Users;

/// <summary>
/// 邮箱地址：格式校验 + 统一小写归一化都收敛在这里。
/// 原先散落在 AuthService（注册/登录/找回/换绑四处各写一遍 Trim().ToLowerInvariant() + 正则）。
/// </summary>
public sealed class Email : ValueObject
{
    private static readonly Regex Pattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private Email(string value) => Value = value;

    public string Value { get; }

    /// <summary>解析并归一化；格式非法抛 DomainException</summary>
    public static Email Parse(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        DomainException.Ensure(normalized.Length > 0 && Pattern.IsMatch(normalized), "邮箱格式不正确");
        DomainException.Ensure(normalized.Length <= 200, "邮箱长度不能超过 200 个字符");
        return new Email(normalized);
    }

    /// <summary>宽松解析：格式非法返回 null（用于「输入既可能是账号也可能是邮箱」的登录场景）</summary>
    public static Email? TryParse(string? raw)
    {
        var normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length > 0 && normalized.Length <= 200 && Pattern.IsMatch(normalized)
            ? new Email(normalized)
            : null;
    }

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }

    public override string ToString() => Value;
}

/// <summary>
/// 口令哈希：只承载「已经是哈希」这一事实，禁止明文误入。
/// 真正的哈希/校验算法（BCrypt）属于基础设施，经 IPasswordHasher 注入。
/// </summary>
public sealed class PasswordHash : ValueObject
{
    private PasswordHash(string value) => Value = value;

    public string Value { get; }

    /// <summary>包装一个由 IPasswordHasher 产出的哈希串</summary>
    public static PasswordHash FromHash(string hash)
    {
        DomainException.Ensure(!string.IsNullOrWhiteSpace(hash), "口令哈希不能为空");
        return new PasswordHash(hash);
    }

    /// <summary>明文口令的长度规则（注册/改密/重置共用同一条）</summary>
    public static void EnsureRawPasswordValid(string? raw)
    {
        DomainException.Ensure(!string.IsNullOrWhiteSpace(raw) && raw!.Length >= 6, "密码长度不能少于 6 个字符");
    }

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }

    public override string ToString() => Value;
}

/// <summary>
/// 昵称：长度规则收敛点。
/// 注意：不做成实体属性的类型——Nickname 要参与 SQL 的 LIKE 模糊查询（管理后台搜索），
/// 而值对象列无法出现在 FreeSql 的查询谓词里，故实体上仍存 string，此处只做校验与归一化。
/// </summary>
public static class NicknameRules
{
    public const int MaxLength = 50;

    public static string Normalize(string? raw)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        DomainException.Ensure(trimmed.Length > 0, "昵称不能为空");
        DomainException.Ensure(trimmed.Length <= MaxLength, $"昵称长度不能超过 {MaxLength} 个字符");
        return trimmed;
    }
}
