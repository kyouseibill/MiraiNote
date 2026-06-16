using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiraiNote.CLI.Agent;

/// <summary>
/// 执行计划。
/// </summary>
public class ExecutionPlan
{
    public string Goal { get; set; } = "";
    public List<PlanStep> Steps { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public bool IsTrivial { get; set; }  // 简单任务不需要展示计划

    public override string ToString()
    {
        if (Steps.Count == 0) return "（无计划）";
        var sb = new StringBuilder();
        sb.AppendLine($"目标：{Goal}");
        for (int i = 0; i < Steps.Count; i++)
            sb.AppendLine($"  {i + 1}. {Steps[i].Action}");
        if (Risks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("风险：");
            foreach (var r in Risks) sb.AppendLine($"  ⚠ {r}");
        }
        return sb.ToString();
    }
}

public class PlanStep
{
    public int Order { get; set; }
    public string Action { get; set; } = "";
    public string[] Tools { get; set; } = Array.Empty<string>();
    public string ExpectedOutput { get; set; } = "";
}

/// <summary>
/// 任务规划器。
/// 在 Agent 执行任务前，先用一个轻量级 LLM 调用生成执行计划。
/// 简单任务（如打招呼、单一查询）自动跳过规划阶段。
/// </summary>
public class AgentPlanner
{
    private readonly AgentConfig _config;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AgentPlanner(AgentConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.BaseAddress = new Uri(_config.DeepSeekBaseUrl);
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.DeepSeekApiKey);
    }

    /// <summary>
    /// 判断用户消息是否需要规划。
    /// 简单对话、单一工具查询等直接返回 null。
    /// </summary>
    public bool NeedsPlanning(string userMessage)
    {
        // 简单判断：消息中有多个动作词或复杂意图时触发规划
        var complexityHints = new[]
        {
            "然后", "接着", "之后", "再", "同时", "并且",
            "总结", "汇总", "分析", "检查", "对比", "生成",
            "写", "创建", "修改", "删除",
            "所有", "全部", "整个", "每个"
        };

        // 检查是否为简单对话
        var msg = userMessage.Trim();
        if (msg.Length < 20) return false; // 太短的通常是打招呼
        if (msg.StartsWith("你好") || msg.StartsWith("谢谢") || msg == "再见") return false;

        var hintCount = complexityHints.Count(h => msg.Contains(h, StringComparison.Ordinal));
        return hintCount >= 2 || msg.Length > 100;
    }

    /// <summary>
    /// 调用 LLM 生成执行计划。
    /// 使用非流式调用获取结构化计划。
    /// </summary>
    public async Task<ExecutionPlan?> GeneratePlanAsync(
        string userMessage,
        List<string> availableTools,
        List<AgentMessage>? history = null,
        CancellationToken ct = default)
    {
        if (!NeedsPlanning(userMessage))
            return null;

        var toolList = string.Join("、", availableTools);

        var systemPrompt = $"""
            你是一个任务规划助手。给定用户的需求和可用工具列表，生成一个简洁的执行计划。

            可用工具：{toolList}

            规则：
            1. 步骤数控制在 3-7 个之间
            2. 每个步骤指定预期使用的工具
            3. 如果任务很简单（如简单查询），标记 is_trivial 为 true
            4. 如果涉及破坏性操作（删除、shell），在 risks 中标注
            5. 只用 JSON 格式回复，不要其他文字
            """;

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        if (history != null && history.Count > 0)
        {
            // 只带最近 3 轮对话做上下文
            foreach (var m in history.TakeLast(6))
                messages.Add(new { role = m.Role, content = m.Content });
        }

        messages.Add(new { role = "user", content = $"用户需求：{userMessage}\n\n请用以下 JSON 格式回复：{{\"goal\":\"目标\",\"steps\":[{{\"order\":1,\"action\":\"做什么\",\"tools\":[\"tool_name\"],\"expected_output\":\"预期结果\"}}],\"risks\":[\"风险1\"],\"is_trivial\":false}}" });

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _config.DeepSeekModel,
                messages,
                temperature = 0.3,  // 低温度，稳定输出
                max_tokens = 2000,
                stream = false
            }, _jsonOpts);

            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var respJson = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(respJson);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content)) return null;

            // 提取 JSON（可能包裹在 ```json ... ``` 中）
            var json = content.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('{');
                var end = json.LastIndexOf('}');
                if (start >= 0 && end > start)
                    json = json[start..(end + 1)];
            }

            var plan = JsonSerializer.Deserialize<ExecutionPlan>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return plan?.IsTrivial == true ? null : plan;
        }
        catch
        {
            // 规划失败不影响执行，直接回退到无规划模式
            return null;
        }
    }
}
