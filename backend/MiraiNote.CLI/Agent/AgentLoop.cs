using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiraiNote.CLI.Services;

namespace MiraiNote.CLI.Agent;

/// <summary>
/// Agent 配置
/// </summary>
public class AgentConfig
{
    public string DeepSeekApiKey { get; set; } = string.Empty;
    public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";
    public string DeepSeekModel { get; set; } = "deepseek-chat";
    public string? TavilyApiKey { get; set; }
    public int MaxToolRounds { get; set; } = 12;
    public int MaxRetriesPerTool { get; set; } = 2;
}

/// <summary>
/// Agent 运行选项。
/// </summary>
public class AgentRunOptions
{
    /// <summary>是否启用 Planner（默认 true）</summary>
    public bool EnablePlanner { get; set; } = true;

    /// <summary>是否启用 Reflector（默认 true）</summary>
    public bool EnableReflector { get; set; } = true;

    /// <summary>是否跳过破坏性操作的确认（默认 false，即需要确认）</summary>
    public bool SkipConfirmation { get; set; } = false;

    /// <summary>自定义确认回调（返回 true 表示确认）</summary>
    public Func<ToolRiskLevel, string, string, Task<bool>>? ConfirmCallback { get; set; }
}

/// <summary>
/// Agent 运行结果。
/// </summary>
public class AgentRunResult
{
    public string Content { get; set; } = "";
    public int ToolCallsCount { get; set; }
    public ExecutionPlan? Plan { get; set; }
    public ReflectionResult? Reflection { get; set; }
}

