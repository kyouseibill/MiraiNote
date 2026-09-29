using MiraiNote.Data.Entities;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 同一天同一条提醒最多尝试 3 次。失败后先等 1 分钟，再等 5 分钟。
/// Skipped 不占住当天：事项恢复后可以重新发送。
/// </summary>
internal static class HouseholdReminderAttempt
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan InFlightWindow = TimeSpan.FromSeconds(60);

    public static bool CanClaim(
        HouseholdReminderDeliveryStatus status,
        int attemptCount,
        DateTime? lastAttemptAt,
        DateTime utcNow)
    {
        var now = AsUtc(utcNow);
        if (status == HouseholdReminderDeliveryStatus.Sent)
            return false;
        if (status == HouseholdReminderDeliveryStatus.Skipped)
            return true;
        if (attemptCount >= MaxAttempts)
            return false;

        var last = lastAttemptAt.HasValue ? AsUtc(lastAttemptAt.Value) : (DateTime?)null;
        if (status == HouseholdReminderDeliveryStatus.Pending)
            return last == null || now >= last.Value.Add(InFlightWindow);

        if (status != HouseholdReminderDeliveryStatus.Failed)
            return false;

        var wait = attemptCount <= 1 ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(5);
        return last == null || now >= last.Value.Add(wait);
    }

    public static int NextAttemptCount(HouseholdReminderDeliveryStatus status, int attemptCount) =>
        status == HouseholdReminderDeliveryStatus.Skipped ? 1 : attemptCount + 1;

    /// <summary>只保留异常类型。异常消息里经常带着请求地址，不能写入提醒日志。</summary>
    public static string Summarize(Exception exception)
    {
        var text = "发送失败（" + exception.GetType().Name + "）";
        return text.Length <= 200 ? text : text[..200];
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
