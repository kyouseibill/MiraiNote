using System.Text.Json;
using MiraiNote.Shared.Agent;
using MiraiNote.Shared.Dtos.LifeLogs;
using MiraiNote.Shared.Dtos.Memos;
using MiraiNote.Shared.Dtos.WorkLogs;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// 基类：服务端写操作工具。
/// </summary>
public abstract class ServerWriteTool : IServerAgentTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract ToolParameterSchema Parameters { get; }
    public virtual ToolRiskLevel RiskLevel => ToolRiskLevel.Write;

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public abstract Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default);

    protected static bool TryStr(JsonElement el, string key, out string val)
    {
        if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
        { val = p.GetString()!; return !string.IsNullOrWhiteSpace(val); }
        val = ""; return false;
    }
}

// ===== 创建工作记录 =====

public class ServerCreateWorkLogTool : ServerWriteTool
{
    private readonly IWorkLogService _svc;
    public ServerCreateWorkLogTool(IWorkLogService svc) { _svc = svc; }
    public override string Name => "create_work_log";
    public override string Description =>
        "创建一条工作记录。当用户明确表达要记录工作内容、添加工作日志时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["title"] = ToolParameterProperty.String("工作记录标题（必填）"),
            ["log_date"] = ToolParameterProperty.String("记录日期 yyyy-MM-dd（必填）"),
            ["purpose"] = ToolParameterProperty.String("工作目的/背景"),
            ["content"] = ToolParameterProperty.String("工作内容详情"),
            ["tags"] = ToolParameterProperty.String("标签，逗号分隔"),
            ["category"] = ToolParameterProperty.String("项目分类"),
            ["status"] = ToolParameterProperty.Integer("状态：0=未标记 1=进行中 2=已完成 3=已延期"),
            ["status_remark"] = ToolParameterProperty.String("状态备注")
        },
        Required = new() { "title", "log_date" }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        if (!TryStr(args, "title", out var title)) return "创建失败：title 为必填项。";
        if (!TryStr(args, "log_date", out var ds)) return "创建失败：log_date 为必填项。";
        if (!DateTime.TryParse(ds, out var logDate)) return "创建失败：log_date 格式无效。";

        TryStr(args, "purpose", out var purpose);
        TryStr(args, "content", out var content);
        TryStr(args, "tags", out var tags);
        TryStr(args, "category", out var category);
        TryStr(args, "status_remark", out var statusRemark);
        byte status = 0;
        if (args.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Number)
            status = (byte)st.GetInt32();

        var dto = await _svc.CreateAsync(userId, new CreateWorkLogRequest
        {
            Title = title, Purpose = Nz(purpose), Content = Nz(content),
            Tags = Nz(tags), Category = Nz(category), LogDate = logDate,
            Status = status, StatusRemark = Nz(statusRemark)
        }, ct);
        return $"已成功创建工作记录（ID={dto.Id}）：《{dto.Title}》，日期：{dto.LogDate:yyyy-MM-dd}。";
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

// ===== 更新工作记录 =====

