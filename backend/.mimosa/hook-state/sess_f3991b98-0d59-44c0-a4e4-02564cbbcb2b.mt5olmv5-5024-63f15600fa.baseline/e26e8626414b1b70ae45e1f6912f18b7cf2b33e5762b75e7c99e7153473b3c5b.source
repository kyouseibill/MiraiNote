using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// 基类：服务端查询工具（只读操作，风险等级 Safe）。
/// </summary>
public abstract class ServerQueryTool : IServerAgentTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract ToolParameterSchema Parameters { get; }
    public virtual ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;

    // IAgentTool 兼容（无 userId 版本，Server 端通常不调用此重载）
    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public abstract Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default);
}

// ===== 工作记录查询 =====

public class ServerSearchWorkLogsTool : ServerQueryTool
{
    private readonly MiraiNoteDbContext _db;
    public ServerSearchWorkLogsTool(MiraiNoteDbContext db) { _db = db; }

    public override string Name => "search_work_logs";
    public override string Description =>
        "查询用户的工作记录。支持按日期范围、关键词、项目分类筛选。" +
        "当用户询问工作内容、工作进展、某天/某周做了什么工作、工作总结时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["date_from"] = ToolParameterProperty.String("起始日期 yyyy-MM-dd"),
            ["date_to"] = ToolParameterProperty.String("结束日期 yyyy-MM-dd"),
            ["keyword"] = ToolParameterProperty.String("关键词，模糊匹配标题/内容/目的"),
            ["category"] = ToolParameterProperty.String("项目分类名称")
        }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        var q = _db.WorkLogs.AsNoTracking().Where(w => w.UserId == userId);

        if (TryStr(args, "date_from", out var df) && DateTime.TryParse(df, out var from))
            q = q.Where(w => w.LogDate >= from.Date);
        if (TryStr(args, "date_to", out var dt) && DateTime.TryParse(dt, out var to))
            q = q.Where(w => w.LogDate <= to.Date);
        if (TryStr(args, "keyword", out var kw))
            q = q.Where(w => w.Title.Contains(kw) || (w.Content != null && w.Content.Contains(kw)) || (w.Purpose != null && w.Purpose.Contains(kw)));
        if (TryStr(args, "category", out var cat))
            q = q.Where(w => w.Category == cat);

        var raw = await q.OrderByDescending(w => w.LogDate).Take(20)
            .Select(w => new { w.Id, w.Title, w.Purpose, w.Content, w.Tags, w.Category, w.LogDate, w.Status, w.StatusRemark })
            .ToListAsync(ct);
        if (raw.Count == 0) return "没有找到符合条件的工作记录。";

        return JsonSerializer.Serialize(raw.Select(w => new
        {
            id = w.Id, title = w.Title, purpose = w.Purpose, content = w.Content,
            tags = w.Tags, category = w.Category, logDate = w.LogDate.ToString("yyyy-MM-dd"),
            status = w.Status == 1 ? "进行中" : w.Status == 2 ? "已完成" : w.Status == 3 ? "已延期" : "未标记",
            statusRemark = w.StatusRemark
        }));
    }

    private static bool TryStr(JsonElement el, string key, out string val)
    {
        if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
        { val = p.GetString()!; return !string.IsNullOrWhiteSpace(val); }
        val = ""; return false;
    }
}

// ===== 备忘查询 =====

public class ServerSearchMemosTool : ServerQueryTool
{
    private readonly MiraiNoteDbContext _db;
    public ServerSearchMemosTool(MiraiNoteDbContext db) { _db = db; }

    public override string Name => "search_memos";
    public override string Description =>
        "查询用户的备忘/待办事项。当用户询问备忘、待办、提醒、会议安排、任务清单时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["section"] = ToolParameterProperty.String("'work' 或 'life'，不填查全部板块"),
            ["keyword"] = ToolParameterProperty.String("关键词模糊匹配"),
            ["include_done"] = ToolParameterProperty.Boolean("是否包含已完成，默认 false"),
            ["include_archived"] = ToolParameterProperty.Boolean("是否包含已归档，默认 false")
        }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        var q = _db.Memos.AsNoTracking().Where(m => m.UserId == userId);

        if (TryStr(args, "section", out var sec)) q = q.Where(m => m.Section == sec);
        if (TryStr(args, "keyword", out var kw)) q = q.Where(m => m.Content.Contains(kw));
        if (!(args.TryGetProperty("include_done", out var d) && d.ValueKind == JsonValueKind.True))
            q = q.Where(m => !m.IsDone);
        if (!(args.TryGetProperty("include_archived", out var a) && a.ValueKind == JsonValueKind.True))
            q = q.Where(m => !m.IsArchived);

        var raw = await q.OrderByDescending(m => m.IsPinned).ThenByDescending(m => m.Priority).Take(30)
            .Select(m => new { m.Id, m.Section, m.Content, m.Priority, m.IsPinned, m.IsDone, m.RemindAt })
            .ToListAsync(ct);
        if (raw.Count == 0) return "没有找到符合条件的备忘事项。";

        var cst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        return JsonSerializer.Serialize(raw.Select(m => new
        {
            id = m.Id, section = m.Section, content = m.Content,
            priority = m.Priority == 3 ? "高" : m.Priority == 2 ? "中" : "低",
            isPinned = m.IsPinned, isDone = m.IsDone,
            remindAt = m.RemindAt.HasValue
                ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(m.RemindAt.Value, DateTimeKind.Utc), cst).ToString("yyyy-MM-dd HH:mm")
                : null
        }));
    }

    private static bool TryStr(JsonElement el, string key, out string val)
    {
        if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
        { val = p.GetString()!; return !string.IsNullOrWhiteSpace(val); }
        val = ""; return false;
    }
}

