using System.Text.Json;
using MiraiNote.CLI.Services;

namespace MiraiNote.CLI.Agent.Tools;

/// <summary>
/// API 代理工具基类。所有需要调用 MiraiNote API 的工具继承此类。
/// </summary>
public abstract class ApiBackedTool : IAgentTool
{
    protected readonly ApiClient Api;
    protected ApiBackedTool(ApiClient api) { Api = api; }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract ToolParameterSchema Parameters { get; }
    public virtual ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public abstract Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default);
}

// ===== 工作记录查询 =====

public class SearchWorkLogsTool : ApiBackedTool
{
    public SearchWorkLogsTool(ApiClient api) : base(api) { }
    public override string Name => "search_work_logs";
    public override string Description =>
        "查询用户的工作记录。支持按日期范围、关键词、分类筛选。" +
        "当用户询问工作内容、进展、某天/某周做了什么、工作总结时调用。";

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

    public override async Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement;

        ToolArgHelper.TryGetString(args, "keyword", out var keyword);
        ToolArgHelper.TryGetString(args, "category", out var category);

        DateTime? from = null, to = null;
        if (ToolArgHelper.TryGetString(args, "date_from", out var df) && DateTime.TryParse(df, out var f)) from = f;
        if (ToolArgHelper.TryGetString(args, "date_to", out var dt) && DateTime.TryParse(dt, out var t)) to = t;

        var result = await Api.GetWorkLogsAsync(keyword, from, to, category, pageSize: 30);
        if (result.Items.Count == 0) return "没有找到符合条件的工作记录。";

        return JsonSerializer.Serialize(result.Items.Select(w => new
        {
            w.Id, w.Title, w.Purpose, w.Content, w.Tags, w.Category,
            logDate = w.LogDate.ToString("yyyy-MM-dd"),
            status = w.Status switch { 1 => "进行中", 2 => "已完成", 3 => "已延期", _ => "未标记" }
        }));
    }
}

// ===== 备忘查询 =====

public class SearchMemosTool : ApiBackedTool
{
    public SearchMemosTool(ApiClient api) : base(api) { }
    public override string Name => "search_memos";
    public override string Description =>
        "查询用户的备忘/待办事项。当用户询问备忘、待办、提醒、任务时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["section"] = ToolParameterProperty.String("板块：work 或 life，不填查全部"),
            ["keyword"] = ToolParameterProperty.String("关键词模糊匹配"),
            ["include_done"] = ToolParameterProperty.Boolean("是否包含已完成，默认 false"),
            ["include_archived"] = ToolParameterProperty.Boolean("是否包含已归档，默认 false")
        }
    };

    public override async Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement;

        ToolArgHelper.TryGetString(args, "section", out var section);
        if (string.IsNullOrWhiteSpace(section)) section = "work";
        ToolArgHelper.TryGetString(args, "keyword", out var keyword);
        ToolArgHelper.TryGetBool(args, "include_done", out var incDone);
        ToolArgHelper.TryGetBool(args, "include_archived", out var incArchived);

        var result = await Api.GetMemosAsync(section, keyword, incDone, incArchived, pageSize: 50);
        if (result.Items.Count == 0) return "没有找到符合条件的备忘。";

        return JsonSerializer.Serialize(result.Items.Select(m => new
        {
            m.Id, m.Section, m.Content,
            priority = m.Priority switch { 3 => "高", 2 => "中", _ => "低" },
            m.IsPinned, m.IsDone, m.IsArchived,
            remindAt = m.RemindAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        }));
    }
}

// ===== 生活记录查询 =====

public class SearchLifeLogsTool : ApiBackedTool
{
    public SearchLifeLogsTool(ApiClient api) : base(api) { }
    public override string Name => "search_life_logs";
    public override string Description =>
        "查询用户的生活记录/日记。当用户询问生活状态、某天经历、心情时调用。";

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

    public override async Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement;

        ToolArgHelper.TryGetString(args, "keyword", out var keyword);
        ToolArgHelper.TryGetString(args, "mood", out var mood);
        var month = "";
        if (ToolArgHelper.TryGetString(args, "date_from", out var df) && df.Length >= 7)
            month = df[..7];

        var result = await Api.GetLifeLogsAsync(keyword, mood, month, pageSize: 30);
        if (result.Items.Count == 0) return "没有找到符合条件的生活记录。";

        return JsonSerializer.Serialize(result.Items.Select(l => new
        {
            l.Id, l.Content, l.Mood, logDate = l.LogDate.ToString("yyyy-MM-dd")
        }));
    }
}

// ===== 周报查询 =====

public class GetWeeklyReportsTool : ApiBackedTool
{
    public GetWeeklyReportsTool(ApiClient api) : base(api) { }
    public override string Name => "get_weekly_reports";
    public override string Description => "获取已生成的工作周报。当用户询问周报时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["week_start"] = ToolParameterProperty.String("周报起始日期 yyyy-MM-dd，不填返回最近的")
        }
    };

    public override async Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
    {
        var reports = await Api.GetWeeklyReportsAsync();
        if (reports.Count == 0) return "暂无周报记录。";

        return JsonSerializer.Serialize(reports.Select(r => new
        {
            r.Id,
            weekStart = r.WeekStart.ToString("yyyy-MM-dd"),
            weekEnd = r.WeekEnd.ToString("yyyy-MM-dd"),
            r.Content,
            generatedAt = r.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        }));
    }
}

// ===== 周报生成 =====

