namespace MiraiNote.Core.Services.Household;

public enum HouseholdReminderKind
{
    None = 0,
    Lead = 1,
    Due = 2,
    Overdue = 3
}

/// <summary>
/// 纯日程：哪一天该提醒、离线后是否只补最新的一条。不读数据库，也不看推送时刻。
/// </summary>
public static class HouseholdReminderSchedule
{
    public static HouseholdReminderKind LatestKind(DateOnly today, DateOnly due, int leadDays)
    {
        if (leadDays < 0)
            leadDays = 0;

        var leadStart = leadDays == 0 ? due : due.AddDays(-leadDays);
        if (today < leadStart)
            return HouseholdReminderKind.None;
        if (today < due)
            return HouseholdReminderKind.Lead;
        if (today == due)
            return HouseholdReminderKind.Due;
        return HouseholdReminderKind.Overdue;
    }

    public static bool IsExactScheduleDay(DateOnly day, DateOnly due, int leadDays, int overdueInterval)
    {
        if (leadDays > 0 && day == due.AddDays(-leadDays))
            return true;
        if (day == due)
            return true;
        if (overdueInterval <= 0 || day <= due)
            return false;

        var overdueDays = day.DayNumber - due.DayNumber;
        return overdueDays % overdueInterval == 0;
    }

    /// <summary>
    /// 今天是日程日，或者今天之前有漏发的日程日。调用方还要保证今天还没发过。
    /// 漏发时只会产生一次 true，具体发几条由当天的唯一约束保证。
    /// </summary>
    public static bool ShouldNotify(
        DateOnly today,
        DateOnly due,
        int leadDays,
        int overdueInterval,
        DateOnly? lastSent)
    {
        if (LatestKind(today, due, leadDays) == HouseholdReminderKind.None)
            return false;
        if (lastSent == today)
            return false;
        if (IsExactScheduleDay(today, due, leadDays, overdueInterval))
            return true;
        return HasUnsentScheduleDay(today, due, leadDays, overdueInterval, lastSent);
    }

    private static bool HasUnsentScheduleDay(
        DateOnly today,
        DateOnly due,
        int leadDays,
        int overdueInterval,
        DateOnly? lastSent)
    {
        var windowStart = leadDays > 0 ? due.AddDays(-leadDays) : due;
        var from = lastSent.HasValue && lastSent.Value >= windowStart
            ? lastSent.Value.AddDays(1)
            : windowStart;
        if (from >= today)
            return false;

        var scanned = 0;
        for (var day = from; day < today && scanned < 3660; day = day.AddDays(1), scanned++)
        {
            if (IsExactScheduleDay(day, due, leadDays, overdueInterval))
                return true;
        }

        return false;
    }
}
