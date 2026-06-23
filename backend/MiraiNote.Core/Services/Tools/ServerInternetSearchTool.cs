using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// 服务端互联网搜索工具（通过 Tavily API）。
/// </summary>
public class ServerSearchInternetTool : IServerAgentTool
{
    private readonly TavilyOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public string Name => "search_internet";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "搜索互联网公开信息。适用于：天气预报、新闻资讯、知识问答、产品/技术介绍、" +
        "政策法规、价格查询等与用户个人数据无关的问题。" +
        $"当前状态：" + (string.IsNullOrEmpty(_options.ApiKey) ? "未配置 API Key，不可用" : "已配置，可用");

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["query"] = ToolParameterProperty.String("搜索查询词（必填）")
        },
        Required = new() { "query" }
    };

    public ServerSearchInternetTool(IOptions<TavilyOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    // IAgentTool 兼容
    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return "互联网搜索功能未配置（Tavily API Key 为空）。";

        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "query", out var query))
            return "搜索失败：未提供 query 参数。";

        try
        {
            var client = _httpClientFactory.CreateClient("Tavily");
            var body = new
            {
                api_key = _options.ApiKey,
                query,
                max_results = _options.MaxResults,
                search_depth = "basic",
                include_answer = false,
                include_raw_content = false
            };

            var httpResp = await client.PostAsJsonAsync($"{_options.BaseUrl}/search", body, ct);
            if (!httpResp.IsSuccessStatusCode)
            {
                var err = await httpResp.Content.ReadAsStringAsync(ct);
                return $"互联网搜索失败（{(int)httpResp.StatusCode}）：{err[..Math.Min(200, err.Length)]}";
            }

            using var doc = JsonDocument.Parse(await httpResp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("results", out var resultsEl))
                return "搜索无结果。";

            var results = resultsEl.EnumerateArray().Select(r => new
            {
                title = r.TryGetProperty("title", out var t) ? t.GetString() : null,
                url = r.TryGetProperty("url", out var u) ? u.GetString() : null,
                content = r.TryGetProperty("content", out var c) ? c.GetString() : null,
                score = r.TryGetProperty("score", out var s) ? s.GetDouble() : 0.0
            }).ToList();

            if (results.Count == 0) return "搜索无结果。";
            return JsonSerializer.Serialize(results);
        }
        catch (TaskCanceledException)
        {
            return "搜索超时，请稍后重试。";
        }
    }
}
