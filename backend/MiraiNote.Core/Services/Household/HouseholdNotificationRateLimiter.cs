using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 按用户限制测试 Bark、测试邮件和定时邮件。窗口跟着家务时钟走，Testing 环境的测试时钟因此也生效。
/// </summary>
public sealed class HouseholdNotificationRateLimiter
{
    public const int MaxPerMinute = 3;
    public const int MaxPerHour = 20;
    public const string TestBark = "test-bark";
    public const string TestEmail = "test-email";
    public const string ScheduledEmail = "scheduled-email";
    public const string LimitedMessage = "发送过于频繁，请稍后再试";

    private readonly IHouseholdClock _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _hits = new();

    public HouseholdNotificationRateLimiter(IHouseholdClock clock)
    {
        _clock = clock;
    }

    public void EnsureAllowed(string scope, int userId)
    {
        if (!TryConsume(scope, userId))
            throw new BusinessException(LimitedMessage, 429);
    }

    public bool TryConsume(string scope, int userId)
    {
        var now = _clock.UtcNow;
        var key = scope + ":" + userId;
        lock (_gate)
        {
            if (!_hits.TryGetValue(key, out var hits))
            {
                hits = new List<DateTimeOffset>();
                _hits[key] = hits;
            }

            hits.RemoveAll(at => now - at >= TimeSpan.FromHours(1));
            var inMinute = hits.Count(at => now - at < TimeSpan.FromMinutes(1));
            if (inMinute >= MaxPerMinute || hits.Count >= MaxPerHour)
                return false;

            hits.Add(now);
            return true;
        }
    }
}
