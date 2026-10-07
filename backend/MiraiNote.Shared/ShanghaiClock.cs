using System.Globalization;
using System.Text.RegularExpressions;

namespace MiraiNote.Shared;

/// <summary>
/// Asia/Shanghai 日历日。Linux / IANA 用 Asia/Shanghai，Windows 回退 China Standard Time。
/// 不使用服务器本地时区。上海无夏令时，UTC+8。上海 00:00 对应前一日 16:00 UTC。
/// </summary>
public static class ShanghaiClock
{
    public const string IanaId = "Asia/Shanghai";
    public const string WindowsId = "China Standard Time";

    public static TimeZoneInfo Resolve(Func<string, TimeZoneInfo>? finder = null)
    {
        finder ??= TimeZoneInfo.FindSystemTimeZoneById;
        foreach (var id in new[] { IanaId, WindowsId })
        {
            try
            {
                return finder(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException(
            "无法解析 Asia/Shanghai 或 China Standard Time。请确认服务器安装了时区数据。");
    }

    /// <summary>给定 UTC 时刻对应的上海日历日。</summary>
    public static DateOnly Today(DateTimeOffset utcNow, TimeZoneInfo? zone = null)
        => ToShanghaiDate(utcNow, zone);

    public static DateOnly ToShanghaiDate(DateTimeOffset utcNow, TimeZoneInfo? zone = null)
    {
        var shanghai = TimeZoneInfo.ConvertTime(utcNow, zone ?? Resolve());
        return DateOnly.FromDateTime(shanghai.DateTime);
    }

    /// <summary>上海日历日的 00:00，Kind 为 Unspecified，供只存日期的字段使用。</summary>
    public static DateTime TodayUnspecified(DateTimeOffset utcNow, TimeZoneInfo? zone = null)
    {
        var day = Today(utcNow, zone);
        return DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
    }

    /// <summary>上海墙钟时间，Kind 为 Unspecified。</summary>
    public static DateTime ToShanghaiWall(DateTimeOffset utcNow, TimeZoneInfo? zone = null)
    {
        var shanghai = TimeZoneInfo.ConvertTime(utcNow, zone ?? Resolve());
        return DateTime.SpecifyKind(shanghai.DateTime, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// 上海日历日对应的 UTC 半开区间 [当日 00:00, 次日 00:00)。
    /// 例如 2026-10-07 为 [2026-10-06 16:00Z, 2026-10-07 16:00Z)。
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(DateOnly shanghaiDate, TimeZoneInfo? zone = null)
    {
        var tz = zone ?? Resolve();
        var startLocal = DateTime.SpecifyKind(shanghaiDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var endLocal = DateTime.SpecifyKind(shanghaiDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(startLocal, tz), DateTimeKind.Utc),
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(endLocal, tz), DateTimeKind.Utc));
    }

    private static readonly Regex ExplicitOffset = new(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 把提醒时间写成 UTC。带 Z 或数字偏移的字符串按偏移换算；没有偏移的墙钟时间按上海时区解释。
    /// 上海 00:00 对应前一日 16:00 UTC。
    /// </summary>
    public static DateTime? ParseToUtc(string? text, TimeZoneInfo? zone = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        if (ExplicitOffset.IsMatch(trimmed)
            && DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto.UtcDateTime;
        }

        if (!DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
            return null;

        var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone ?? Resolve());
    }
}
