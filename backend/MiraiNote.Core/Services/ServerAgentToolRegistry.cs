using System.Text.Json;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services;

/// <summary>
/// 服务端 Agent 工具注册表。
/// 管理所有 IServerAgentTool，生成 Function Calling schema，执行工具调度。
/// 替代 ChatService 中的 BuildTools() 和 ExecuteToolAsync() 硬编码逻辑。
/// </summary>
public class ServerAgentToolRegistry
{
    private readonly Dictionary<string, IServerAgentTool> _tools = new();

    public IReadOnlyCollection<IServerAgentTool> Tools => _tools.Values;

    public void Register(IServerAgentTool tool)
    {
        _tools[tool.Name] = tool;
    }

    public IServerAgentTool? Get(string name)
        => _tools.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>
    /// 生成符合 Function Calling 格式的工具定义。
    /// </summary>
    public object[] BuildToolDefinitions() =>
        _tools.Values.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = t.Parameters.ToSchemaObject()
            }
        }).ToArray();

    /// <summary>
    /// 执行工具。未找到或执行失败时返回错误信息。
    /// </summary>
    public async Task<string> ExecuteAsync(int userId, string toolName, string argsJson, CancellationToken ct = default)
    {
        var tool = Get(toolName);
        if (tool == null)
            return $"未知工具：{toolName}";

        try
        {
            return await tool.ExecuteAsync(userId, argsJson, ct);
        }
        catch (Exception ex)
        {
            return $"工具执行失败：{ex.Message}";
        }
    }
}
