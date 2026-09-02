namespace MiraiNote.Shared.Agent;

/// <summary>
/// 工具注册表。管理所有 IAgentTool 实例，生成 Function Calling schema。
/// 同时支持 IAgentTool 和 IServerAgentTool。
/// </summary>
public class AgentToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools = new();

    public IReadOnlyCollection<IAgentTool> Tools => _tools.Values;

    public void Register(IAgentTool tool)
    {
        _tools[tool.Name] = tool;
    }

    public IAgentTool? Get(string name)
        => _tools.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>
    /// 生成符合 DeepSeek/OpenAI Function Calling 格式的工具列表。
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
}