/// <summary>
/// ReAct Agent 循环引擎 v2。
/// 集成 Planner、Reflector、Guard 确认机制、上下文管理。
/// 直接调用 DeepSeek API，通过 Function Calling 执行工具。
/// </summary>
public class AgentLoop
{
    private readonly AgentConfig _config;
    private readonly AgentToolRegistry _registry;
    private readonly AgentDisplay _display;
    private readonly HttpClient _http;
    private readonly TokenStore _tokenStore;
    private readonly AgentPlanner _planner;
    private readonly AgentReflector _reflector;
    private readonly AgentContextManager _contextManager;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AgentLoop(
        AgentConfig config,
        AgentToolRegistry registry,
        AgentDisplay display,
        TokenStore tokenStore,
        AgentPlanner? planner = null,
        AgentReflector? reflector = null,
        AgentContextManager? contextManager = null)
    {
        _config = config;
        _registry = registry;
        _display = display;
        _tokenStore = tokenStore;
        _planner = planner ?? new AgentPlanner(config);
        _reflector = reflector ?? new AgentReflector(config);
        _contextManager = contextManager ?? new AgentContextManager();
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <summary>
    /// 运行完整 Agent 流程：Plan → Execute → Reflect → FollowUp。
    /// </summary>
    public async Task<AgentRunResult> RunWithPlanAsync(
        string userMessage,
        List<AgentMessage> history,
        AgentRunOptions options,
        Func<string, Task>? onToken = null,
        CancellationToken ct = default)
    {
        var result = new AgentRunResult();

        // ── 阶段 0：上下文检查 ──
        if (_contextManager.NeedsCompaction(history, _registry.Tools.Count))
        {
            var usage = _contextManager.GetUsage(history, _registry.Tools.Count);
            _display.ShowProgress($"上下文用量 {usage.PercentUsed}%，正在压缩历史...");
            var compacted = _contextManager.Compact(history);
            history.Clear();
            history.AddRange(compacted);
            _display.ShowProgress($"压缩完成，{history.Count} 条消息");
        }

        // ── 阶段 1：Plan ──
        if (options.EnablePlanner)
        {
            var toolNames = _registry.Tools.Select(t => t.Name).ToList();
            result.Plan = await _planner.GeneratePlanAsync(userMessage, toolNames, history, ct);

            if (result.Plan != null)
            {
                _display.ShowPlan(result.Plan);
            }
        }

        // ── 阶段 2：Execute ──
        history.Add(new AgentMessage("user", userMessage));
        result.Content = await RunReActLoopAsync(history, options, onToken, result, ct);
        history.Add(new AgentMessage("assistant", result.Content));

        // ── 阶段 3：Reflect ──
        if (options.EnableReflector && result.Content.Length > 100)
        {
            _display.ShowProgress("正在反思中...");
            result.Reflection = await _reflector.ReflectAsync(
                userMessage, result.Content, result.ToolCallsCount, ct);

            if (result.Reflection != null)
            {
                _display.ShowReflection(result.Reflection);

                // 反思发现需要补充执行
                if (result.Reflection.NeedsFollowUp && !string.IsNullOrWhiteSpace(result.Reflection.FollowUpAction))
                {
                    _display.ShowProgress($"自动补充：{result.Reflection.FollowUpAction}");
                    history.Add(new AgentMessage("user",
                        $"请根据以下反馈改进：{result.Reflection.FollowUpAction}"));
                    var followUp = await RunReActLoopAsync(history, options, onToken, result, ct);
                    history.Add(new AgentMessage("assistant", followUp));
                    result.Content = followUp;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 运行一次 Agent 交互（简化版，向后兼容）。
    /// </summary>
    public async Task<string> RunAsync(
        List<AgentMessage> messages,
        Func<string, Task>? onToken = null,
        CancellationToken ct = default)
    {
        var options = new AgentRunOptions
        {
            EnablePlanner = false,
            EnableReflector = false,
            SkipConfirmation = true
        };
        var result = await RunReActLoopAsync(messages, options, null, new AgentRunResult(), ct);
        return result;
    }

    /// <summary>
    /// ReAct 循环核心：LLM 决策 → 工具执行 → 结果反馈，最多 N 轮。
    /// </summary>
    private async Task<string> RunReActLoopAsync(
        List<AgentMessage> messages,
        AgentRunOptions options,
        Func<string, Task>? onToken,
        AgentRunResult runResult,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.DeepSeekApiKey))
            throw new InvalidOperationException("DeepSeek API Key 未配置。请设置环境变量 DEEPSEEK_API_KEY。");

        _http.BaseAddress = new Uri(_config.DeepSeekBaseUrl);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _config.DeepSeekApiKey);

        var toolDefs = _registry.BuildToolDefinitions();
        var fullMessages = new List<object> { new { role = "system", content = BuildSystemPrompt() } };
        fullMessages.AddRange(messages.Select(m => (object)new { role = m.Role, content = m.Content }));
        var finalContent = new StringBuilder();

        for (int round = 0; round < _config.MaxToolRounds; round++)
        {
            var body = new
            {
                model = _config.DeepSeekModel,
                messages = fullMessages,
                tools = toolDefs,
                tool_choice = "auto",
                stream = true
            };

            var bodyJson = JsonSerializer.Serialize(body, _jsonOpts);
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };

            using var httpResp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!httpResp.IsSuccessStatusCode)
            {
                var err = await httpResp.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"DeepSeek API 错误 {(int)httpResp.StatusCode}: {err[..Math.Min(300, err.Length)]}");
            }

            var (content, toolCalls, finishReason) = await ParseStreamAsync(
                await httpResp.Content.ReadAsStreamAsync(ct), onToken, ct);

            if (!string.IsNullOrEmpty(content))
                finalContent.Append(content);

            if (finishReason == "stop" || finishReason == "length")
                return finalContent.ToString();

            if (finishReason == "tool_calls" && toolCalls.Count > 0)
            {
                var assistantObj = new Dictionary<string, object?>
                {
                    ["role"] = "assistant",
                    ["content"] = string.IsNullOrEmpty(content) ? null : content
                };

                var tcArray = toolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.FunctionName, arguments = tc.Arguments }
                }).ToArray();
                assistantObj["tool_calls"] = tcArray;
                fullMessages.Add(assistantObj);

                foreach (var tc in toolCalls)
                {
                    // ── Guard：风险检查 ──
                    var tool = _registry.Get(tc.FunctionName);
                    if (tool != null && tool.RiskLevel != ToolRiskLevel.Safe)
                    {
                        if (!options.SkipConfirmation && options.ConfirmCallback != null)
                        {
                            var confirmed = await options.ConfirmCallback(
                                tool.RiskLevel, tc.FunctionName, tc.Arguments);
                            if (!confirmed)
                            {
                                fullMessages.Add(new
                                {
                                    role = "tool",
                                    tool_call_id = tc.Id,
                                    content = "用户拒绝了此操作。请寻找替代方案。"
                                });
                                continue;
                            }
                        }

                        _display.ShowToolCall(tc.FunctionName, tc.Arguments);
                    }
                    else
                    {
                        _display.ShowToolCall(tc.FunctionName, tc.Arguments);
                    }

                    var result = await ExecuteToolWithRetryAsync(tc.FunctionName, tc.Arguments, ct);
                    runResult.ToolCallsCount++;
                    _display.ShowToolResult(result);

                    fullMessages.Add(new
                    {
                        role = "tool",
                        tool_call_id = tc.Id,
                        content = result
                    });
                }
            }
            else
            {
                break;
            }
        }

        return finalContent.Length > 0
            ? finalContent.ToString()
            : "抱歉，处理请求时超出工具调用轮次限制，请尝试简化问题。";
    }

    /// <summary>
    /// 解析 DeepSeek SSE 流式响应。
    /// </summary>
    private async Task<(string content, List<ToolCallDelta> toolCalls, string finishReason)>
        ParseStreamAsync(Stream stream, Func<string, Task>? onToken, CancellationToken ct)
    {
        var content = new StringBuilder();
        var toolCalls = new Dictionary<int, ToolCallDelta>();
        var finishReason = "";

        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var json = line[6..];
            if (json == "[DONE]") break;

            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) continue;
            var choice = choices[0];

            if (choice.TryGetProperty("finish_reason", out var frEl) &&
                frEl.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(frEl.GetString()))
            {
                finishReason = frEl.GetString()!;
                if (finishReason == "stop" || finishReason == "length") break;
            }

            var delta = choice.GetProperty("delta");

            if (delta.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                var token = contentEl.GetString();
                if (!string.IsNullOrEmpty(token))
                {
                    content.Append(token);
                    if (onToken != null) await onToken(token);
                }
            }

            if (delta.TryGetProperty("tool_calls", out var tcDeltaEl) &&
                tcDeltaEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tcEl in tcDeltaEl.EnumerateArray())
                {
                    var index = tcEl.GetProperty("index").GetInt32();
                    if (!toolCalls.ContainsKey(index))
                        toolCalls[index] = new ToolCallDelta();

                    var tc = toolCalls[index];
                    if (tcEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        tc.Id = idEl.GetString()!;
                    if (tcEl.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                        tc.Type = typeEl.GetString()!;
                    if (tcEl.TryGetProperty("function", out var funcEl))
                    {
                        if (funcEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                            tc.FunctionName = nameEl.GetString()!;
                        if (funcEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                            tc.Arguments += argsEl.GetString();
                    }
                }
            }
        }

        return (
            content.ToString(),
            toolCalls.Values.Where(t => !string.IsNullOrEmpty(t.Id)).ToList(),
            finishReason
        );
    }

    /// <summary>
    /// 执行工具，失败时自动重试。
    /// </summary>
    private async Task<string> ExecuteToolWithRetryAsync(string toolName, string argsJson, CancellationToken ct)
    {
        var tool = _registry.Get(toolName);
        if (tool == null)
            return $"未知工具：{toolName}";

        Exception? lastEx = null;
        for (int attempt = 1; attempt <= _config.MaxRetriesPerTool + 1; attempt++)
        {
            try { return await tool.ExecuteAsync(argsJson, ct); }
            catch (Exception ex)
            {
                lastEx = ex;
                if (attempt <= _config.MaxRetriesPerTool)
                {
                    _display.ShowToolCall(toolName, argsJson, attempt + 1);
                    await Task.Delay(500 * attempt, ct);
                }
            }
        }
        return $"工具执行失败（重试 {_config.MaxRetriesPerTool} 次后）：{lastEx?.Message}";
    }

    /// <summary>
    /// 构建系统提示词。
    /// </summary>
    private string BuildSystemPrompt()
    {
        var cstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cstZone);
        var today = now.ToString("yyyy-MM-dd");
        var weekday = now.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一", DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三", DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五", DayOfWeek.Saturday => "周六",
            _ => "周日"
        };
        var daysFromMon = ((int)now.DayOfWeek + 6) % 7;
        var weekMon = now.AddDays(-daysFromMon).ToString("yyyy-MM-dd");
        var weekSun = now.AddDays(6 - daysFromMon).ToString("yyyy-MM-dd");

        return $"""
            你是 MiraiNote 个人助理 Agent，基于 DeepSeek 模型运行。
            你不是 Claude、GPT 或其他任何第三方 AI 产品。你的名称是 "Mirai"。

            【当前时间】今天是 {today}（{weekday}），本周范围：{weekMon} 至 {weekSun}。

            ══════════════════════════════════════════════
            【核心能力】
            ══════════════════════════════════════════════
            你是一个功能强大的 Agent，能够：
            1. 查询和管理用户的工作记录、备忘事项、生活记录
            2. 搜索互联网获取最新信息
            3. 读写本地文件
            4. 执行 Shell 命令（仅限安全操作）
            5. 获取系统信息

            ══════════════════════════════════════════════
            【操作原则】
            ══════════════════════════════════════════════
            1. 所有数据操作必须通过工具完成，严禁凭空描述。
            2. 涉及用户数据的问题，必须先调用工具查询真实数据。
            3. 写操作（创建/修改/删除）要在工具返回确认后才能告知用户。
            4. 删除操作必须先向用户确认。
            5. 遇到信息不足时，礼貌询问补全。
            6. 当用户要求完成一个复杂任务时，自动分解为多个步骤并按顺序执行。

            ══════════════════════════════════════════════
            【输出质量要求】
            ══════════════════════════════════════════════
            1. 回复要完整覆盖用户的所有需求点。
            2. 数据来自工具查询结果，禁止编造。
            3. 使用清晰的结构（标题、列表、表格）。
            4. 需要操作时说明做了什么、结果如何。

            【查询策略】
            - 问题涉及"今天/本周/某日期"时，所有数据源均需查询。
            - 跨数据源查询时，汇总所有结果后再作答。
            - 需要最新信息（天气、新闻、技术资料）时调用互联网搜索。

            【时间换算（基于今天 {today}）】
            - "今天" → date_from = date_to = {today}
            - "本周" → date_from = {weekMon}，date_to = {weekSun}
            - "昨天" → date_from = date_to = {now.AddDays(-1):yyyy-MM-dd}
            - "最近 7 天" → date_from = {now.AddDays(-7):yyyy-MM-dd}，date_to = {today}
            - "本月" → date_from = {now:yyyy-MM}-01，date_to = {today}

            【当前 API 服务】{_tokenStore.ApiBase}
            """;
    }
}

/// <summary>
/// Agent 消息模型。
/// </summary>
public class AgentMessage
{
    public string Role { get; init; } = "user";
    public string Content { get; init; } = "";

    public AgentMessage() { }
    public AgentMessage(string role, string content) { Role = role; Content = content; }
}

/// <summary>
/// 流式解析中的 tool_call delta 信息。
/// </summary>
public class ToolCallDelta
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "function";
    public string FunctionName { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}
