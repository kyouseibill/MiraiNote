using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiraiNote.CLI.Agent;

/// <summary>
/// 反思结果。
/// </summary>
public class ReflectionResult
{
    public bool IsComplete { get; set; }        // 任务目标是否达成
    public int Score { get; set; }              // 0-10 自评分
    public string[] Strengths { get; set; } = Array.Empty<string>();
    public string[] Issues { get; set; } = Array.Empty<string>();
    public string[] Suggestions { get; set; } = Array.Empty<string>();
    public bool NeedsFollowUp { get; set; }     // 是否需要补充操作
    public string? FollowUpAction { get; set; } // 补充操作描述

    public override string ToString()
    {
        var sb = new StringBuilder();
        var icon = IsComplete ? "✓" : "✗";
        sb.AppendLine($"{icon} 目标达成：{(IsComplete ? "是" : "否")}  自评：{Score}/10");

        if (Strengths.Length > 0)
        {
            sb.Append("  优点：");
            sb.AppendLine(string.Join("、", Strengths));
        }

        if (Issues.Length > 0)
        {
            sb.Append("  ⚠ 问题：");
            sb.AppendLine(string.Join("、", Issues));
        }

        if (Suggestions.Length > 0)
        {
            sb.Append("  💡 建议：");
            sb.AppendLine(string.Join("、", Suggestions));
        }

        if (NeedsFollowUp && !string.IsNullOrWhiteSpace(FollowUpAction))
            sb.AppendLine($"  → 将自动补充：{FollowUpAction}");

        return sb.ToString();
    }
}

/// <summary>
/// 自我反思器。
/// 任务执行完成后，调用 LLM 对自己的输出做质量检查。
/// 如果发现遗漏或问题，自动触发补充执行。
/// </summary>
public class AgentReflector
{
    private readonly AgentConfig _config;
    private readonly HttpClient _http;
    private readonly bool _enabled;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>超过此长度的消息才触发反思（短回复不需要）</summary>
    private const int MinReflectionLength = 100;

    public AgentReflector(AgentConfig config, bool enabled = true)
    {
        _config = config;
        _enabled = enabled;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.BaseAddress = new Uri(_config.DeepSeekBaseUrl);
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.DeepSeekApiKey);
    }

    /// <summary>
    /// 对 Agent 的最终回复做质量反思。
    /// </summary>
    /// <param name="userMessage">原始用户需求</param>
    /// <param name="assistantResponse">Agent 的最终回复</param>
    /// <param name="toolCallsCount">工具调用次数</param>
    /// <param name="ct"></param>
    /// <returns>反思结果，null 表示不需要反思</returns>
    public async Task<ReflectionResult?> ReflectAsync(
        string userMessage,
        string assistantResponse,
        int toolCallsCount,
        CancellationToken ct = default)
    {
        if (!_enabled) return null;

        // 太短的回复不反思
        if (assistantResponse.Length < MinReflectionLength) return null;

        // 工具调用超过 3 次的一般是复杂任务，值得反思
        bool isComplex = toolCallsCount > 3;

        var systemPrompt = """
            你是一个质量审查助手。请对以下 AI 回复进行质量评估。

            评估维度：
            1. 完整性：是否覆盖了用户需求的所有点？
            2. 正确性：事实陈述和数据是否来自工具结果，没有编造？
            3. 可用性：回复是否清晰、有组织、可直接使用？
            4. 安全性：是否避免了有风险的操作建议？

            请用 JSON 格式回复（不要其他文字）：
            {
              "is_complete": true/false,
              "score": 0-10,
              "strengths": ["做得好的点"],
              "issues": ["存在的问题"],
              "suggestions": ["改进建议"],
              "needs_follow_up": true/false,
              "follow_up_action": "如果需要补充，描述补充操作，否则null"
            }
            """;

        try
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"""
                    用户原始需求：{userMessage}

                    AI 的回复：
                    {assistantResponse[..Math.Min(3000, assistantResponse.Length)]}

                    请评估此回复的质量。
                    """ }
            };

            var body = JsonSerializer.Serialize(new
            {
                model = _config.DeepSeekModel,
                messages,
                temperature = 0.2,
                max_tokens = 1000,
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

            // 提取 JSON
            var json = content.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('{');
                var end = json.LastIndexOf('}');
                if (start >= 0 && end > start)
                    json = json[start..(end + 1)];
            }

            var result = JsonSerializer.Deserialize<ReflectionResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result;
        }
        catch
        {
            return null;
        }
    }
}
