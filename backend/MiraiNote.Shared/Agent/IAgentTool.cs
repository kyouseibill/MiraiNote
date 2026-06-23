using System.Text.Json;

namespace MiraiNote.Shared.Agent;

/// <summary>
/// 工具风险等级。
/// </summary>
public enum ToolRiskLevel
{
    /// <summary>只读操作，无需用户确认</summary>
    Safe,
    /// <summary>写入操作，显示提示但不阻塞</summary>
    Write,
    /// <summary>破坏性操作，必须用户确认后才执行</summary>
    Dangerous
}

/// <summary>
/// 工具定义接口。每个工具实现此接口即可被 Agent 自动发现。
/// </summary>
public interface IAgentTool
{
    /// <summary>工具名称（与 Function Calling schema 中的 name 一致）</summary>
    string Name { get; }

    /// <summary>工具描述（给 LLM 看的）</summary>
    string Description { get; }

    /// <summary>工具风险等级</summary>
    ToolRiskLevel RiskLevel { get; }

    /// <summary>
    /// 参数 JSON Schema（properties + required）。
    /// 用于构建 Function Calling 的 tool 定义。
    /// </summary>
    ToolParameterSchema Parameters { get; }

    /// <summary>执行工具，传入 LLM 给的 arguments JSON 字符串，返回结果字符串。</summary>
    Task<string> ExecuteAsync(string argumentsJson, CancellationToken ct = default);
}

/// <summary>
/// 服务端工具接口。在 IAgentTool 基础上增加 userId 上下文。
/// </summary>
public interface IServerAgentTool : IAgentTool
{
    /// <summary>执行工具，传入当前用户 ID。</summary>
    Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default);
}

/// <summary>
/// 工具参数 JSON Schema 定义。
/// </summary>
public class ToolParameterSchema
{
    public Dictionary<string, ToolParameterProperty> Properties { get; init; } = new();
    public List<string> Required { get; init; } = new();

    public object ToSchemaObject() => new
    {
        type = "object",
        properties = Properties.ToDictionary(
            kv => kv.Key,
            kv => (object)new
            {
                type = kv.Value.Type,
                description = kv.Value.Description,
                @enum = kv.Value.EnumValues?.Count > 0 ? kv.Value.EnumValues : null
            }),
        required = Required.Count > 0 ? Required : null
    };
}

public class ToolParameterProperty
{
    public string Type { get; init; } = "string";
    public string Description { get; init; } = "";
    public List<string>? EnumValues { get; init; }

    public static ToolParameterProperty String(string desc) => new() { Type = "string", Description = desc };
    public static ToolParameterProperty Integer(string desc) => new() { Type = "integer", Description = desc };
    public static ToolParameterProperty Boolean(string desc) => new() { Type = "boolean", Description = desc };
    public static ToolParameterProperty Enum(string desc, List<string> values) => new() { Type = "string", Description = desc, EnumValues = values };
}

/// <summary>
/// 工具参数解析辅助方法。
/// </summary>
public static class ToolArgHelper
{
    public static bool TryGetString(JsonElement el, string key, out string value)
    {
        if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString()!;
            return !string.IsNullOrWhiteSpace(value);
        }
        value = string.Empty;
        return false;
    }

    public static bool TryGetInt(JsonElement el, string key, out int value)
    {
        if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            value = prop.GetInt32();
            return true;
        }
        value = 0;
        return false;
    }

    public static bool TryGetBool(JsonElement el, string key, out bool value)
    {
        if (el.TryGetProperty(key, out var prop) && (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
        {
            value = prop.GetBoolean();
            return true;
        }
        value = false;
        return false;
    }
}
