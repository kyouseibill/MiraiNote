using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Shared.Agent;

/// <summary>
/// DeepSeek Plan 请求的连接参数。
/// </summary>
public class DeepSeekConnection
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-v4-flash";

    public void Deconstruct(out string apiKey, out string baseUrl, out string model)
    {
        apiKey = ApiKey;
        baseUrl = BaseUrl;
        model = Model;
    }
}

/// <summary>
/// DeepSeek 实现的 Agent 规划器。
/// 在 Agent 执行任务前，用非流式 LLM 调用生成执行计划。
/// 简单任务（如打招呼、单一查询）自动跳过规划阶段。
/// </summary>
public class DeepSeekPlanner : IAgentPlanner
{
    private readonly DeepSeekConnection _conn;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 创建规划器。
    /// </summary>
    /// <param name="conn">DeepSeek API 连接参数</param>
    /// <param name="httpClient">可选的 HttpClient（默认创建新的，Timeout=30s）</param>
    public DeepSeekPlanner(DeepSeekConnection conn, HttpClient? httpClient = null)
    {
        _conn = conn;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (_http.BaseAddress == null)
            _http.BaseAddress = new Uri(_conn.BaseUrl);
        if (_http.DefaultRequestHeaders.Authorization == null)
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _conn.ApiKey);
    }

    public bool NeedsPlanning(string userMessage)
    {
        var complexityHints = new[]
        {
            "然后", "接着", "之后", "再", "同时", "并且",
            "总结", "汇总", "分析", "检查", "对比", "生成",
            "写", "创建", "修改", "删除",
            "所有", "全部", "整个", "每个"
        };

        var msg = userMessage.Trim();
        if (msg.Length < 20) return false;
        if (msg.StartsWith("你好") || msg.StartsWith("谢谢") || msg == "再见") return false;

        var hintCount = complexityHints.Count(h => msg.Contains(h, StringComparison.Ordinal));
        return hintCount >= 2 || msg.Length > 100;
    }

    public async Task<ExecutionPlan?> GeneratePlanAsync(
        string userMessage,
        List<string> availableTools,
        List<(string Role, string Content)>? history = null,
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
            foreach (var m in history.TakeLast(6))
                messages.Add(new { role = m.Role, content = m.Content });
        }

        messages.Add(new { role = "user", content = $"用户需求：{userMessage}\n\n请用以下 JSON 格式回复：{{\"goal\":\"目标\",\"steps\":[{{\"order\":1,\"action\":\"做什么\",\"tools\":[\"tool_name\"],\"expected_output\":\"预期结果\"}}],\"risks\":[\"风险1\"],\"is_trivial\":false}}" });

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _conn.Model,
                messages,
                temperature = 0.3,
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
            return null;
        }
    }
}
