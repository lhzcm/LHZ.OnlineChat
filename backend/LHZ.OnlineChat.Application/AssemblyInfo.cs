using System.Runtime.CompilerServices;

// 用例处理器与领域事件订阅方都是 internal（外界只通过 MediatR 发命令，不该直接 new 处理器）。
// 单元测试需要直接构造它们来验证编排逻辑，因此对测试程序集开放。
[assembly: InternalsVisibleTo("LHZ.OnlineChat.Application.Tests")]
