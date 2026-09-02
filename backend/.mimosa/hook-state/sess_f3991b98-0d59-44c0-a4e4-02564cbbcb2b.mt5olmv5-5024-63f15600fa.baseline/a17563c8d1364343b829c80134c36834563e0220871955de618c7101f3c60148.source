using System.Text.Json;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// 创建定时任务工具。供 AI Agent 调用，让用户可以在对话中直接创建定时任务。
/// </summary>
public class ServerScheduleTaskTool : IServerAgentTool
{
    private readonly IScheduledTaskService _taskService;

    public string Name => "schedule_task";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public string Description =>
        "创建一个定时任务，在指定时间自动执行。用于：用户说「明天早上7点帮我查天气并发邮件」等场景。" +
        "需要提供任务描述和执行时间。执行时间支持：绝对时间（ISO 8601）、相对时间（如「1小时后」）。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["description"] = ToolParameterProperty.String("任务描述，详细说明要做什么（必填）"),
            ["execute_at"] = ToolParameterProperty.String("执行时间，ISO 8601 格式 UTC 时间（如 2026-06-19T07:00:00Z），必填"),
            ["notify_email"] = ToolParameterProperty.Boolean("完成后是否发邮件通知（默认 false）")
        },
        Required = new() { "description", "execute_at" }
    };

    public ServerScheduleTaskTool(IScheduledTaskService taskService)
    {
        _taskService = taskService;
    }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        if (!ToolArgHelper.TryGetString(args, "description", out var description))
            return "创建定时任务失败：未提供 description（任务描述）。";

        if (!ToolArgHelper.TryGetString(args, "execute_at", out var executeAtStr))
            return "创建定时任务失败：未提供 execute_at（执行时间）。";

        if (!DateTime.TryParse(executeAtStr, out var executeAt))
            return $"创建定时任务失败：无法解析执行时间「{executeAtStr}」，请使用 ISO 8601 格式，如 2026-06-19T07:00:00Z。";

        ToolArgHelper.TryGetBool(args, "notify_email", out var notifyEmail);

        try
        {
            var task = await _taskService.CreateAsync(userId, description, executeAt.ToUniversalTime(), notifyEmail, ct);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(task.ExecuteAt,
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"));
            return $"定时任务已创建（ID={task.Id}），将于 {localTime:yyyy-MM-dd HH:mm}（北京时间）自动执行。" +
                   (notifyEmail ? " 完成后将发送邮件通知。" : "");
        }
        catch (Exception ex)
        {
            return $"创建定时任务失败：{ex.Message}";
        }
    }
}

/// <summary>
/// 查询定时任务工具。列出用户的所有定时任务。
/// </summary>
public class ServerListScheduledTasksTool : IServerAgentTool
{
    private readonly IScheduledTaskService _taskService;

    public string Name => "list_scheduled_tasks";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "列出用户的定时任务。支持筛选待执行或全部任务。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["status"] = ToolParameterProperty.Enum("筛选状态", new() { "all", "pending" })
        }
    };

    public ServerListScheduledTasksTool(IScheduledTaskService taskService)
    {
        _taskService = taskService;
    }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        string status = "all";
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String)
                status = s.GetString() ?? "all";
        }
        catch { /* 无参数时默认 all */ }

        var tasks = status == "pending"
            ? await _taskService.GetPendingAsync(userId, ct)
            : await _taskService.GetAllAsync(userId, ct);

        if (tasks.Count == 0)
            return "没有定时任务。" + (status == "pending" ? "（可能有已完成的任务，尝试查询所有状态）" : "");

        var cstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var items = tasks.Select(t => new
        {
            id = t.Id,
            description = t.Description,
            executeAt = TimeZoneInfo.ConvertTimeFromUtc(t.ExecuteAt, cstZone).ToString("yyyy-MM-dd HH:mm"),
            status = t.Status,
            result = t.Result
        });
        return JsonSerializer.Serialize(items);
    }
}
