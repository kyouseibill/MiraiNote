using System.Text.Json;
using MiraiNote.Shared.Agent;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// 服务端记忆工具：记住用户信息（remember）。
/// </summary>
public class ServerRememberTool : IServerAgentTool
{
    private readonly IAgentMemoryService _memoryService;

    public string Name => "remember";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "记住关于用户的重要信息（偏好、事实、习惯、重要日期等）。" +
        "当你发现用户分享了值得长期记住的信息时，主动调用此工具存储。" +
        "key 使用英文下划线命名（如 coffee_preference、birthday）。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["key"] = ToolParameterProperty.String("记忆键名，英文下划线命名（必填，如 work_style）"),
            ["value"] = ToolParameterProperty.String("记忆内容，中文描述（必填）"),
            ["category"] = ToolParameterProperty.Enum("分类", new() { "preference", "fact", "context", "pattern", "date" }),
            ["importance"] = ToolParameterProperty.Integer("重要性 1-5，默认 3")
        },
        Required = new() { "key", "value" }
    };

    public ServerRememberTool(IAgentMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "key", out var key))
            return "记忆失败：未提供 key。";
        if (!ToolArgHelper.TryGetString(args, "value", out var value))
            return "记忆失败：未提供 value。";

        var category = "context";
        if (args.TryGetProperty("category", out var cat) && cat.GetString() is string catStr)
            category = catStr;

        byte importance = 3;
        if (ToolArgHelper.TryGetInt(args, "importance", out var imp))
            importance = (byte)Math.Clamp(imp, 1, 5);

        try
        {
            var result = await _memoryService.CreateAsync(userId, new CreateMemoryRequest
            {
                Key = key,
                Value = value,
                Category = category,
                Importance = importance,
                Source = "manual"
            }, ct);

            return $"已记住：{key} = {value[..Math.Min(80, value.Length)]}（分类：{category}，重要性：{importance}）";
        }
        catch (Exception ex)
        {
            return $"记忆存储失败：{ex.Message}";
        }
    }
}

/// <summary>
/// 服务端记忆工具：检索用户记忆（recall）。
/// </summary>
public class ServerRecallTool : IServerAgentTool
{
    private readonly IAgentMemoryService _memoryService;

    public string Name => "recall";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "检索之前记住的用户信息。可按 key 精确查找，按 category 分类浏览，或按 keyword 模糊搜索。" +
        "当用户询问之前提到过的偏好、习惯、事实等内容时使用。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["key"] = ToolParameterProperty.String("记忆键名（精确查找，与 category/keyword 互斥）"),
            ["category"] = ToolParameterProperty.String("按分类筛选：preference / fact / context / pattern / date"),
            ["keyword"] = ToolParameterProperty.String("模糊搜索关键词（匹配 key、value、tags）")
        }
    };

    public ServerRecallTool(IAgentMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;

        try
        {
            // 精确 key 查找
            if (ToolArgHelper.TryGetString(args, "key", out var key))
            {
                var memory = await _memoryService.GetByKeyAsync(userId, key, ct);
                if (memory == null)
                    return $"未找到记忆：{key}。";
                return JsonSerializer.Serialize(new
                {
                    found = true,
                    memory = new
                    {
                        memory.Key, memory.Value, memory.Category,
                        memory.Tags, memory.Importance,
                        memory.LastAccessedAt, memory.CreatedAt
                    }
                });
            }

            // 关键词搜索
            string? keyword = null;
            string? category = null;
            ToolArgHelper.TryGetString(args, "keyword", out keyword);
            ToolArgHelper.TryGetString(args, "category", out category);

            var memories = await _memoryService.SearchAsync(userId, keyword, category, ct);

            if (memories.Count == 0)
                return "未找到相关记忆。";

            return JsonSerializer.Serialize(new
            {
                count = memories.Count,
                memories = memories.Select(m => new
                {
                    m.Key, m.Value, m.Category, m.Tags,
                    m.Importance, m.LastAccessedAt
                })
            });
        }
        catch (Exception ex)
        {
            return $"记忆检索失败：{ex.Message}";
        }
    }
}

/// <summary>
/// 服务端记忆工具：删除用户记忆（forget）。
/// </summary>
public class ServerForgetTool : IServerAgentTool
{
    private readonly IAgentMemoryService _memoryService;

    public string Name => "forget";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public string Description =>
        "删除之前记住的用户信息。仅当用户明确要求删除某条记忆时使用。" +
        "删除操作为软删除，可恢复。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["key"] = ToolParameterProperty.String("要删除的记忆键名（必填）")
        },
        Required = new() { "key" }
    };

    public ServerForgetTool(IAgentMemoryService memoryService)
    {
        _memoryService = memoryService;
    }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "key", out var key))
            return "删除失败：未提供 key。";

        try
        {
            await _memoryService.DeleteByKeyAsync(userId, key, ct);
            return $"已删除记忆：{key}。";
        }
        catch (Exception ex)
        {
            return $"删除记忆失败：{ex.Message}";
        }
    }
}
