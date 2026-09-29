using FreeSql.DataAnnotations;
using FreeSql.Internal.Model;
using LHZ.OnlineChat.Domain.Users;

namespace LHZ.OnlineChat.Infrastructure.Persistence.TypeHandlers;

/// <summary>
/// 值对象 ↔ 数据列的双向转换器。
///
/// 为什么需要它：FreeSql 物化实体时走 Convert.DefaultToType，不认隐式转换运算符，
/// 因此「值对象直接作为实体属性」默认只能写不能读。TypeHandler 是官方扩展点，
/// 注册在基础设施层，领域层对此完全无感。
///
/// 已知约束：值对象类型的列不能出现在查询谓词的成员访问里
/// （`Where(u => u.Email.Value == s)` 无法解析），但整体比较可以
/// （`Where(u => u.Email == emailVo)`），所以仓储的查询方法一律接收值对象参数。
///
/// 其余值对象（GroupAnnouncement / MessageReply / MentionList / WebhookUrl）
/// 都不直接作为列：前两个由多列聚合成只读属性，后两个在实体上仍存原始类型
/// （Mentions 要兼容既有的逗号分隔存储，WebhookUrl 要参与空值判断）。
/// </summary>
internal static class ValueObjectTypeHandlers
{
    private static bool _registered;
    private static readonly object Gate = new();

    /// <summary>进程级注册一次（FreeSql 的 TypeHandlers 是静态表）</summary>
    internal static void RegisterAll()
    {
        if (_registered) return;
        lock (Gate)
        {
            if (_registered) return;

            FreeSql.Internal.Utils.TypeHandlers.TryAdd(typeof(Email), new EmailTypeHandler());
            FreeSql.Internal.Utils.TypeHandlers.TryAdd(typeof(PasswordHash), new PasswordHashTypeHandler());

            _registered = true;
        }
    }
}

/// <summary>邮箱 ↔ varchar</summary>
internal sealed class EmailTypeHandler : TypeHandler<Email>
{
    public override object Serialize(Email value) => value.Value;

    public override Email Deserialize(object value) => Email.Parse((string)value);

    public override void FluentApi(ColumnFluent col) => col.MapType(typeof(string));
}

/// <summary>
/// 口令哈希 ↔ varchar。
/// 反序列化直接包装、不做格式校验：库里的历史值一律视为有效哈希。
/// </summary>
internal sealed class PasswordHashTypeHandler : TypeHandler<PasswordHash>
{
    public override object Serialize(PasswordHash value) => value.Value;

    public override PasswordHash Deserialize(object value) => PasswordHash.FromHash((string)value);

    public override void FluentApi(ColumnFluent col) => col.MapType(typeof(string));
}
