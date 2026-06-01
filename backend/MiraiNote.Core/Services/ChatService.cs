using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Chat;

namespace MiraiNote.Core.Services;

public interface IChatService
{
    Task<List<ChatSessionDto>> GetSessionsAsync(int userId, CancellationToken ct = default);
    Task<ChatSessionDetailDto> GetSessionAsync(int userId, int sessionId, CancellationToken ct = default);
    Task<ChatSessionDto> CreateSessionAsync(int userId, CreateSessionRequest request, CancellationToken ct = default);
    Task<ChatSessionDto> UpdateSessionTitleAsync(int userId, int sessionId, UpdateSessionTitleRequest request, CancellationToken ct = default);
    Task DeleteSessionAsync(int userId, int sessionId, CancellationToken ct = default);
    Task<ChatMessageDto> SendMessageAsync(int userId, int sessionId, SendMessageRequest request, CancellationToken ct = default);
}

/// <summary>
/// AI 对话业务实现。
/// 通过 DeepSeek Function Calling API 让 AI 按需检索用户的真实数据，再生成回答。
/// 对话历史持久化保存。
/// </summary>
public class ChatService : IChatService
{
    private readonly MiraiNoteDbContext _db;
    private readonly DeepSeekOptions _deepSeekOptions;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions _sendOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly object[] _tools = BuildTools();

    public ChatService(MiraiNoteDbContext db, IOptions<DeepSeekOptions> deepSeekOptions, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _deepSeekOptions = deepSeekOptions.Value;
        _httpClientFactory = httpClientFactory;
    }

    // ===== 会话 CRUD =====