public class ServerUpdateWorkLogTool : ServerWriteTool
{
    private readonly IWorkLogService _svc;
    public ServerUpdateWorkLogTool(IWorkLogService svc) { _svc = svc; }
    public override string Name => "update_work_log";
    public override string Description =>
        "更新已有的工作记录。需要先通过 search_work_logs 获取记录 ID。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["id"] = ToolParameterProperty.Integer("要更新的工作记录 ID（必填）"),
            ["title"] = ToolParameterProperty.String("新标题（必填）"),
            ["log_date"] = ToolParameterProperty.String("记录日期 yyyy-MM-dd（必填）"),
            ["purpose"] = ToolParameterProperty.String("工作目的"),
            ["content"] = ToolParameterProperty.String("工作内容"),
            ["tags"] = ToolParameterProperty.String("标签"),
            ["category"] = ToolParameterProperty.String("项目分类"),
            ["status"] = ToolParameterProperty.Integer("状态：0-3"),
            ["status_remark"] = ToolParameterProperty.String("状态备注")
        },
        Required = new() { "id", "title", "log_date" }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "更新失败：id 为必填项。";
        var id = idEl.GetInt32();
        if (!TryStr(args, "title", out var title)) return "更新失败：title 为必填项。";
        if (!TryStr(args, "log_date", out var ds)) return "更新失败：log_date 为必填项。";
        if (!DateTime.TryParse(ds, out var logDate)) return "更新失败：log_date 格式无效。";

        TryStr(args, "purpose", out var purpose);
        TryStr(args, "content", out var content);
        TryStr(args, "tags", out var tags);
        TryStr(args, "category", out var category);
        TryStr(args, "status_remark", out var statusRemark);
        byte status = 0;
        if (args.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Number)
            status = (byte)st.GetInt32();

        var dto = await _svc.UpdateAsync(userId, id, new UpdateWorkLogRequest
        {
            Title = title, Purpose = Nz(purpose), Content = Nz(content),
            Tags = Nz(tags), Category = Nz(category), LogDate = logDate,
            Status = status, StatusRemark = Nz(statusRemark)
        }, ct);
        return $"已成功更新工作记录（ID={dto.Id}）：《{dto.Title}》。";
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

// ===== 删除工作记录 =====

public class ServerDeleteWorkLogTool : ServerWriteTool
{
    private readonly IWorkLogService _svc;
    public ServerDeleteWorkLogTool(IWorkLogService svc) { _svc = svc; }
    public override string Name => "delete_work_log";
    public override string Description =>
        "删除工作记录。必须在用户明确确认后才能调用。";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new() { ["id"] = ToolParameterProperty.Integer("要删除的记录 ID") },
        Required = new() { "id" }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "删除失败：id 为必填项。";
        await _svc.DeleteAsync(userId, idEl.GetInt32(), ct);
        return $"已成功删除工作记录（ID={idEl.GetInt32()}）。";
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

// ===== 创建备忘 =====

public class ServerCreateMemoTool : ServerWriteTool
{
    private readonly IMemoService _svc;
    public ServerCreateMemoTool(IMemoService svc) { _svc = svc; }
    public override string Name => "create_memo";
    public override string Description => "创建备忘/待办事项。用户要求记录提醒、待办时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["content"] = ToolParameterProperty.String("备忘内容（必填）"),
            ["section"] = ToolParameterProperty.String("板块：work（默认）或 life"),
            ["priority"] = ToolParameterProperty.Integer("1=低 2=中 3=高，默认2"),
            ["is_pinned"] = ToolParameterProperty.Boolean("是否置顶"),
            ["remind_at"] = ToolParameterProperty.String("提醒时间 yyyy-MM-dd HH:mm"),
            ["remind_methods"] = ToolParameterProperty.Integer("提醒方式：0=不提醒 1=弹窗 2=邮件 3=弹窗+邮件")
        },
        Required = new() { "content" }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        if (!TryStr(args, "content", out var content)) return "创建失败：content 为必填项。";
        TryStr(args, "section", out var section);
        if (string.IsNullOrWhiteSpace(section)) section = "work";

        byte priority = 2;
        if (args.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number)
            priority = (byte)p.GetInt32();
        bool isPinned = args.TryGetProperty("is_pinned", out var pin) && pin.ValueKind == JsonValueKind.True;
        DateTime? remindAt = null;
        if (TryStr(args, "remind_at", out var rs) && DateTime.TryParse(rs, out var rd))
            remindAt = rd.ToUniversalTime();
        byte remindMethods = 0;
        if (args.TryGetProperty("remind_methods", out var rm) && rm.ValueKind == JsonValueKind.Number)
            remindMethods = (byte)rm.GetInt32();

