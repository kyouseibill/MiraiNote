namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 家务模块专用时钟。认证和其他模块继续使用系统时间，不读取这个抽象。
/// </summary>
public interface IHouseholdClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>始终返回 <see cref="TimeProvider.System"/>，忽略测试时钟。</summary>
public sealed class SystemHouseholdClock : IHouseholdClock
{
    public DateTimeOffset UtcNow => TimeProvider.System.GetUtcNow();
}

/// <summary>把任意 <see cref="TimeProvider"/> 适配成家务时钟，供测试固定时刻。</summary>
public sealed class DelegatingHouseholdClock : IHouseholdClock
{
    private readonly TimeProvider _clock;

    public DelegatingHouseholdClock(TimeProvider clock) => _clock = clock;

    public DateTimeOffset UtcNow => _clock.GetUtcNow();
}
