using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Core.Services;

/// <summary>
/// 后端 Agent 规划器。调用 DeepSeek 为 Web 端 Agent 生成执行计划。
/// </summary>
public interface IAgentPlannerService
{
    Task<ExecutionPlan?> GeneratePlanAsync(string userMessage, List<string> availableTools, CancellationToken ct = default);
}

public class AgentPlannerService : IAgentPlannerService
{
    private readonly DeepSeekOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AgentPlannerService(IOptions<DeepSeekOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ExecutionPlan?> GeneratePlanAsync(string userMessage, List<string> availableTools, CancellationToken ct = default)
    {
        if (!NeedsPlanning(userMessage)) return null;

        var toolList = string.Join("、", availableTools);
        var client = _httpClientFactory.CreateClient("DeepSeek");

        var messages = new List<object>
        {
            new { role = "system", content = $"""
                你是一个任务规划助手。给定用户的需求和可用工具列表，生成一个简洁的执行计划。

                可用工具：{toolList}

                规则：
                1. 步骤数控制在 3-7 个之间
                2. 每个步骤指定预期使用的工具
                3. 如果任务很简单（如简单查询），标记 is_trivial 为 true
                4. 如果涉及破坏性操作，在 risks 中标注
                5. 只用 JSON 格式回复
                """ },
            new { role = "user", content = $"用户需求：{userMessage}\n\nJSON 回复格式：{{\"goal\":\"目标\",\"steps\":[{{\"order\":1,\"action\":\"做什么\",\"tools\":[\"tool_name\"],\"expected_output\":\"预期结果\"}}],\"risks\":[\"风险1\"],\"is_trivial\":false}}" }
        };

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                model = _options.Model,
                messages,
                temperature = 0.3,
                max_tokens = 2000,
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

            var plan = JsonSerializer.Deserialize<ExecutionPlan>(json, new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true });

            return plan?.IsTrivial == true ? null : plan;
        }
        catch { return null; }
    }

    private static bool NeedsPlanning(string msg)
    {
        if (msg.Length < 20) return false;
        if (msg.StartsWith("你好") || msg.StartsWith("谢谢") || msg == "再见") return false;

        var hints = new[] { "然后", "接着", "之后", "再", "同时", "并且", "总结", "汇总", "分析", "检查", "对比", "生成", "写", "创建", "修改", "删除", "所有", "全部", "整个", "每个" };
        return hints.Count(h => msg.Contains(h, StringComparison.Ordinal)) >= 2 || msg.Length > 100;
    }
}
