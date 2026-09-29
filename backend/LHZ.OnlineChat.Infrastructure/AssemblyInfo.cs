using System.Runtime.CompilerServices;

// 基础设施层的实现类大多是 internal（外界只通过 Application 声明的端口使用它们）。
// 单元测试需要直接构造加密器、签名器、值对象转换器等纯逻辑组件来验证其行为。
[assembly: InternalsVisibleTo("LHZ.OnlineChat.Infrastructure.Tests")]
