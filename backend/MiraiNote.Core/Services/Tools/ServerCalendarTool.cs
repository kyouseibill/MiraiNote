using System.Globalization;
using System.Text.Json;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// 日历/日期计算工具。纯本地计算，无需联网。
/// 支持日期推算、星期查询、日期差计算、列出某月指定星期几等。
/// </summary>
public class ServerCalendarTool : IServerAgentTool
{
    public string Name => "query_calendar";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "日期计算和日历查询工具（纯本地计算，无需联网）。" +
        "支持：距某日期还有多少天、某日期是周几、计算两日期间隔天数、列出某月所有指定星期几。" +
        "适用于用户询问时间相关计算问题。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["operation"] = ToolParameterProperty.Enum("操作类型", new()
            {
                "days_until",    // 距某日期还有多少天
                "day_of_week",   // 某日期是周几
                "date_diff",     // 两日期间隔天数
                "list_weekdays"  // 列出某月所有指定星期几
            }),
            ["date"] = ToolParameterProperty.String("日期（yyyy-MM-dd），days_until / day_of_week 操作必填"),
            ["date2"] = ToolParameterProperty.String("第二个日期（yyyy-MM-dd），date_diff 操作必填"),
            ["weekday"] = ToolParameterProperty.Enum("星期几（中文），list_weekdays 操作必填", new()
            {
                "周一", "周二", "周三", "周四", "周五", "周六", "周日"
            }),
            ["month"] = ToolParameterProperty.String("月份（yyyy-MM），list_weekdays 操作必填")
        },
        Required = new() { "operation" }
    };

    // IAgentTool 兼容
    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "operation", out var operation))
            return Task.FromResult("计算失败：未提供 operation。");

        try
        {
            var result = operation switch
            {
                "days_until" => DaysUntil(args),
                "day_of_week" => DayOfWeekQuery(args),
                "date_diff" => DateDiff(args),
                "list_weekdays" => ListWeekdays(args),
                _ => $"不支持的操作：{operation}。支持的操作：days_until、day_of_week、date_diff、list_weekdays。"
            };
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            return Task.FromResult($"日期计算失败：{ex.Message}");
        }
    }

    private static string DaysUntil(JsonElement args)
    {
        if (!ToolArgHelper.TryGetString(args, "date", out var dateStr))
            return "计算失败：未提供 date（如 2026-12-25）。";
        if (!DateTime.TryParseExact(dateStr, "yyyy-MM-dd", null, DateTimeStyles.None, out var target))
            return $"日期格式错误：{dateStr}，请使用 yyyy-MM-dd。";

        var cstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cstZone);
        var today = now.Date;
        var days = (target - today).Days;

        if (days == 0) return $"今天就是 {dateStr}！";
        if (days < 0) return $"{dateStr} 已在 {Math.Abs(days)} 天前过去。";

        var weekDay = target.DayOfWeek switch
        {
            System.DayOfWeek.Monday => "周一",
            System.DayOfWeek.Tuesday => "周二",
            System.DayOfWeek.Wednesday => "周三",
            System.DayOfWeek.Thursday => "周四",
            System.DayOfWeek.Friday => "周五",
            System.DayOfWeek.Saturday => "周六",
            _ => "周日"
        };

        return $"距 {dateStr}（{weekDay}）还有 {days} 天。";
    }

    private static string DayOfWeekQuery(JsonElement args)
    {
        if (!ToolArgHelper.TryGetString(args, "date", out var dateStr))
            return "计算失败：未提供 date。";
        if (!DateTime.TryParseExact(dateStr, "yyyy-MM-dd", null, DateTimeStyles.None, out var dt))
            return $"日期格式错误：{dateStr}，请使用 yyyy-MM-dd。";

        var weekDay = dt.DayOfWeek switch
        {
            System.DayOfWeek.Monday => "周一",
            System.DayOfWeek.Tuesday => "周二",
            System.DayOfWeek.Wednesday => "周三",
            System.DayOfWeek.Thursday => "周四",
            System.DayOfWeek.Friday => "周五",
            System.DayOfWeek.Saturday => "周六",
            _ => "周日"
        };

        return $"{dateStr} 是 {weekDay}。";
    }

    private static string DateDiff(JsonElement args)
    {
        if (!ToolArgHelper.TryGetString(args, "date", out var dateStr1))
            return "计算失败：未提供 date（第一个日期）。";
        if (!ToolArgHelper.TryGetString(args, "date2", out var dateStr2))
            return "计算失败：未提供 date2（第二个日期）。";
        if (!DateTime.TryParseExact(dateStr1, "yyyy-MM-dd", null, DateTimeStyles.None, out var dt1))
            return $"日期格式错误：{dateStr1}。";
        if (!DateTime.TryParseExact(dateStr2, "yyyy-MM-dd", null, DateTimeStyles.None, out var dt2))
            return $"日期格式错误：{dateStr2}。";

        var diff = Math.Abs((dt2 - dt1).Days);
        return $"{dateStr1} 与 {dateStr2} 相隔 {diff} 天。";
    }

    private static string ListWeekdays(JsonElement args)
    {
        if (!ToolArgHelper.TryGetString(args, "weekday", out var weekday))
            return "计算失败：未提供 weekday（如 周五）。";
        if (!ToolArgHelper.TryGetString(args, "month", out var monthStr))
            return "计算失败：未提供 month（如 2026-06）。";
        if (!DateTime.TryParseExact(monthStr + "-01", "yyyy-MM-dd", null, DateTimeStyles.None, out var firstDay))
            return $"月份格式错误：{monthStr}，请使用 yyyy-MM。";

        var targetDay = weekday switch
        {
            "周一" => System.DayOfWeek.Monday,
            "周二" => System.DayOfWeek.Tuesday,
            "周三" => System.DayOfWeek.Wednesday,
            "周四" => System.DayOfWeek.Thursday,
            "周五" => System.DayOfWeek.Friday,
            "周六" => System.DayOfWeek.Saturday,
            "周日" => System.DayOfWeek.Sunday,
            _ => System.DayOfWeek.Monday
        };

        var results = new List<string>();
        var daysInMonth = DateTime.DaysInMonth(firstDay.Year, firstDay.Month);
        for (int d = 1; d <= daysInMonth; d++)
        {
            var date = new DateTime(firstDay.Year, firstDay.Month, d);
            if (date.DayOfWeek == targetDay)
                results.Add(date.ToString("yyyy-MM-dd"));
        }

        return results.Count == 0
            ? $"{monthStr} 没有{weekday}。"
            : $"{monthStr} 的所有{weekday}：\n" + string.Join("\n", results.Select(r => $"- {r}"));
    }
}