// ===== 生活记录查询 =====

public class ServerSearchLifeLogsTool : ServerQueryTool
{
    private readonly MiraiNoteDbContext _db;
    public ServerSearchLifeLogsTool(MiraiNoteDbContext db) { _db = db; }

    public override string Name => "search_life_logs";
    public override string Description =>
        "查询用户的生活记录/日记。支持按日期、心情、关键词筛选。" +
        "当用户询问生活状态、某天经历、心情，或模糊查询今日/本周事项时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["date_from"] = ToolParameterProperty.String("起始日期 yyyy-MM-dd"),
            ["date_to"] = ToolParameterProperty.String("结束日期 yyyy-MM-dd"),
            ["keyword"] = ToolParameterProperty.String("关键词模糊匹配"),
            ["mood"] = ToolParameterProperty.String("心情标签：开心/平静/疲惫/难过")
        }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        var q = _db.LifeLogs.AsNoTracking().Where(l => l.UserId == userId);

        if (TryStr(args, "date_from", out var df) && DateTime.TryParse(df, out var from))
            q = q.Where(l => l.LogDate >= from.Date);
        if (TryStr(args, "date_to", out var dt) && DateTime.TryParse(dt, out var to))
            q = q.Where(l => l.LogDate <= to.Date);
        if (TryStr(args, "keyword", out var kw)) q = q.Where(l => l.Content.Contains(kw));
        if (TryStr(args, "mood", out var mood)) q = q.Where(l => l.Mood == mood);

        var raw = await q.OrderByDescending(l => l.LogDate).Take(20)
            .Select(l => new { l.Id, l.Content, l.Mood, l.LogDate })
            .ToListAsync(ct);
        if (raw.Count == 0) return "没有找到符合条件的生活记录。";

        return JsonSerializer.Serialize(raw.Select(l => new
        {
            id = l.Id, content = l.Content, mood = l.Mood, logDate = l.LogDate.ToString("yyyy-MM-dd")
        }));
    }

    private static bool TryStr(JsonElement el, string key, out string val)
    {
        if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
        { val = p.GetString()!; return !string.IsNullOrWhiteSpace(val); }
        val = ""; return false;
    }
}

// ===== 周报查询 =====

public class ServerGetWeeklyReportsTool : ServerQueryTool
{
    private readonly MiraiNoteDbContext _db;
    public ServerGetWeeklyReportsTool(MiraiNoteDbContext db) { _db = db; }

    public override string Name => "get_weekly_reports";
    public override string Description => "获取用户已生成的工作周报。当用户询问周报时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["week_start"] = ToolParameterProperty.String("周报起始日期 yyyy-MM-dd，不填返回最近")
        }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        var q = _db.WeeklyReports.AsNoTracking().Where(r => r.UserId == userId);

        if (TryStr(args, "week_start", out var ws) && DateTime.TryParse(ws, out var weekStart))
            q = q.Where(r => r.WeekStart == weekStart.Date);

        var raw = await q.OrderByDescending(r => r.WeekStart).Take(5)
            .Select(r => new { r.Id, r.WeekStart, r.WeekEnd, r.Content, r.GeneratedAt })
            .ToListAsync(ct);
        if (raw.Count == 0) return "没有找到符合条件的周报。";

        return JsonSerializer.Serialize(raw.Select(r => new
        {
            id = r.Id, weekStart = r.WeekStart.ToString("yyyy-MM-dd"),
            weekEnd = r.WeekEnd.ToString("yyyy-MM-dd"), content = r.Content,
            generatedAt = r.GeneratedAt.ToString("yyyy-MM-dd")
        }));
    }

    private static bool TryStr(JsonElement el, string key, out string val)
    {
        if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
        { val = p.GetString()!; return !string.IsNullOrWhiteSpace(val); }
        val = ""; return false;
    }
}
