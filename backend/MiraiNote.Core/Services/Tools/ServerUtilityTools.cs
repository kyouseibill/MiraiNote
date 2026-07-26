using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// Safe utility tools for everyday Agent tasks.
/// </summary>
public class ServerCurrentTimeTool : IServerAgentTool
{
    public string Name => "get_current_time";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "获取当前日期、时间、星期和时区信息。适用于用户询问今天、明天、本周、当前时间或需要把相对日期换算成具体日期的场景。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["timezone"] = ToolParameterProperty.String("IANA/Windows 时区 ID，默认 Asia/Shanghai")
        }
    };

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var timezone = "Asia/Shanghai";
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            ToolArgHelper.TryGetString(doc.RootElement, "timezone", out timezone);
            if (string.IsNullOrWhiteSpace(timezone)) timezone = "Asia/Shanghai";
        }
        catch
        {
            timezone = "Asia/Shanghai";
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            timezone = "Asia/Shanghai";
        }

        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var result = new
        {
            timezone,
            now = now.ToString("yyyy-MM-dd HH:mm:ss"),
            date = now.ToString("yyyy-MM-dd"),
            time = now.ToString("HH:mm:ss"),
            weekday = ToChineseWeekday(now.DayOfWeek),
            isoUtc = DateTime.UtcNow.ToString("O")
        };
        return Task.FromResult(JsonSerializer.Serialize(result));
    }

    private static string ToChineseWeekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };
}

public class ServerCalculatorTool : IServerAgentTool
{
    private static readonly Regex AllowedExpression = new(@"^[0-9+\-*/().%\s]+$", RegexOptions.Compiled);

    public string Name => "calculate";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "执行基础数学计算。支持 +、-、*、/、%、括号和小数，适用于预算、比例、天数外的数值计算等场景。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["expression"] = ToolParameterProperty.String("要计算的数学表达式，例如 (1200+350)*0.8")
        },
        Required = new() { "expression" }
    };

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        if (!ToolArgHelper.TryGetString(doc.RootElement, "expression", out var expression))
            return Task.FromResult("计算失败：expression 为必填项。");

        expression = expression.Replace("%", "/100", StringComparison.Ordinal);
        if (!AllowedExpression.IsMatch(expression))
            return Task.FromResult("计算失败：表达式只允许数字、四则运算符、百分号、小数点和括号。");

        try
        {
            var value = new DataTable().Compute(expression, null);
            var result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                expression,
                result = result.ToString(CultureInfo.InvariantCulture)
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"计算失败：{ex.Message}");
        }
    }
}

public class ServerRecordOverviewTool : IServerAgentTool
{
    private readonly MiraiNoteDbContext _db;

    public string Name => "record_overview";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "按日期范围汇总用户的工作记录、备忘、生活记录和周报数量，并给出工作分类、工作状态、生活心情、待办数量等概览。适用于日报、周报、复盘、趋势分析前的快速摸底。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["date_from"] = ToolParameterProperty.String("起始日期 yyyy-MM-dd，不填默认最近 7 天"),
            ["date_to"] = ToolParameterProperty.String("结束日期 yyyy-MM-dd，不填默认今天")
        }
    };

    public ServerRecordOverviewTool(MiraiNoteDbContext db)
    {
        _db = db;
    }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var (from, to) = ParseRange(argumentsJson);

        var workLogs = await _db.WorkLogs.AsNoTracking()
            .Where(w => w.UserId == userId && w.LogDate >= from && w.LogDate <= to)
            .Select(w => new { w.Category, w.Status })
            .ToListAsync(ct);

        var lifeLogs = await _db.LifeLogs.AsNoTracking()
            .Where(l => l.UserId == userId && l.LogDate >= from && l.LogDate <= to)
            .Select(l => new { l.Mood })
            .ToListAsync(ct);

        var memos = await _db.Memos.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => new { m.Section, m.IsDone, m.IsArchived, m.Priority, m.RemindAt })
            .ToListAsync(ct);

        var reports = await _db.WeeklyReports.AsNoTracking()
            .Where(r => r.UserId == userId && r.WeekStart <= to && r.WeekEnd >= from)
            .CountAsync(ct);

        var result = new
        {
            range = new { from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd") },
            workLogs = new
            {
                total = workLogs.Count,
                byCategory = workLogs
                    .GroupBy(w => string.IsNullOrWhiteSpace(w.Category) ? "未分类" : w.Category)
                    .ToDictionary(g => g.Key, g => g.Count()),
                byStatus = workLogs
                    .GroupBy(w => WorkStatusName(w.Status))
                    .ToDictionary(g => g.Key, g => g.Count())
            },
            memos = new
            {
                total = memos.Count,
                pending = memos.Count(m => !m.IsDone && !m.IsArchived),
                done = memos.Count(m => m.IsDone),
                archived = memos.Count(m => m.IsArchived),
                highPriorityPending = memos.Count(m => !m.IsDone && !m.IsArchived && m.Priority == 3),
                withReminder = memos.Count(m => m.RemindAt.HasValue),
                bySection = memos.GroupBy(m => m.Section).ToDictionary(g => g.Key, g => g.Count())
            },
            lifeLogs = new
            {
                total = lifeLogs.Count,
                byMood = lifeLogs
                    .GroupBy(l => string.IsNullOrWhiteSpace(l.Mood) ? "未标记" : l.Mood)
                    .ToDictionary(g => g.Key, g => g.Count())
            },
            weeklyReports = new { total = reports }
        };

        return JsonSerializer.Serialize(result);
    }

    private static (DateTime from, DateTime to) ParseRange(string argumentsJson)
    {
        var today = DateTime.Today;
        var from = today.AddDays(-6);
        var to = today;

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (ToolArgHelper.TryGetString(doc.RootElement, "date_from", out var fromText) &&
                DateTime.TryParse(fromText, out var parsedFrom))
                from = parsedFrom.Date;
            if (ToolArgHelper.TryGetString(doc.RootElement, "date_to", out var toText) &&
                DateTime.TryParse(toText, out var parsedTo))
                to = parsedTo.Date;
        }
        catch
        {
            // Keep defaults.
        }

        if (from > to) (from, to) = (to, from);
        return (from, to);
    }

    private static string WorkStatusName(byte status) => status switch
    {
        1 => "进行中",
        2 => "已完成",
        3 => "已延期",
        _ => "未标记"
    };
}
