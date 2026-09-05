using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MiraiNote.Core.Services.Mirai;

/// <summary>
/// DeepSeek 非流式 JSON 调用辅助（Mirai M1：分拣/晨报使用；
/// Chat/Agent 流式链路不经过此类）。
/// </summary>
internal static class DeepSeekJsonClient
{
    /// <summary>
    /// 调用 /v1/chat/completions（stream=false，可指定 response_format=json_object），
    /// 返回首个 choice 的 message.content。
    /// </summary>
    /// <param name="client">已完成鉴权配置的 HttpClient。</param>
    /// <param name="model">模型名。</param>
    /// <param name="messages">消息列表（匿名对象，属性 role/content）。</param>
    /// <param name="temperature">温度。</param>
    /// <param name="maxTokens">max_tokens。</param>
    /// <param name="jsonObject">true 时携带 response_format=json_object。</param>
    /// <param name="disableThinking">true 时显式关闭 DeepSeek 思考模式，避免短文案请求只消耗推理预算而没有正文。</param>
    /// <param name="timeout">本次调用整体超时。</param>
    /// <param name="ct">外部取消令牌。</param>
    public static async Task<string> CompleteAsync(
        HttpClient client,
        string model,
        IReadOnlyList<object> messages,
        double temperature,
        int maxTokens,
        bool jsonObject,
        TimeSpan timeout,
        CancellationToken ct,
        bool disableThinking = false)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens,
            ["stream"] = false
        };
        if (disableThinking) body["thinking"] = new { type = "disabled" };
        if (jsonObject) body["response_format"] = new { type = "json_object" };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, MiraiJson.Options), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(httpRequest, timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            throw new HttpRequestException(
                $"AI 服务错误 {(int)response.StatusCode}: {err[..Math.Min(200, err.Length)]}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeoutCts.Token));
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new JsonException("AI 响应缺少 choices");

        var message = choices[0].TryGetProperty("message", out var msg) ? msg : default;
        if (message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        throw new JsonException("AI 响应缺少 message.content");
    }

    /// <summary>构造已携带 Bearer 鉴权的 DeepSeek 客户端（复用命名客户端，避免每次建连）。</summary>
    public static HttpClient CreateAuthorizedClient(
        IHttpClientFactory factory, string baseUrl, string apiKey)
    {
        var client = factory.CreateClient("DeepSeek");
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }
}
