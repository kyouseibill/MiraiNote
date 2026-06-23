using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Shared.Agent;

/// <summary>
/// DeepSeek 实现的 Agent 反思器。
/// 任务执行完成后，调用 LLM 对自己的输出做质量检查。
/// 如果发现遗漏或问题，自动触发补充执行。
/// </summary>
public class DeepSeekReflector : IAgentReflector
{
    private readonly DeepSeekConnection _conn;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>超过此长度的消息才触发反思</summary>
    private const int MinReflectionLength = 100;

    /// <summary>
    /// 创建反思器。
    /// </summary>
    /// <param name="conn">DeepSeek API 连接参数</param>
    /// <param name="httpClient">可选的 HttpClient（默认创建新的，Timeout=30s）</param>
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
        CancellationToken ct = default)
    {
        if (assistantResponse.Length < MinReflectionLength) return null;

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
                model = _conn.Model,
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
