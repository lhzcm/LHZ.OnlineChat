using LHZ.OnlineChat.Application.Common;
using LHZ.OnlineChat.Application.Common.Behaviors;
using LHZ.OnlineChat.Application.Tests.TestDoubles;
using LHZ.OnlineChat.Domain.Common;
using LHZ.OnlineChat.Domain.Users;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace LHZ.OnlineChat.Application.Tests.Common;

/// <summary>
/// 领域异常 → ApiResponse.Fail 的统一转换。
/// 这是「让领域层摆脱 ApiResponse」方案的关键一环：
/// 领域层只 throw DomainException，响应形状由这里统一落地，
/// 前端拿到的 JSON 与改造前完全一致。
/// </summary>
public class DomainExceptionBehaviorTests
{
    private sealed record PlainCommand : ICommand<ApiResponse>;

    private sealed record TypedCommand : ICommand<ApiResponse<string>>;

    private static DomainExceptionBehavior<TRequest, TResponse> Behavior<TRequest, TResponse>()
        where TRequest : notnull
        => new(NullLogger<DomainExceptionBehavior<TRequest, TResponse>>.Instance);

    [Fact]
    public async Task 正常路径原样透传()
    {
        var expected = ApiResponse.Ok("成功了");

        var actual = await Behavior<PlainCommand, ApiResponse>().Handle(
            new PlainCommand(), _ => Task.FromResult(expected), default);

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task 非泛型响应_领域异常转为Fail且保留提示语()
    {
        var result = await Behavior<PlainCommand, ApiResponse>().Handle(
            new PlainCommand(),
            _ => throw new DomainException("昵称不能为空"),
            default);

        Assert.False(result.Success);
        Assert.Equal("昵称不能为空", result.Message);
    }

    [Fact]
    public async Task 泛型响应_领域异常转为Fail且Data为null()
    {
        var result = await Behavior<TypedCommand, ApiResponse<string>>().Handle(
            new TypedCommand(),
            _ => throw new DomainException("邮箱格式不正确"),
            default);

        Assert.False(result.Success);
        Assert.Equal("邮箱格式不正确", result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task 派生的EntityNotFound也被转换()
    {
        var result = await Behavior<PlainCommand, ApiResponse>().Handle(
            new PlainCommand(),
            _ => throw new EntityNotFoundException("用户不存在"),
            default);

        Assert.False(result.Success);
        Assert.Equal("用户不存在", result.Message);
    }

    [Fact]
    public async Task 派生的ForbiddenOperation也被转换()
    {
        var result = await Behavior<PlainCommand, ApiResponse>().Handle(
            new PlainCommand(),
            _ => throw new ForbiddenOperationException("无权操作"),
            default);

        Assert.False(result.Success);
        Assert.Equal("无权操作", result.Message);
    }

    [Fact]
    public async Task 禁言异常也被转换_携带原提示语()
    {
        var result = await Behavior<PlainCommand, ApiResponse>().Handle(
            new PlainCommand(),
            _ => throw new Domain.Groups.MemberMutedException(
                DateTime.UtcNow, "你已被禁言至 03-14 12:30，期间无法在群里发言"),
            default);

        Assert.False(result.Success);
        Assert.Contains("禁言", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 非领域异常原样抛出_不被误吞成业务失败()
    {
        // 基础设施故障（连不上库等）必须继续冒泡成 500，不能伪装为业务驳回
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Behavior<PlainCommand, ApiResponse>().Handle(
                new PlainCommand(),
                _ => throw new InvalidOperationException("数据库连接失败"),
                default));
    }

    [Fact]
    public async Task 非ApiResponse返回类型时原样抛出()
    {
        // WS 路径的用例返回内部结果（如 SendMessageResult / bool），
        // 无法转成 ApiResponse，应让异常继续冒泡由调用方处理
        await Assert.ThrowsAsync<DomainException>(
            () => Behavior<PlainCommand, bool>().Handle(
                new PlainCommand(),
                _ => throw new DomainException("不该被转换"),
                default));
    }
}

public class LoggingBehaviorTests
{
    private sealed record ProbeCommand : ICommand<ApiResponse>;

    private static LoggingBehavior<ProbeCommand, ApiResponse> Behavior()
        => new(NullLogger<LoggingBehavior<ProbeCommand, ApiResponse>>.Instance);

    [Fact]
    public async Task 正常路径透传响应()
    {
        var expected = ApiResponse.Ok();

        var actual = await Behavior().Handle(
            new ProbeCommand(), _ => Task.FromResult(expected), default);

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task 领域异常原样抛出_交给下游行为转换()
    {
        await Assert.ThrowsAsync<DomainException>(() => Behavior().Handle(
            new ProbeCommand(), _ => throw new DomainException("业务驳回"), default));
    }

    [Fact]
    public async Task 其他异常原样抛出()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => Behavior().Handle(
            new ProbeCommand(), _ => throw new TimeoutException(), default));
    }

    [Fact]
    public async Task 取消令牌传递到下游()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken received = default;

        await Behavior().Handle(new ProbeCommand(), ct =>
        {
            received = ct;
            return Task.FromResult(ApiResponse.Ok());
        }, cts.Token);

        Assert.Equal(cts.Token, received);
    }
}

public class ApiResponseTests
{
    [Fact]
    public void Ok_默认消息为success()
    {
        var response = ApiResponse.Ok();

        Assert.True(response.Success);
        Assert.Equal("success", response.Message);
    }

    [Fact]
    public void 泛型Ok_携带数据()
    {
        var response = ApiResponse<string>.Ok("载荷", "完成");

        Assert.True(response.Success);
        Assert.Equal("完成", response.Message);
        Assert.Equal("载荷", response.Data);
    }

    [Fact]
    public void Fail_不携带数据()
    {
        var response = ApiResponse<string>.Fail("出错了");

        Assert.False(response.Success);
        Assert.Equal("出错了", response.Message);
        Assert.Null(response.Data);
    }

    [Fact]
    public void 非泛型继承自泛型object版本_保持既有响应形状()
    {
        // 前端依赖 { success, message, data } 这一形状，改造后必须不变
        Assert.IsAssignableFrom<ApiResponse<object>>(ApiResponse.Ok());
        Assert.IsAssignableFrom<IApiResponse>(ApiResponse.Ok());
        Assert.IsAssignableFrom<IApiResponse>(ApiResponse<string>.Ok("x"));
    }

    [Fact]
    public void PagedResult_Create带上分页参数()
    {
        var page = new PageRequest(2, 20);

        var result = PagedResult<int>.Create(new[] { 1, 2, 3 }, total: 50, page);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(50, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public void PagedResult_Empty保留分页参数()
    {
        var result = PagedResult<int>.Empty(new PageRequest(3, 10));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
        Assert.Equal(3, result.Page);
        Assert.Equal(10, result.PageSize);
    }
}

public class DomainEventDispatcherExtensionsTests
{
    [Fact]
    public async Task DispatchEventsOf_取出聚合事件并派发()
    {
        var dispatcher = new RecordingEventDispatcher();
        var user = User.Register(
            "张三", Email.Parse("a@test.local"),
            PasswordHash.FromHash("$2a$11$x"), FakeClock.Default);
        user.AssignPersistedId(10001);
        user.ConfirmRegistration(FakeClock.Default);

        await dispatcher.DispatchEventsOfAsync(user);

        Assert.Single(dispatcher.Dispatched);
        // 派发后聚合内的事件应已清空，避免重复派发
        Assert.Empty(user.DequeueDomainEvents());
    }

    [Fact]
    public async Task DispatchAsync_单个事件()
    {
        var dispatcher = new RecordingEventDispatcher();

        await dispatcher.DispatchAsync(
            new UserRegistered(10001, "张三", FakeClock.Default));

        Assert.Single(dispatcher.EventsOf<UserRegistered>());
    }

    [Fact]
    public async Task 无事件时不派发()
    {
        var dispatcher = new RecordingEventDispatcher();
        var user = User.Register(
            "张三", Email.Parse("a@test.local"),
            PasswordHash.FromHash("$2a$11$x"), FakeClock.Default);

        await dispatcher.DispatchEventsOfAsync(user);

        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public void DomainEventNotification_包装事件供MediatR发布()
    {
        var domainEvent = new UserRegistered(10001, "张三", FakeClock.Default);

        var notification = new DomainEventNotification<UserRegistered>(domainEvent);

        Assert.Same(domainEvent, notification.Event);
        Assert.IsAssignableFrom<INotification>(notification);
    }
}
