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
}
