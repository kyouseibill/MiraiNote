using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 家务模块可调时钟。实现 <see cref="IHouseholdClock"/>，不注册为全局 <see cref="TimeProvider"/>。
/// 绝对时间或偏移必须落在基准时钟（真实现在）的前后 10 年以内。
/// </summary>
public sealed class AdjustableHouseholdTimeProvider : TimeProvider, IHouseholdClock
{
    public const int MaxAbsYears = 10;
    public const string OutOfRangeMessage = "测试时钟只能调整到当前时间的前后 10 年以内";

    private readonly TimeProvider _baseClock;
    private readonly object _gate = new();
    private DateTimeOffset? _absoluteUtc;
    private TimeSpan? _offset;

    public AdjustableHouseholdTimeProvider(TimeProvider baseClock)
    {
        _baseClock = baseClock;
    }

    public DateTimeOffset UtcNow => GetUtcNow();

    public string Mode
    {
        get
        {
            lock (_gate)
            {
                if (_absoluteUtc != null) return "Absolute";
                if (_offset != null) return "Offset";
                return "System";
            }
        }
    }

    public DateTimeOffset? AbsoluteUtc
    {
        get { lock (_gate) return _absoluteUtc; }
    }

    public long? OffsetSeconds
    {
        get { lock (_gate) return _offset == null ? null : (long)_offset.Value.TotalSeconds; }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            if (_absoluteUtc is DateTimeOffset absolute)
                return absolute;
            var now = _baseClock.GetUtcNow();
            return _offset is TimeSpan offset ? now.Add(offset) : now;
        }
    }

    public void SetAbsolute(DateTimeOffset utc)
    {
        var target = utc.ToUniversalTime();
        EnsureWithinWindow(target);
        lock (_gate)
        {
            _absoluteUtc = target;
            _offset = null;
        }
    }

    public void SetOffset(TimeSpan offset)
    {
        DateTimeOffset target;
        try
        {
            target = _baseClock.GetUtcNow().Add(offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw OutOfRange();
        }

        EnsureWithinWindow(target);
        lock (_gate)
        {
            _absoluteUtc = null;
            _offset = offset;
        }
    }

    public void SetOffsetSeconds(long seconds)
    {
        TimeSpan offset;
        try
        {
            offset = TimeSpan.FromSeconds(seconds);
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            throw OutOfRange();
        }

        SetOffset(offset);
    }

    public void Reset()
    {
        lock (_gate)
        {
            _absoluteUtc = null;
            _offset = null;
        }
    }

    private void EnsureWithinWindow(DateTimeOffset target)
    {
        var real = _baseClock.GetUtcNow();
        DateTimeOffset min;
        DateTimeOffset max;
        try
        {
            min = real.AddYears(-MaxAbsYears);
            max = real.AddYears(MaxAbsYears);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw OutOfRange();
        }

        if (target < min || target > max)
            throw OutOfRange();
    }

    private static BusinessException OutOfRange() => new(OutOfRangeMessage, 400);
}
