using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services;

/// <summary>
/// 定时任务执行后台服务。每 30 秒扫描一次到期任务，
/// 调用 DeepSeek API（含 Function Calling）执行，完成后可选发邮件通知。
/// </summary>
public class ScheduledTaskExecutionService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int MaxToolRounds = 8;

    private readonly IServiceProvider _services;
    private readonly ILogger<ScheduledTaskExecutionService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DeepSeekOptions _deepSeekOpts;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ScheduledTaskExecutionService(
        IServiceProvider services,
        ILogger<ScheduledTaskExecutionService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<DeepSeekOptions> deepSeekOpts)
    {
        _services = services;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _deepSeekOpts = deepSeekOpts.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("定时任务执行服务已启动，扫描周期：{Interval}", PollInterval);

        await Task.Delay(5000, stoppingToken); // 启动延迟

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndExecuteAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "定时任务扫描异常");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollAndExecuteAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var taskService = scope.ServiceProvider.GetRequiredService<IScheduledTaskService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var toolRegistry = scope.ServiceProvider.GetRequiredService<ServerAgentToolRegistry>();

        var dueTasks = await taskService.GetDueTasksAsync(ct);
        if (dueTasks.Count == 0) return;

        _logger.LogInformation("发现 {Count} 个到期任务，开始执行", dueTasks.Count);

        foreach (var task in dueTasks)
        {
            if (ct.IsCancellationRequested) break;

            _logger.LogInformation("执行任务 {TaskId}: {Description}", task.Id, task.Description);

            try
            {
                await taskService.MarkRunningAsync(task.Id, ct);

                var result = await ExecuteAITaskAsync(task.Description, task.UserId, toolRegistry, ct);

                await taskService.MarkCompletedAsync(task.Id, result, ct);
                _logger.LogInformation("任务 {TaskId} 执行成功", task.Id);

                // 邮件通知
                if (task.NotifyEmail && task.User != null && !string.IsNullOrWhiteSpace(task.User.Email))
                {
                    try
                    {
                        await emailService.SendScheduledTaskResultAsync(
                            task.User.Email, task.User.Username, task.Description, result, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "发送任务 {TaskId} 结果邮件失败", task.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "任务 {TaskId} 执行失败", task.Id);
                await taskService.MarkFailedAsync(task.Id, ex.Message, ct);
            }
        }
    }

    /// <summary>
    /// 调用 DeepSeek API（含 Function Calling 循环）执行任务描述。
    /// </summary>
    private async Task<string> ExecuteAITaskAsync(
        string description, int userId, ServerAgentToolRegistry toolRegistry, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_deepSeekOpts.ApiKey))
            throw new InvalidOperationException("DeepSeek API Key 未配置，无法执行定时任务");

        var client = _httpClientFactory.CreateClient("DeepSeek");
        client.BaseAddress = new Uri(_deepSeekOpts.BaseUrl);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _deepSeekOpts.ApiKey);

        var tools = toolRegistry.BuildToolDefinitions();
        var messages = new List<object>
        {
            new { role = "system", content = BuildTaskSystemPrompt() },
            new { role = "user", content = description }
        };

        for (int round = 0; round < MaxToolRounds; round++)
        {
            var body = new
            {
                model = _deepSeekOpts.Model,
                messages,
                tools,
                tool_choice = "auto"
            };

            var bodyJson = JsonSerializer.Serialize(body, _jsonOpts);
            var httpReq = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };

            var httpResp = await client.SendAsync(httpReq, ct);
            if (!httpResp.IsSuccessStatusCode)
            {
                var err = await httpResp.Content.ReadAsStringAsync(ct);
                throw new Exception($"DeepSeek API 错误 {(int)httpResp.StatusCode}: {err[..Math.Min(300, err.Length)]}");
            }

            var respJson = await httpResp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(respJson);
            var choice = doc.RootElement.GetProperty("choices")[0];
            var finishReason = choice.GetProperty("finish_reason").GetString();
            var msgEl = choice.GetProperty("message");

            if (finishReason == "stop" || finishReason == "length")
            {
                return msgEl.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString() ?? "（无回复内容）"
                    : "（无回复内容）";
            }

            if (finishReason == "tool_calls" && msgEl.TryGetProperty("tool_calls", out var tcEl))
            {
                string? assistantContent = null;
                if (msgEl.TryGetProperty("content", out var ctEl) && ctEl.ValueKind != JsonValueKind.Null)
                    assistantContent = ctEl.GetString();

                messages.Add(new
                {
                    role = "assistant",
                    content = assistantContent,
                    tool_calls = tcEl.EnumerateArray().Select(tc => new
                    {
                        id = tc.GetProperty("id").GetString(),
                        type = "function",
                        function = new
                        {
                            name = tc.GetProperty("function").GetProperty("name").GetString(),
                            arguments = tc.GetProperty("function").GetProperty("arguments").GetString()
                        }
                    }).ToArray()
                });

                foreach (var tc in tcEl.EnumerateArray())
                {
                    var toolCallId = tc.GetProperty("id").GetString()!;
                    var funcName = tc.GetProperty("function").GetProperty("name").GetString()!;
                    var argsJson = tc.GetProperty("function").GetProperty("arguments").GetString()!;

                    _logger.LogInformation("  工具调用: {Tool}({Args})", funcName,
                        argsJson.Length > 100 ? argsJson[..100] + "..." : argsJson);

                    var result = await toolRegistry.ExecuteAsync(userId, funcName, argsJson, ct);
                    messages.Add(new { role = "tool", tool_call_id = toolCallId, content = result });
                }
            }
            else
            {
                if (msgEl.TryGetProperty("content", out var fallback) && fallback.ValueKind == JsonValueKind.String)
                    return fallback.GetString() ?? string.Empty;
                break;
            }
        }

        return "任务执行超出工具调用轮次限制，部分操作可能未完成。";
    }

    private static string BuildTaskSystemPrompt()
    {
        var cstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cstZone);
        var today = now.ToString("yyyy-MM-dd");

        return $"""
            你是 MiraiNote 个人助理，正在执行用户预设的定时任务。
            你不是 Claude、GPT 或其他任何第三方 AI 产品。

            【当前时间】{today}

            【执行原则】
            1. 严格按照任务描述执行，不要额外发挥或遗漏步骤。
            2. 所有数据操作必须通过调用工具完成，严禁用文字描述代替工具调用。
            3. 涉及用户数据时，必须先查询真实数据再作答。
            4. 执行完毕后，用简洁清晰的语言总结完成情况。

            【可用工具】
            你拥有以下工具：搜索互联网、查询/创建/修改工作记录、备忘、生活记录、
            获取/生成周报、读取/写入文件、执行 Shell 命令、发送邮件、查询天气等。

            【输出要求】
            执行完成后输出总结：做了什么、结果如何、是否有需要注意的事项。
            """;
    }
}
