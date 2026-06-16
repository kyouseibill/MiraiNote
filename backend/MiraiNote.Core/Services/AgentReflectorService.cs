using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Core.Services;

/// <summary>
/// 后端 Agent 反思器。对 Agent 回复做质量自检。
/// </summary>
public interface IAgentReflectorService
{
    Task<ReflectionResult?> ReflectAsync(string userMessage, string assistantResponse, int toolCallsCount, CancellationToken ct = default);
}

public class AgentReflectorService : IAgentReflectorService
{
    private readonly DeepSeekOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AgentReflectorService(IOptions<DeepSeekOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ReflectionResult?> ReflectAsync(string userMessage, string assistantResponse, int toolCallsCount, CancellationToken ct = default)
    {
        if (assistantResponse.Length < 100) return null;

        var client = _httpClientFactory.CreateClient("DeepSeek");

        var messages = new List<object>
        {
            new { role = "system", content = """
                你是一个质量审查助手。请对以下 AI 回复进行质量评估。

                评估维度：
                1. 完整性：是否覆盖了用户需求的所有点？
                2. 正确性：事实陈述和数据是否来自工具结果，没有编造？
                3. 可用性：回复是否清晰、有组织、可直接使用？
                4. 安全性：是否避免了有风险的操作建议？

                请用 JSON 格式回复：
                {
                  "is_complete": true/false,
                  "score": 0-10,
                  "strengths": ["做得好的点"],
                  "issues": ["存在的问题"],
                  "suggestions": ["改进建议"],
                  "needs_follow_up": true/false,
                  "follow_up_action": "如果需要补充，描述补充操作，否则null"
                }
                """ },
            new { role = "user", content = $"用户需求：{userMessage}\n\nAI 回复：{assistantResponse[..Math.Min(3000, assistantResponse.Length)]}\n\n请评估。" }
        };

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _options.Model,
                messages,
                temperature = 0.2,
                max_tokens = 1000,
                stream = false
            }, _jsonOpts);

            using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using var resp = await client.SendAsync(req, ct);
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
                if (start >= 0 && end > start) json = json[start..(end + 1)];
            }

            return JsonSerializer.Deserialize<ReflectionResult>(json, new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }
}