        var dto = await _svc.CreateAsync(userId, new CreateMemoRequest
        {
            Section = section, Content = content, Priority = priority,
            IsPinned = isPinned, RemindAt = remindAt, RemindMethods = remindMethods
        }, ct);
        return $"已成功创建备忘（ID={dto.Id}，板块={dto.Section}）：{dto.Content[..Math.Min(50, dto.Content.Length)]}";
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

// ===== 更新备忘 =====

public class ServerUpdateMemoTool : ServerWriteTool
{
    private readonly IMemoService _svc;
    public ServerUpdateMemoTool(IMemoService svc) { _svc = svc; }
    public override string Name => "update_memo";
    public override string Description =>
        "更新已有的备忘内容。需要先通过 search_memos 获取 ID。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["id"] = ToolParameterProperty.Integer("备忘 ID（必填）"),
            ["content"] = ToolParameterProperty.String("新备忘内容（必填）"),
            ["priority"] = ToolParameterProperty.Integer("1-3"),
            ["is_pinned"] = ToolParameterProperty.Boolean("是否置顶"),
            ["remind_at"] = ToolParameterProperty.String("提醒时间"),
            ["remind_methods"] = ToolParameterProperty.Integer("提醒方式 0-3")
        },
        Required = new() { "id", "content" }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "更新失败：id 为必填项。";
        var id = idEl.GetInt32();
        if (!TryStr(args, "content", out var content)) return "更新失败：content 为必填项。";

        byte priority = 2;
        if (args.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number)
            priority = (byte)p.GetInt32();
        bool isPinned = args.TryGetProperty("is_pinned", out var pin) && pin.ValueKind == JsonValueKind.True;
        DateTime? remindAt = null;
        if (TryStr(args, "remind_at", out var rs) && DateTime.TryParse(rs, out var rd))
            remindAt = rd.ToUniversalTime();
        byte remindMethods = 0;
        if (args.TryGetProperty("remind_methods", out var rm) && rm.ValueKind == JsonValueKind.Number)
            remindMethods = (byte)rm.GetInt32();

        var dto = await _svc.UpdateAsync(userId, id, new UpdateMemoRequest
        {
            Content = content, Priority = priority, IsPinned = isPinned,
            RemindAt = remindAt, RemindMethods = remindMethods
        }, ct);
        return $"已成功更新备忘（ID={dto.Id}）：{dto.Content[..Math.Min(50, dto.Content.Length)]}";
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

// ===== 修改备忘状态 =====

public class ServerPatchMemoStatusTool : ServerWriteTool
{
    private readonly IMemoService _svc;
    public ServerPatchMemoStatusTool(IMemoService svc) { _svc = svc; }
    public override string Name => "patch_memo_status";
    public override string Description =>
        "切换备忘的完成/置顶/归档状态。标记完成、归档备忘时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["id"] = ToolParameterProperty.Integer("备忘 ID（必填）"),
            ["is_done"] = ToolParameterProperty.Boolean("是否已完成"),
            ["is_pinned"] = ToolParameterProperty.Boolean("是否置顶"),
            ["is_archived"] = ToolParameterProperty.Boolean("是否归档")
        },
        Required = new() { "id" }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "操作失败：id 为必填项。";
        var id = idEl.GetInt32();

        bool? isDone = null, isPinned = null, isArchived = null;
        if (args.TryGetProperty("is_done", out var d) && d.ValueKind != JsonValueKind.Null) isDone = d.GetBoolean();
        if (args.TryGetProperty("is_pinned", out var p) && p.ValueKind != JsonValueKind.Null) isPinned = p.GetBoolean();
        if (args.TryGetProperty("is_archived", out var a) && a.ValueKind != JsonValueKind.Null) isArchived = a.GetBoolean();

        await _svc.PatchStatusAsync(userId, id, new PatchMemoStatusRequest
        {
            IsDone = isDone, IsPinned = isPinned, IsArchived = isArchived
        }, ct);
        return $"已成功更新备忘状态（ID={id}）。";
    }
}

// ===== 删除备忘 =====

public class ServerDeleteMemoTool : ServerWriteTool
{
    private readonly IMemoService _svc;
    public ServerDeleteMemoTool(IMemoService svc) { _svc = svc; }
    public override string Name => "delete_memo";
    public override string Description =>
        "删除备忘事项。必须在用户明确确认后才能调用。";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new() { ["id"] = ToolParameterProperty.Integer("要删除的备忘 ID") },
        Required = new() { "id" }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "删除失败：id 为必填项。";
        await _svc.DeleteAsync(userId, idEl.GetInt32(), ct);
        return $"已成功删除备忘（ID={idEl.GetInt32()}）。";
    }
}