public class GenerateWeeklyReportTool : ApiBackedTool
{
    public GenerateWeeklyReportTool(ApiClient api) : base(api) { }
    public override string Name => "generate_weekly_report";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public override string Description =>
        "生成指定周的工作周报。AI 会自动汇总本周工作记录生成周报。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["week_start"] = ToolParameterProperty.String("周起始日期 yyyy-MM-dd，不填默认本周一")
        },
        Required = new() { "week_start" }
    };

    public override async Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement;

        DateTime weekStart;
        if (ToolArgHelper.TryGetString(args, "week_start", out var ws) && DateTime.TryParse(ws, out var parsed))
            weekStart = parsed;
        else
        {
            var today = DateTime.Today;
            weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        }

        var report = await Api.GenerateWeeklyReportAsync(weekStart);
        return $"周报已生成（ID={report.Id}，周期 {report.WeekStart:yyyy-MM-dd} ~ {report.WeekEnd:yyyy-MM-dd}）：\n{report.Content}";
    }
}

// ===== 写操作：工作记录 =====

public class CreateWorkLogTool : ApiBackedTool
{
    public CreateWorkLogTool(ApiClient api) : base(api) { }
    public override string Name => "create_work_log";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public override string Description =>
        "创建一条工作记录。当用户明确要求记录工作内容、添加工作日志时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["title"] = ToolParameterProperty.String("工作记录标题（必填）"),
            ["log_date"] = ToolParameterProperty.String("记录日期 yyyy-MM-dd（必填）"),
            ["purpose"] = ToolParameterProperty.String("工作目的"),
            ["content"] = ToolParameterProperty.String("工作内容详情"),
            ["tags"] = ToolParameterProperty.String("标签，逗号分隔"),
            ["category"] = ToolParameterProperty.String("项目分类"),
            ["status"] = ToolParameterProperty.Integer("0=未标记 1=进行中 2=已完成 3=已延期，默认0")
        },
        Required = new() { "title", "log_date" }
    };

    public override async Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement;

        if (!ToolArgHelper.TryGetString(args, "title", out var title))
            return "创建失败：title 为必填项。";

        DateTime logDate = DateTime.Today;
        if (ToolArgHelper.TryGetString(args, "log_date", out var ds))
            DateTime.TryParse(ds, out logDate);

        ToolArgHelper.TryGetString(args, "purpose", out var purpose);
        ToolArgHelper.TryGetString(args, "content", out var content);
        ToolArgHelper.TryGetString(args, "tags", out var tags);
        ToolArgHelper.TryGetString(args, "category", out var category);
        ToolArgHelper.TryGetInt(args, "status", out var status);

        var created = await Api.CreateWorkLogAsync(new
        {
            title, logDate, purpose, content, tags, category, status
        });
        return $"已创建工作记录（ID={created.Id}）：《{created.Title}》，日期 {created.LogDate:yyyy-MM-dd}";
    }
}

// ===== 写操作：备忘 =====

public class CreateMemoTool : ApiBackedTool
{
    public CreateMemoTool(ApiClient api) : base(api) { }
    public override string Name => "create_memo";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public override string Description => "创建备忘/待办事项。用户要求记录提醒、待办时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["content"] = ToolParameterProperty.String("备忘内容（必填）"),
            ["section"] = ToolParameterProperty.String("work 或 life，默认 work"),
            ["priority"] = ToolParameterProperty.Integer("1=低 2=中 3=高，默认2"),
            ["is_pinned"] = ToolParameterProperty.Boolean("是否置顶"),
            ["remind_at"] = ToolParameterProperty.String("提醒时间 yyyy-MM-dd HH:mm")
        },
        Required = new() { "content" }
    };

    public override async Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement;

        if (!ToolArgHelper.TryGetString(args, "content", out var content))
            return "创建失败：content 为必填项。";

        ToolArgHelper.TryGetString(args, "section", out var section);
        if (string.IsNullOrWhiteSpace(section)) section = "work";
        ToolArgHelper.TryGetInt(args, "priority", out var priority);
        if (priority == 0) priority = 2;
        ToolArgHelper.TryGetBool(args, "is_pinned", out var isPinned);

        DateTime? remindAt = null;
        if (ToolArgHelper.TryGetString(args, "remind_at", out var rs) && DateTime.TryParse(rs, out var rd))
            remindAt = rd.ToUniversalTime();

        var created = await Api.CreateMemoAsync(new
        {
            section, content, priority, isPinned,
            remindAt = remindAt?.ToString("o"),
            remindMethods = remindAt.HasValue ? 1 : 0
        });
        return $"已创建备忘（ID={created.Id}）：{created.Content[..Math.Min(60, created.Content.Length)]}";
    }
}

// ===== 备忘状态修改 =====

public class PatchMemoStatusTool : ApiBackedTool
{
    public PatchMemoStatusTool(ApiClient api) : base(api) { }
    public override string Name => "patch_memo_status";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public override string Description => "切换备忘的完成/置顶状态。标记完成、取消完成时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["id"] = ToolParameterProperty.Integer("备忘 ID（必填）"),
            ["is_done"] = ToolParameterProperty.Boolean("是否标记为已完成"),
            ["is_pinned"] = ToolParameterProperty.Boolean("是否置顶")
        },
        Required = new() { "id" }
    };

    public override async Task<string> ExecuteAsync(string argsJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement;

        if (!ToolArgHelper.TryGetInt(args, "id", out var id))
            return "操作失败：id 为必填项。";

        bool? isDone = null, isPinned = null;
        if (ToolArgHelper.TryGetBool(args, "is_done", out var d)) isDone = d;
        if (ToolArgHelper.TryGetBool(args, "is_pinned", out var p)) isPinned = p;

        await Api.PatchMemoStatusAsync(id, new { isDone, isPinned });
        return $"备忘 ID={id} 状态已更新。";
    }
}
