using System.Runtime.CompilerServices;

// 基础设施层需要调用 Entity<TId>.AssignPersistedId 回填数据库生成的主键。
// 只开放这一处接缝，业务代码（Application / Server）依旧看不到任何写 Id 的途径。
[assembly: InternalsVisibleTo("LHZ.OnlineChat.Infrastructure")]

// 单元测试需要构造「已持久化」状态的聚合（回填 Id），以及直接验证这个接缝本身的行为。
[assembly: InternalsVisibleTo("LHZ.OnlineChat.Domain.Tests")]
[assembly: InternalsVisibleTo("LHZ.OnlineChat.Application.Tests")]
[assembly: InternalsVisibleTo("LHZ.OnlineChat.Infrastructure.Tests")]