// ===== 生活记录 CRUD =====

public class ServerCreateLifeLogTool : ServerWriteTool
{
    private readonly ILifeLogService _svc;
    public ServerCreateLifeLogTool(ILifeLogService svc) { _svc = svc; }
    public override string Name => "create_life_log";
    public override string Description =>
        "创建生活记录/日记。当用户要记录生活感悟、日常事件、心情时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["content"] = ToolParameterProperty.String("生活记录内容（必填）"),
            ["log_date"] = ToolParameterProperty.String("记录日期 yyyy-MM-dd（必填）"),
            ["mood"] = ToolParameterProperty.String("心情标签：开心/平静/疲惫/难过")
        },
        Required = new() { "content", "log_date" }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        if (!TryStr(args, "content", out var content)) return "创建失败：content 为必填项。";
        if (!TryStr(args, "log_date", out var ds)) return "创建失败：log_date 为必填项。";
        if (!DateTime.TryParse(ds, out var logDate)) return "创建失败：log_date 格式无效。";
        TryStr(args, "mood", out var mood);

        var dto = await _svc.CreateAsync(userId, new CreateLifeLogRequest
        {
            Content = content, Mood = string.IsNullOrWhiteSpace(mood) ? null : mood, LogDate = logDate
        }, ct);
        return $"已成功创建生活记录（ID={dto.Id}，日期：{dto.LogDate:yyyy-MM-dd}）。";
    }
}

public class ServerUpdateLifeLogTool : ServerWriteTool
{
    private readonly ILifeLogService _svc;
    public ServerUpdateLifeLogTool(ILifeLogService svc) { _svc = svc; }
    public override string Name => "update_life_log";
    public override string Description =>
        "更新已有的生活记录。需要先通过 search_life_logs 获取 ID。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["id"] = ToolParameterProperty.Integer("生活记录 ID（必填）"),
            ["content"] = ToolParameterProperty.String("新内容（必填）"),
            ["log_date"] = ToolParameterProperty.String("日期 yyyy-MM-dd（必填）"),
            ["mood"] = ToolParameterProperty.String("心情标签")
        },
        Required = new() { "id", "content", "log_date" }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "更新失败：id 为必填项。";
        var id = idEl.GetInt32();
        if (!TryStr(args, "content", out var content)) return "更新失败：content 为必填项。";
        if (!TryStr(args, "log_date", out var ds)) return "更新失败：log_date 为必填项。";
        if (!DateTime.TryParse(ds, out var logDate)) return "更新失败：log_date 格式无效。";
        TryStr(args, "mood", out var mood);

        var dto = await _svc.UpdateAsync(userId, id, new UpdateLifeLogRequest
        {
            Content = content, Mood = string.IsNullOrWhiteSpace(mood) ? null : mood, LogDate = logDate
        }, ct);
        return $"已成功更新生活记录（ID={dto.Id}，日期：{dto.LogDate:yyyy-MM-dd}）。";
    }
}

public class ServerDeleteLifeLogTool : ServerWriteTool
{
    private readonly ILifeLogService _svc;
    public ServerDeleteLifeLogTool(ILifeLogService svc) { _svc = svc; }
    public override string Name => "delete_life_log";
    public override string Description =>
        "删除生活记录。必须在用户明确确认后才能调用。";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new() { ["id"] = ToolParameterProperty.Integer("要删除的记录 ID") },
        Required = new() { "id" }
    };

    public override async Task<string> ExecuteAsync(int userId, string argsJson, CancellationToken ct)
    {
        var args = JsonDocument.Parse(argsJson).RootElement;
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "删除失败：id 为必填项。";
        await _svc.DeleteAsync(userId, idEl.GetInt32(), ct);
        return $"已成功删除生活记录（ID={idEl.GetInt32()}）。";
    }
}