    public async Task<List<ChatSessionDto>> GetSessionsAsync(int userId, CancellationToken ct = default)
    {
        return await _db.ChatSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => MapSession(s))
            .ToListAsync(ct);
    }

    public async Task<ChatSessionDetailDto> GetSessionAsync(int userId, int sessionId, CancellationToken ct = default)
    {
        var session = await _db.ChatSessions
            .AsNoTracking()
            .Include(s => s.Messages.Where(m => !m.IsDeleted))
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new BusinessException("对话不存在", 404);

        return new ChatSessionDetailDto
        {
            Id = session.Id,
            Title = session.Title,
            Messages = session.Messages
                .OrderBy(m => m.CreatedAt)
                .Select(m => MapMessage(m))
                .ToList(),
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt
        };
    }

    public async Task<ChatSessionDto> CreateSessionAsync(int userId, CreateSessionRequest request, CancellationToken ct = default)
    {
        var session = new ChatSession
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "新对话" : request.Title.Trim()
        };
        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return MapSession(session);
    }

    public async Task<ChatSessionDto> UpdateSessionTitleAsync(int userId, int sessionId, UpdateSessionTitleRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new BusinessException("标题不能为空", 400);

        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new BusinessException("对话不存在", 404);

        session.Title = request.Title.Trim();
        await _db.SaveChangesAsync(ct);
        return MapSession(session);
    }

    public async Task DeleteSessionAsync(int userId, int sessionId, CancellationToken ct = default)
    {
        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new BusinessException("对话不存在", 404);

        session.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    // ===== 发送消息（含 Function Calling 循环） =====

    public async Task<ChatMessageDto> SendMessageAsync(int userId, int sessionId, SendMessageRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new BusinessException("消息内容不能为空", 400);

        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new BusinessException("对话不存在", 404);

        var userMsg = new ChatMessage
        {
            SessionId = sessionId,
            Role = "user",
            Content = request.Content.Trim()
        };
        _db.ChatMessages.Add(userMsg);
        await _db.SaveChangesAsync(ct);

        var history = await _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var assistantContent = await CallDeepSeekWithToolsAsync(userId, history, ct);

        var assistantMsg = new ChatMessage
        {
            SessionId = sessionId,
            Role = "assistant",
            Content = assistantContent
        };
        _db.ChatMessages.Add(assistantMsg);

        if (history.Count == 1 && session.Title == "新对话")
        {
            session.Title = request.Content.Length > 30
                ? request.Content[..30] + "..."
                : request.Content;
        }

        await _db.SaveChangesAsync(ct);
        return MapMessage(assistantMsg);
    }

    // ===== DeepSeek Function Calling 循环 =====

    private async Task<string> CallDeepSeekWithToolsAsync(int userId, List<ChatMessage> history, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_deepSeekOptions.ApiKey))
            throw new BusinessException("DeepSeek API Key 未配置，请联系管理员", 500);

        var client = _httpClientFactory.CreateClient("DeepSeek");
        client.BaseAddress = new Uri(_deepSeekOptions.BaseUrl);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _deepSeekOptions.ApiKey);

        var messages = new List<object>
        {
            new { role = "system", content = BuildSystemPrompt() }
        };
        messages.AddRange(history.Select(m => (object)new { role = m.Role, content = m.Content }));

        for (int round = 0; round < 5; round++)
        {
            var bodyJson = JsonSerializer.Serialize(new
            {
                model = _deepSeekOptions.Model,
                messages,
                tools = _tools,
                tool_choice = "auto"
            }, _sendOpts);

            var httpResp = await client.PostAsync(
                "/v1/chat/completions",
                new StringContent(bodyJson, Encoding.UTF8, "application/json"),
                ct);

            if (!httpResp.IsSuccessStatusCode)
            {
                var err = await httpResp.Content.ReadAsStringAsync(ct);
                throw new BusinessException($"AI 服务错误 {(int)httpResp.StatusCode}: {err[..Math.Min(300, err.Length)]}", 500);
            }

            using var doc = JsonDocument.Parse(await httpResp.Content.ReadAsStringAsync(ct));
            var choice = doc.RootElement.GetProperty("choices")[0];
            var finishReason = choice.GetProperty("finish_reason").GetString();
            var msgEl = choice.GetProperty("message");

            if (finishReason == "stop" || finishReason == "length")
            {
                return msgEl.GetProperty("content").GetString() ?? string.Empty;
            }

            if (finishReason == "tool_calls")
            {
                var toolCallsEl = msgEl.GetProperty("tool_calls");

                string? assistantContent = null;
                if (msgEl.TryGetProperty("content", out var cEl) && cEl.ValueKind != JsonValueKind.Null)
                    assistantContent = cEl.GetString();

                messages.Add(new
                {
                    role = "assistant",
                    content = assistantContent,
                    tool_calls = toolCallsEl.EnumerateArray().Select(tc => new
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

                foreach (var tc in toolCallsEl.EnumerateArray())
                {
                    var toolCallId = tc.GetProperty("id").GetString()!;
                    var funcName = tc.GetProperty("function").GetProperty("name").GetString()!;
                    var argsJson = tc.GetProperty("function").GetProperty("arguments").GetString()!;
                    var result = await ExecuteToolAsync(userId, funcName, argsJson, ct);
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

        return "抱歉，处理请求时超出工具调用限制，请尝试重新提问。";
    }

    // ===== 工具执行调度 =====

    private async Task<string> ExecuteToolAsync(int userId, string toolName, string argsJson, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            var args = doc.RootElement;
            return toolName switch
            {
                "search_work_logs"  => await SearchWorkLogsAsync(userId, args, ct),
                "search_memos"      => await SearchMemosAsync(userId, args, ct),
                "search_life_logs"  => await SearchLifeLogsAsync(userId, args, ct),
                "get_weekly_reports" => await GetWeeklyReportsAsync(userId, args, ct),
                _ => $"未知工具：{toolName}"
            };
        }
        catch (Exception ex)
        {
            return $"工具执行失败：{ex.Message}";
        }
    }

    // ===== 数据库查询工具 =====

    private async Task<string> SearchWorkLogsAsync(int userId, JsonElement args, CancellationToken ct)
    {
        var q = _db.WorkLogs.AsNoTracking().Where(w => w.UserId == userId);
        if (TryGetStr(args, "date_from", out var df) && DateTime.TryParse(df, out var from))
            q = q.Where(w => w.LogDate >= from.Date);
        if (TryGetStr(args, "date_to", out var dt) && DateTime.TryParse(dt, out var to))
            q = q.Where(w => w.LogDate <= to.Date);
        if (TryGetStr(args, "keyword", out var kw))
            q = q.Where(w => w.Title.Contains(kw) || (w.Content != null && w.Content.Contains(kw)) || (w.Purpose != null && w.Purpose.Contains(kw)));
        if (TryGetStr(args, "category", out var cat))
            q = q.Where(w => w.Category == cat);

        var raw = await q.OrderByDescending(w => w.LogDate).Take(20)
            .Select(w => new { w.Id, w.Title, w.Purpose, w.Content, w.Tags, w.Category, w.LogDate })
            .ToListAsync(ct);
        if (raw.Count == 0) return "没有找到符合条件的工作记录。";

        return JsonSerializer.Serialize(raw.Select(w => new
        {
            id = w.Id, title = w.Title, purpose = w.Purpose, content = w.Content,
            tags = w.Tags, category = w.Category, logDate = w.LogDate.ToString("yyyy-MM-dd")
        }), _sendOpts);
    }

    private async Task<string> SearchMemosAsync(int userId, JsonElement args, CancellationToken ct)
    {
        var q = _db.Memos.AsNoTracking().Where(m => m.UserId == userId);
        if (TryGetStr(args, "section", out var sec)) q = q.Where(m => m.Section == sec);
        if (TryGetStr(args, "keyword", out var kw))  q = q.Where(m => m.Content.Contains(kw));
        if (!(args.TryGetProperty("include_done", out var idEl) && idEl.ValueKind == JsonValueKind.True))
            q = q.Where(m => !m.IsDone);
        if (!(args.TryGetProperty("include_archived", out var iaEl) && iaEl.ValueKind == JsonValueKind.True))
            q = q.Where(m => !m.IsArchived);

        var raw = await q.OrderByDescending(m => m.IsPinned).ThenByDescending(m => m.Priority).Take(30)
            .Select(m => new { m.Id, m.Section, m.Content, m.Priority, m.IsPinned, m.IsDone, m.RemindAt })
            .ToListAsync(ct);
        if (raw.Count == 0) return "没有找到符合条件的备忘事项。";

        return JsonSerializer.Serialize(raw.Select(m => new
        {
            id = m.Id, section = m.Section, content = m.Content,
            priority = m.Priority == 3 ? "高" : m.Priority == 2 ? "中" : "低",
            isPinned = m.IsPinned, isDone = m.IsDone,
            remindAt = m.RemindAt.HasValue ? m.RemindAt.Value.ToString("yyyy-MM-dd HH:mm") : null
        }), _sendOpts);
    }

    private async Task<string> SearchLifeLogsAsync(int userId, JsonElement args, CancellationToken ct)
    {
        var q = _db.LifeLogs.AsNoTracking().Where(l => l.UserId == userId);
        if (TryGetStr(args, "date_from", out var df) && DateTime.TryParse(df, out var from))
            q = q.Where(l => l.LogDate >= from.Date);
        if (TryGetStr(args, "date_to", out var dt) && DateTime.TryParse(dt, out var to))
            q = q.Where(l => l.LogDate <= to.Date);
        if (TryGetStr(args, "keyword", out var kw))  q = q.Where(l => l.Content.Contains(kw));
        if (TryGetStr(args, "mood", out var mood))   q = q.Where(l => l.Mood == mood);

        var raw = await q.OrderByDescending(l => l.LogDate).Take(20)
            .Select(l => new { l.Id, l.Content, l.Mood, l.LogDate })
            .ToListAsync(ct);
        if (raw.Count == 0) return "没有找到符合条件的生活记录。";

        return JsonSerializer.Serialize(raw.Select(l => new
        {
            id = l.Id, content = l.Content, mood = l.Mood, logDate = l.LogDate.ToString("yyyy-MM-dd")
        }), _sendOpts);
    }

    private async Task<string> GetWeeklyReportsAsync(int userId, JsonElement args, CancellationToken ct)
    {
        var q = _db.WeeklyReports.AsNoTracking().Where(r => r.UserId == userId);
        if (TryGetStr(args, "week_start", out var ws) && DateTime.TryParse(ws, out var weekStart))
            q = q.Where(r => r.WeekStart == weekStart.Date);

        var raw = await q.OrderByDescending(r => r.WeekStart).Take(5)
            .Select(r => new { r.Id, r.WeekStart, r.WeekEnd, r.Content, r.GeneratedAt })
            .ToListAsync(ct);
        if (raw.Count == 0) return "没有找到符合条件的周报。";

        return JsonSerializer.Serialize(raw.Select(r => new
        {
            id = r.Id, weekStart = r.WeekStart.ToString("yyyy-MM-dd"),
            weekEnd = r.WeekEnd.ToString("yyyy-MM-dd"), content = r.Content,
            generatedAt = r.GeneratedAt.ToString("yyyy-MM-dd")
        }));
    }

    // ===== 系统提示 =====

    private static string BuildSystemPrompt()
    {
        var today = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
        return $"""
            你是 MiraiNote 个人助理，帮助用户管理工作记录、备忘事项、生活记录和周报。
            今天的日期（北京时间）：{today}。

            当用户询问涉及他们数据的问题时（例如"今天做了什么""本周工作""未完成的备忘""最近心情"等），
            请先调用相应的查询工具获取真实数据，再基于数据给出回答，不要凭空猜测或编造内容。
            若查询结果为空，如实告知用户没有相关记录。

            时间词换算规则（基于今天 {today}）：
            - "今天" → date_from 和 date_to 均设为今天
            - "本周" → date_from 设为本周一，date_to 设为本周日
            - "昨天" → date_from 和 date_to 均设为昨天
            - "最近 N 天" → date_from 设为 N 天前，date_to 设为今天
            """;
    }

    // ===== 工具定义（Function Calling Schema） =====

    private static object[] BuildTools() =>
    [
        new
        {
            type = "function",
            function = new
            {
                name = "search_work_logs",
                description = "查询用户的工作记录。支持按日期范围、关键词、项目分类筛选。" +
                              "当用户询问工作内容、工作进展、某天/某周做了什么工作、工作总结时调用。",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        date_from = new { type = "string", description = "起始日期，格式 yyyy-MM-dd" },
                        date_to   = new { type = "string", description = "结束日期，格式 yyyy-MM-dd" },
                        keyword   = new { type = "string", description = "关键词，模糊匹配标题/内容/目的" },
                        category  = new { type = "string", description = "项目分类名称" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "search_memos",
                description = "查询用户的备忘/待办事项。section='work' 查工作备忘，section='life' 查生活备忘，不填查全部。" +
                              "当用户询问待办事项、备忘、提醒事项、未完成任务时调用。",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        section          = new { type = "string",  description = "'work' 或 'life'，不填查全部" },
                        keyword          = new { type = "string",  description = "关键词，模糊匹配内容" },
                        include_done     = new { type = "boolean", description = "是否包含已完成，默认 false" },
                        include_archived = new { type = "boolean", description = "是否包含已归档，默认 false" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "search_life_logs",
                description = "查询用户的生活记录（日记/感想/事件）。支持按日期范围、心情标签、关键词筛选。" +
                              "当用户询问生活状态、某天经历、心情、生活点滴时调用。",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        date_from = new { type = "string", description = "起始日期，格式 yyyy-MM-dd" },
                        date_to   = new { type = "string", description = "结束日期，格式 yyyy-MM-dd" },
                        keyword   = new { type = "string", description = "关键词，模糊匹配内容" },
                        mood      = new { type = "string", description = "心情标签，如：开心/平静/疲惫/难过" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "get_weekly_reports",
                description = "获取用户已生成的工作周报内容。当用户询问周报时调用。",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        week_start = new { type = "string", description = "周报起始日期，格式 yyyy-MM-dd，不填返回最近的周报" }
                    }
                }
            }
        }
    ];

    // ===== 辅助方法 =====

    private static bool TryGetStr(JsonElement el, string key, out string value)
    {
        if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString()!;
            return !string.IsNullOrWhiteSpace(value);
        }
        value = string.Empty;
        return false;
    }

    private static ChatSessionDto MapSession(ChatSession s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt
    };

    private static ChatMessageDto MapMessage(ChatMessage m) => new()
    {
        Id = m.Id,
        Role = m.Role,
        Content = m.Content,
        CreatedAt = m.CreatedAt
    };
}