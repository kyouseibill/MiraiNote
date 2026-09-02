using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Shared.Agent;

/// <summary>
/// DeepSeek-backed quality feedback generator.
/// </summary>
public class DeepSeekReflector : IAgentReflector
{
    private readonly DeepSeekConnection _conn;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const int MinReflectionLength = 100;

    public DeepSeekReflector(DeepSeekConnection conn, HttpClient? httpClient = null)
    {
        _conn = conn;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (_http.BaseAddress == null)
            _http.BaseAddress = new Uri(_conn.BaseUrl);
        if (_http.DefaultRequestHeaders.Authorization == null)
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _conn.ApiKey);
    }

    public async Task<ReflectionResult?> ReflectAsync(
        string userMessage,
        string assistantResponse,
        int toolCallsCount,
        CancellationToken ct = default,
        string? evaluationContext = null)
    {
        if (assistantResponse.Length < MinReflectionLength) return null;

        var systemPrompt = """
            你是 MiraiNote 的回答质量反馈器。目标不是批判 AI，而是给用户提供有用、简短、可执行的反馈。

            只在反馈确实有帮助时指出问题。优先关注：
            1. 是否遗漏用户当前问题的关键点。
            2. 是否需要补充必要的工具查询、网页/API 调用、日期确认或数据来源。
            3. 是否存在明显事实错误、无依据结论或与已知上下文冲突的内容。
            4. 是否有下一步能立刻改进结果。

            重要规则：
            - 如果“已知上下文/用户记忆”里支持某个用户偏好或事实，不要把它判定为编造。
            - 不要因为当前单轮用户消息没重复说明偏好，就否定长期记忆中的偏好。
            - 如果无法确定某事实是否有依据，用“可能需要补充依据/确认”表达，不要武断说“编造”。
            - 不要输出泛泛的优点，例如“结构清晰、语气友好”；strengths 只保留对用户有实际价值的点。
            - 如果没有实质问题，issues 和 suggestions 可以为空，is_complete 应为 true，score 应 >= 8。
            - 建议最多 2 条，问题最多 2 条，措辞面向用户，避免内部评审腔。

            请用 JSON 格式回复（不要其他文字）：
            {
              "is_complete": true/false,
              "score": 0-10,
              "strengths": ["已经做到且对用户有实际价值的点，可为空"],
              "issues": ["需要用户注意的实质问题，可为空"],
              "suggestions": ["下一步可执行的补充或改进，可为空"],
              "needs_follow_up": true/false,
              "follow_up_action": "如果需要自动补充，描述补充动作，否则 null"
            }
            """;

        try
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"""
                    用户当前需求：
                    {userMessage}

                    已知上下文/用户记忆/工具依据：
                    {(!string.IsNullOrWhiteSpace(evaluationContext) ? evaluationContext : "（无额外上下文）")}

                    工具调用次数：{toolCallsCount}

                    AI 的回复：
                    {assistantResponse[..Math.Min(3000, assistantResponse.Length)]}

                    请给出面向用户的简短质量反馈。
                    """ }
            };

            var body = JsonSerializer.Serialize(new
            {
                model = _conn.Model,
                messages,
                temperature = 0.1,
                max_tokens = 700,
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

            return JsonSerializer.Deserialize<ReflectionResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }
}
