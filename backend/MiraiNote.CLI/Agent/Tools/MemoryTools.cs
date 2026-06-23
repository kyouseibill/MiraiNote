using System.Text.Json;
using MiraiNote.CLI.Services;
using MiraiNote.Shared.Agent;

namespace MiraiNote.CLI.Agent.Tools;

/// <summary>
/// 记忆存储工具。让 Agent 记住用户偏好和上下文信息。
/// </summary>
public class RememberTool : ApiBackedTool
{
    public RememberTool(ApiClient api) : base(api) { }
    public override string Name => "remember";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public override string Description =>
        "存储一条记忆。用于记住用户偏好、上下文、常用操作等信息。" +
        "当用户说\"记住...\"、\"我习惯...\"、\"我常用...\"时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["key"] = ToolParameterProperty.String("记忆键（必填），用于后续检索，如 pref_editor、ctx_project"),
            ["value"] = ToolParameterProperty.String("记忆内容（必填）"),
            ["category"] = ToolParameterProperty.Enum("分类", new() { "preference", "context", "fact", "command" })
        },
        Required = new() { "key", "value" }
    };

    public override async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        if (!ToolArgHelper.TryGetString(args, "key", out var key))
            return "存储失败：key 为必填项。";
        if (!ToolArgHelper.TryGetString(args, "value", out var value))
            return "存储失败：value 为必填项。";

        ToolArgHelper.TryGetString(args, "category", out var category);
        if (string.IsNullOrWhiteSpace(category)) category = "context";

        try
        {
            var resp = await Api.PostAsync<object>($"/api/v1/agent/memories",
                new { key, value, category });
            return $"已记住：{key} → {value[..Math.Min(80, value.Length)]}";
        }
        catch (ApiException ex)
        {
            return $"存储失败：{ex.Message}";
        }
    }
}

/// <summary>
/// 记忆检索工具。
/// </summary>
public class RecallTool : ApiBackedTool
{
    public RecallTool(ApiClient api) : base(api) { }
    public override string Name => "recall";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public override string Description =>
        "检索 Agent 记忆。用于查询之前存储的用户偏好、上下文信息。" +
        "当需要了解用户习惯、上次工作上下文、常用设置时调用。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["key"] = ToolParameterProperty.String("记忆键，不填则返回最近的重要记忆"),
            ["category"] = ToolParameterProperty.String("按分类筛选：preference/context/fact/command")
        }
    };

    public override async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        ToolArgHelper.TryGetString(args, "key", out var key);
        ToolArgHelper.TryGetString(args, "category", out var category);

        try
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                var resp = await Api.GetAsync<object>($"/api/v1/agent/memories/{Uri.EscapeDataString(key)}");
                return JsonSerializer.Serialize(resp);
            }
            else
            {
                var qs = string.IsNullOrWhiteSpace(category) ? "" : $"?category={Uri.EscapeDataString(category)}";
                var resp = await Api.GetAsync<object>($"/api/v1/agent/memories{qs}");
                return JsonSerializer.Serialize(resp);
            }
        }
        catch (ApiException ex)
        {
            return $"检索失败：{ex.Message}";
        }
    }
}

/// <summary>
/// 记忆删除工具。
/// </summary>
public class ForgetTool : ApiBackedTool
{
    public ForgetTool(ApiClient api) : base(api) { }
    public override string Name => "forget";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;
    public override string Description =>
        "删除一条 Agent 记忆。当用户说\"忘记...\"或需要清除某条记忆时调用。" +
        "删除操作不可恢复，请先向用户确认。";

    public override ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["key"] = ToolParameterProperty.String("要删除的记忆键（必填）")
        },
        Required = new() { "key" }
    };

    public override async Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        if (!ToolArgHelper.TryGetString(args, "key", out var key))
            return "删除失败：key 为必填项。";

        try
        {
            await Api.DeleteAsync($"/api/v1/agent/memories/key/{Uri.EscapeDataString(key)}");
            return $"已删除记忆：{key}";
        }
        catch (ApiException ex)
        {
            return $"删除失败：{ex.Message}";
        }
    }
}
