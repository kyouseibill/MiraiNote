using System.Globalization;
using System.Text.RegularExpressions;

namespace MiraiNote.Core.Services;

/// <summary>
/// Asia/Shanghai 日历日。Linux / IANA 用 Asia/Shanghai，Windows 回退 China Standard Time。
/// 不使用服务器本地时区。欢迎语按这个日历日计算。
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

    public static DateOnly ToShanghaiDate(DateTimeOffset utcNow, TimeZoneInfo? zone = null)
    {
        var shanghai = TimeZoneInfo.ConvertTime(utcNow, zone ?? Resolve());
        return DateOnly.FromDateTime(shanghai.DateTime);
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
