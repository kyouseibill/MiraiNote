namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 可调的 <see cref="TimeProvider"/>。未设置时走注入的基准时钟（默认 <see cref="TimeProvider.System"/>）。
/// 家务模块只通过 <see cref="TimeProvider.GetUtcNow"/> 读取当前时间，不调用 DateTime.Now / UtcNow。
/// </summary>
public sealed class AdjustableHouseholdTimeProvider : TimeProvider
{
    private readonly TimeProvider _baseClock;
    private readonly object _gate = new();
    private DateTimeOffset? _absoluteUtc;
    private TimeSpan? _offset;

    public AdjustableHouseholdTimeProvider(TimeProvider baseClock)
    {
        _baseClock = baseClock;
    }

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
        lock (_gate)
        {
            _absoluteUtc = utc.ToUniversalTime();
            _offset = null;
        }
    }

    public void SetOffset(TimeSpan offset)
    {
        lock (_gate)
        {
            _absoluteUtc = null;
            _offset = offset;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _absoluteUtc = null;
            _offset = null;
        }
    }
}
