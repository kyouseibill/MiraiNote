using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Chat;
using MiraiNote.Shared.Dtos.LifeLogs;
using MiraiNote.Shared.Dtos.Memos;
using MiraiNote.Shared.Dtos.WorkLogs;

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
    private readonly TavilyOptions _tavilyOptions;
    private readonly UploadOptions _uploadOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWorkLogService _workLogService;
    private readonly IMemoService _memoService;
    private readonly ILifeLogService _lifeLogService;

    private static readonly JsonSerializerOptions _sendOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ChatService(
        MiraiNoteDbContext db,
        IOptions<DeepSeekOptions> deepSeekOptions,
        IOptions<TavilyOptions> tavilyOptions,
        IOptions<UploadOptions> uploadOptions,
        IHttpClientFactory httpClientFactory,
        IWorkLogService workLogService,
        IMemoService memoService,
        ILifeLogService lifeLogService)
    {
        _db = db;
        _deepSeekOptions = deepSeekOptions.Value;
        _tavilyOptions = tavilyOptions.Value;
        _uploadOptions = uploadOptions.Value;
        _httpClientFactory = httpClientFactory;
        _workLogService = workLogService;
        _memoService = memoService;
        _lifeLogService = lifeLogService;
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

        var tools = BuildTools();
        for (int round = 0; round < 8; round++)
        {
            var bodyJson = JsonSerializer.Serialize(new
            {
                model = _deepSeekOptions.Model,
                messages,
                tools,
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
                "search_work_logs"    => await SearchWorkLogsAsync(userId, args, ct),
                "search_memos"        => await SearchMemosAsync(userId, args, ct),
                "search_life_logs"    => await SearchLifeLogsAsync(userId, args, ct),
                "get_weekly_reports"  => await GetWeeklyReportsAsync(userId, args, ct),
                "search_internet"     => await SearchInternetAsync(args, ct),
                // 写操作工具
                "create_work_log"     => await CreateWorkLogToolAsync(userId, args, ct),
                "update_work_log"     => await UpdateWorkLogToolAsync(userId, args, ct),
                "delete_work_log"     => await DeleteWorkLogToolAsync(userId, args, ct),
                "create_memo"         => await CreateMemoToolAsync(userId, args, ct),
                "update_memo"         => await UpdateMemoToolAsync(userId, args, ct),
                "patch_memo_status"   => await PatchMemoStatusToolAsync(userId, args, ct),
                "delete_memo"         => await DeleteMemoToolAsync(userId, args, ct),
                "create_life_log"     => await CreateLifeLogToolAsync(userId, args, ct),
                "update_life_log"     => await UpdateLifeLogToolAsync(userId, args, ct),
                "delete_life_log"     => await DeleteLifeLogToolAsync(userId, args, ct),
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

        var cstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        return JsonSerializer.Serialize(raw.Select(m => new
        {
            id = m.Id, section = m.Section, content = m.Content,
            priority = m.Priority == 3 ? "高" : m.Priority == 2 ? "中" : "低",
            isPinned = m.IsPinned, isDone = m.IsDone,
            remindAt = m.RemindAt.HasValue
                ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(m.RemindAt.Value, DateTimeKind.Utc), cstZone).ToString("yyyy-MM-dd HH:mm")
                : null
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

    // ===== 写操作工具：工作记录 =====

    private async Task<string> CreateWorkLogToolAsync(int userId, JsonElement args, CancellationToken ct)
    {
        if (!TryGetStr(args, "title", out var title))     return "创建失败：title 为必填项。";
        if (!TryGetStr(args, "log_date", out var dateStr)) return "创建失败：log_date 为必填项，格式 yyyy-MM-dd。";
        if (!DateTime.TryParse(dateStr, out var logDate)) return "创建失败：log_date 格式无效。";

        TryGetStr(args, "purpose",  out var purpose);
        TryGetStr(args, "content",  out var content);
        TryGetStr(args, "tags",     out var tags);
        TryGetStr(args, "category", out var category);
        byte status = 0;
        if (args.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.Number)
            status = (byte)stEl.GetInt32();

        var dto = await _workLogService.CreateAsync(userId, new CreateWorkLogRequest
        {
            Title    = title,
            Purpose  = string.IsNullOrWhiteSpace(purpose)  ? null : purpose,
            Content  = string.IsNullOrWhiteSpace(content)  ? null : content,
            Tags     = string.IsNullOrWhiteSpace(tags)     ? null : tags,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
            LogDate  = logDate,
            Status   = status
        }, ct);
        return $"已成功创建工作记录（ID={dto.Id}）：《{dto.Title}》，日期：{dto.LogDate:yyyy-MM-dd}。";
    }

    private async Task<string> UpdateWorkLogToolAsync(int userId, JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "更新失败：id 为必填项。";
        var id = idEl.GetInt32();
        if (!TryGetStr(args, "title", out var title))     return "更新失败：title 为必填项。";
        if (!TryGetStr(args, "log_date", out var dateStr)) return "更新失败：log_date 为必填项。";
        if (!DateTime.TryParse(dateStr, out var logDate)) return "更新失败：log_date 格式无效。";

        TryGetStr(args, "purpose",  out var purpose);
        TryGetStr(args, "content",  out var content);
        TryGetStr(args, "tags",     out var tags);
        TryGetStr(args, "category", out var category);
        byte status = 0;
        if (args.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.Number)
            status = (byte)stEl.GetInt32();

        var dto = await _workLogService.UpdateAsync(userId, id, new UpdateWorkLogRequest
        {
            Title    = title,
            Purpose  = string.IsNullOrWhiteSpace(purpose)  ? null : purpose,
            Content  = string.IsNullOrWhiteSpace(content)  ? null : content,
            Tags     = string.IsNullOrWhiteSpace(tags)     ? null : tags,
            Category = string.IsNullOrWhiteSpace(category) ? null : category,
            LogDate  = logDate,
            Status   = status
        }, ct);
        return $"已成功更新工作记录（ID={dto.Id}）：《{dto.Title}》。";
    }

    private async Task<string> DeleteWorkLogToolAsync(int userId, JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "删除失败：id 为必填项。";
        var id = idEl.GetInt32();
        await _workLogService.DeleteAsync(userId, id, ct);
        return $"已成功删除工作记录（ID={id}）。";
    }

    // ===== 写操作工具：备忘 =====

    private async Task<string> CreateMemoToolAsync(int userId, JsonElement args, CancellationToken ct)
    {
        if (!TryGetStr(args, "content", out var content)) return "创建失败：content 为必填项。";
        TryGetStr(args, "section", out var section);
        if (string.IsNullOrWhiteSpace(section)) section = "work";

        byte priority = 2;
        if (args.TryGetProperty("priority", out var prEl) && prEl.ValueKind == JsonValueKind.Number)
            priority = (byte)prEl.GetInt32();

        bool isPinned = false;
        if (args.TryGetProperty("is_pinned", out var pinEl) && pinEl.ValueKind == JsonValueKind.True)
            isPinned = true;

        DateTime? remindAt = null;
        if (TryGetStr(args, "remind_at", out var remStr) && DateTime.TryParse(remStr, out var remDt))
            remindAt = remDt.ToUniversalTime();

        byte remindMethods = 0;
        if (args.TryGetProperty("remind_methods", out var rmEl) && rmEl.ValueKind == JsonValueKind.Number)
            remindMethods = (byte)rmEl.GetInt32();

        var dto = await _memoService.CreateAsync(userId, new CreateMemoRequest
        {
            Section       = section,
            Content       = content,
            Priority      = priority,
            IsPinned      = isPinned,
            RemindAt      = remindAt,
            RemindMethods = remindMethods
        }, ct);
        return $"已成功创建备忘（ID={dto.Id}，板块={dto.Section}）：{dto.Content[..Math.Min(50, dto.Content.Length)]}";
    }

    private async Task<string> UpdateMemoToolAsync(int userId, JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "更新失败：id 为必填项。";
        var id = idEl.GetInt32();
        if (!TryGetStr(args, "content", out var content)) return "更新失败：content 为必填项。";

        byte priority = 2;
        if (args.TryGetProperty("priority", out var prEl) && prEl.ValueKind == JsonValueKind.Number)
            priority = (byte)prEl.GetInt32();

        bool isPinned = false;
        if (args.TryGetProperty("is_pinned", out var pinEl) && pinEl.ValueKind == JsonValueKind.True)
            isPinned = true;

        DateTime? remindAt = null;
        if (TryGetStr(args, "remind_at", out var remStr) && DateTime.TryParse(remStr, out var remDt))
            remindAt = remDt.ToUniversalTime();

        byte remindMethods = 0;
        if (args.TryGetProperty("remind_methods", out var rmEl) && rmEl.ValueKind == JsonValueKind.Number)
            remindMethods = (byte)rmEl.GetInt32();

        var dto = await _memoService.UpdateAsync(userId, id, new UpdateMemoRequest
        {
            Content       = content,
            Priority      = priority,
            IsPinned      = isPinned,
            RemindAt      = remindAt,
            RemindMethods = remindMethods
        }, ct);
        return $"已成功更新备忘（ID={dto.Id}）：{dto.Content[..Math.Min(50, dto.Content.Length)]}";
    }

    private async Task<string> PatchMemoStatusToolAsync(int userId, JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "操作失败：id 为必填项。";
        var id = idEl.GetInt32();

        bool? isDone     = null;
        bool? isPinned   = null;
        bool? isArchived = null;

        if (args.TryGetProperty("is_done",     out var dEl) && dEl.ValueKind != JsonValueKind.Null)
            isDone = dEl.GetBoolean();
        if (args.TryGetProperty("is_pinned",   out var pEl) && pEl.ValueKind != JsonValueKind.Null)
            isPinned = pEl.GetBoolean();
        if (args.TryGetProperty("is_archived", out var aEl) && aEl.ValueKind != JsonValueKind.Null)
            isArchived = aEl.GetBoolean();

        await _memoService.PatchStatusAsync(userId, id, new PatchMemoStatusRequest
        {
            IsDone = isDone, IsPinned = isPinned, IsArchived = isArchived
        }, ct);
        return $"已成功更新备忘状态（ID={id}）。";
    }

    private async Task<string> DeleteMemoToolAsync(int userId, JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "删除失败：id 为必填项。";
        var id = idEl.GetInt32();
        await _memoService.DeleteAsync(userId, id, ct);
        return $"已成功删除备忘（ID={id}）。";
    }

    // ===== 写操作工具：生活记录 =====

    private async Task<string> CreateLifeLogToolAsync(int userId, JsonElement args, CancellationToken ct)
    {
        if (!TryGetStr(args, "content", out var content))   return "创建失败：content 为必填项。";
        if (!TryGetStr(args, "log_date", out var dateStr))  return "创建失败：log_date 为必填项。";
        if (!DateTime.TryParse(dateStr, out var logDate))   return "创建失败：log_date 格式无效。";
        TryGetStr(args, "mood", out var mood);

        var dto = await _lifeLogService.CreateAsync(userId, new CreateLifeLogRequest
        {
            Content = content,
            Mood    = string.IsNullOrWhiteSpace(mood) ? null : mood,
            LogDate = logDate
        }, ct);
        return $"已成功创建生活记录（ID={dto.Id}，日期：{dto.LogDate:yyyy-MM-dd}）。";
    }

    private async Task<string> UpdateLifeLogToolAsync(int userId, JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "更新失败：id 为必填项。";
        var id = idEl.GetInt32();
        if (!TryGetStr(args, "content", out var content))   return "更新失败：content 为必填项。";
        if (!TryGetStr(args, "log_date", out var dateStr))  return "更新失败：log_date 为必填项。";
        if (!DateTime.TryParse(dateStr, out var logDate))   return "更新失败：log_date 格式无效。";
        TryGetStr(args, "mood", out var mood);

        var dto = await _lifeLogService.UpdateAsync(userId, id, new UpdateLifeLogRequest
        {
            Content = content,
            Mood    = string.IsNullOrWhiteSpace(mood) ? null : mood,
            LogDate = logDate
        }, ct);
        return $"已成功更新生活记录（ID={dto.Id}，日期：{dto.LogDate:yyyy-MM-dd}）。";
    }

    private async Task<string> DeleteLifeLogToolAsync(int userId, JsonElement args, CancellationToken ct)
    {
        if (!args.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
            return "删除失败：id 为必填项。";
        var id = idEl.GetInt32();
        await _lifeLogService.DeleteAsync(userId, id, ct);
        return $"已成功删除生活记录（ID={id}）。";
    }

    // ===== 互联网搜索工具 =====

    private async Task<string> SearchInternetAsync(JsonElement args, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_tavilyOptions.ApiKey))
            return "互联网搜索功能未配置（Tavily API Key 为空）。";

        if (!TryGetStr(args, "query", out var query))
            return "搜索失败：未提供 query 参数。";

        var client = _httpClientFactory.CreateClient("Tavily");
        var body = new
        {
            api_key = _tavilyOptions.ApiKey,
            query,
            max_results = _tavilyOptions.MaxResults,
            search_depth = "basic",
            include_answer = false,
            include_raw_content = false
        };

        var httpResp = await client.PostAsJsonAsync($"{_tavilyOptions.BaseUrl}/search", body, ct);
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
            title   = r.TryGetProperty("title",   out var t) ? t.GetString() : null,
            url     = r.TryGetProperty("url",     out var u) ? u.GetString() : null,
            content = r.TryGetProperty("content", out var c) ? c.GetString() : null,
            score   = r.TryGetProperty("score",   out var s) ? s.GetDouble()  : 0.0
        }).ToList();

        if (results.Count == 0) return "搜索无结果。";
        return JsonSerializer.Serialize(results, _sendOpts);
    }

    // ===== 系统提示 =====

    private static string BuildSystemPrompt()
    {
        var cstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cstZone);
        var today = now.ToString("yyyy-MM-dd");
        var weekday = now.DayOfWeek switch
        {
            DayOfWeek.Monday    => "周一",
            DayOfWeek.Tuesday   => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday  => "周四",
            DayOfWeek.Friday    => "周五",
            DayOfWeek.Saturday  => "周六",
            _                   => "周日"
        };
        // 本周一
        var daysFromMon = ((int)now.DayOfWeek + 6) % 7;
        var weekMon = now.AddDays(-daysFromMon).ToString("yyyy-MM-dd");
        var weekSun = now.AddDays(6 - daysFromMon).ToString("yyyy-MM-dd");

        return $"""
            你是 MiraiNote 个人助理，帮助用户管理工作记录、备忘事项、生活记录和周报。
            你不仅能查询数据，还能帮用户创建、修改和删除各类记录。

            【当前时间】今天是 {today}（{weekday}），本周范围：{weekMon} 至 {weekSun}。

            ══════════════════════════════════════════════
            【最高优先级规则 — 任何情况下绝对不得违反】
            ══════════════════════════════════════════════

            ★ 规则 A：工具调用是唯一合法的操作方式
            所有数据操作（查询、创建、修改、删除）必须通过调用对应工具完成。
            严禁用文字描述来代替工具调用。以下行为绝对禁止：
              × 说"好的，已为您创建了工作记录《XXX》" —— 若未调用 create_work_log 工具，该记录并未真实创建
              × 说"已为您添加了备忘" —— 若未调用 create_memo 工具，备忘并未真实添加
              × 说"已更新/删除" —— 若未调用对应工具，操作未发生
            只有工具成功返回确认消息后，才允许告知用户"操作已完成"。

            ★ 规则 B：严格数据规则
            1. 凡涉及用户个人数据的问题，必须先调用工具查询真实数据，再给出回答。
               禁止在未调用工具的情况下对用户数据做任何描述或推断。
            2. 工具返回"没有找到"时，如实告知，不得用"可能""也许""通常"等词语进行猜测补充。
            3. 只能陈述工具返回的内容，不得添加任何推测、假设或举例。
            4. 不允许说"根据您的习惯""通常情况下""一般来说"等无依据的表述。

            【查询策略】
            - 问题涉及"今天/本周/某日期"时，所有数据源均需查询：工作记录、备忘（work + life 两个板块）、生活记录。
              例如："今天有没有会议" → 同时调用 search_work_logs（keyword=会议）和 search_memos（keyword=会议）。
            - 问题明确指定数据类型时（如"工作记录""备忘""生活日记"），只查对应工具。
            - 跨数据源查询时，所有工具结果汇总后再作答，某一源无结果时不遗漏其他源的结果。
            - 问题涉及互联网公开信息（天气预报、新闻、知识查询、产品介绍等非个人数据）时，调用 search_internet。

            【写操作规则（必须严格执行）】
            创建操作：
            - 用户明确表达创建意图时（如"帮我记一条工作记录""添加一个备忘"），
              必须立即调用对应的 create_* 工具执行，无需先询问"是否要记录"。
            - 工具调用成功后，根据工具返回的结果告知用户（如"已创建工作记录《XXX》，ID=N"）。
            - 禁止在调用工具之前就告诉用户"已创建"。

            修改操作：
            - 用户明确表达修改意图时，先调用搜索工具确认记录存在，再调用 update_* 或 patch_memo_status 工具。
            - 工具执行后再告知用户结果。

            删除操作：
            - 必须先调用搜索工具查询并向用户展示即将删除的具体记录，
              询问"确认删除这条记录吗？"，等用户明确回复确认后，再调用 delete_* 工具。

            信息不足时：
            - 若用户提供的信息不足（缺少必填字段如标题、日期等），礼貌询问补全，不要用假数据填充。

            【时间换算（基于今天 {today}）】
            - "今天"       → date_from = date_to = {today}
            - "本周"       → date_from = {weekMon}，date_to = {weekSun}
            - "昨天"       → date_from = date_to = {now.AddDays(-1):yyyy-MM-dd}
            - "最近 N 天"  → date_from = N 天前，date_to = {today}
            - "本月"       → date_from = {now:yyyy-MM}-01，date_to = {today}
            """;
    }

    // ===== 工具定义（Function Calling Schema） =====

    private object[] BuildTools() =>
    [
        new
        {
            type = "function",
            function = new
            {
                name = "search_work_logs",
                description = "查询用户的工作记录。支持按日期范围、关键词、项目分类筛选。" +
                              "当用户询问工作内容、工作进展、某天/某周做了什么工作、工作总结时调用。" +
                              "模糊查询（如\"有没有会议\"）时 keyword 传关键词，不限制分类。",
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
                description = "查询用户的备忘/待办事项（工作备忘 section=work，生活备忘 section=life）。" +
                              "当用户询问备忘、待办、提醒、会议安排、任务清单时调用。" +
                              "若查询范围不明确（如\"今天有没有会议\"），section 不填，同时搜索两个板块。",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        section          = new { type = "string",  description = "'work' 或 'life'，不填查全部两个板块" },
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
                description = "查询用户的生活记录（日记/感想/生活事件）。支持按日期范围、心情标签、关键词筛选。" +
                              "当用户询问生活状态、某天经历、心情、生活点滴，或模糊查询今日/本周事项时调用。",
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
        },
        new
        {
            type = "function",
            function = new
            {
                name = "search_internet",
                description = "搜索互联网上的公开信息。适用于：天气预报、新闻资讯、知识问答、产品/技术介绍、" +
                              "政策法规、价格查询等与用户个人数据无关的问题。" +
                              "注意：用户个人数据（工作记录/备忘/生活记录）不通过此工具查询，应使用对应的专用工具。" +
                              $"当前未配置 Tavily API Key 时此工具不可用（ApiKey={(_tavilyOptions.ApiKey.Length > 0 ? "已配置" : "未配置")}）。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "query" },
                    properties = new
                    {
                        query = new { type = "string", description = "搜索查询词，用中文或英文描述要查找的信息" }
                    }
                }
            }
        },
        // ===== 写操作工具 =====
        new
        {
            type = "function",
            function = new
            {
                name = "create_work_log",
                description = "创建一条工作记录。当用户明确表达要记录工作内容、添加工作日志时调用。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "title", "log_date" },
                    properties = new
                    {
                        title    = new { type = "string", description = "工作记录标题（必填）" },
                        log_date = new { type = "string", description = "记录日期，格式 yyyy-MM-dd（必填），如不确定用今天 " + DateTime.UtcNow.ToString("yyyy-MM-dd") },
                        purpose  = new { type = "string", description = "工作目的/背景" },
                        content  = new { type = "string", description = "工作内容详情" },
                        tags     = new { type = "string", description = "标签，多个用逗号分隔" },
                        category = new { type = "string", description = "项目分类" },
                        status   = new { type = "integer", description = "状态：0=未标记，1=进行中，2=已完成，3=已延期，默认0" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "update_work_log",
                description = "更新已有的工作记录。需要先通过 search_work_logs 获取记录 ID，再调用此工具。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "id", "title", "log_date" },
                    properties = new
                    {
                        id       = new { type = "integer", description = "要更新的工作记录 ID（必填）" },
                        title    = new { type = "string",  description = "新标题（必填）" },
                        log_date = new { type = "string",  description = "记录日期，格式 yyyy-MM-dd（必填）" },
                        purpose  = new { type = "string",  description = "工作目的" },
                        content  = new { type = "string",  description = "工作内容" },
                        tags     = new { type = "string",  description = "标签" },
                        category = new { type = "string",  description = "项目分类" },
                        status   = new { type = "integer", description = "状态：0-3" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "delete_work_log",
                description = "删除工作记录。必须在用户明确确认后才能调用。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "id" },
                    properties = new
                    {
                        id = new { type = "integer", description = "要删除的工作记录 ID" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "create_memo",
                description = "创建一条备忘/待办事项。当用户要记录提醒、待办、会议安排时调用。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "content" },
                    properties = new
                    {
                        content        = new { type = "string",  description = "备忘内容（必填）" },
                        section        = new { type = "string",  description = "板块：work（工作，默认）或 life（生活）" },
                        priority       = new { type = "integer", description = "优先级：1=低，2=中（默认），3=高" },
                        is_pinned      = new { type = "boolean", description = "是否置顶，默认 false" },
                        remind_at      = new { type = "string",  description = "提醒时间，格式 yyyy-MM-dd HH:mm" },
                        remind_methods = new { type = "integer", description = "提醒方式：0=不提醒，1=弹窗，2=邮件，3=弹窗+邮件" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "update_memo",
                description = "更新已有的备忘内容。需要先通过 search_memos 获取 ID。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "id", "content" },
                    properties = new
                    {
                        id             = new { type = "integer", description = "备忘 ID（必填）" },
                        content        = new { type = "string",  description = "新的备忘内容（必填）" },
                        priority       = new { type = "integer", description = "优先级：1-3" },
                        is_pinned      = new { type = "boolean", description = "是否置顶" },
                        remind_at      = new { type = "string",  description = "提醒时间" },
                        remind_methods = new { type = "integer", description = "提醒方式：0-3" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "patch_memo_status",
                description = "切换备忘的完成/置顶/归档状态。如将备忘标记为已完成、归档备忘时调用。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "id" },
                    properties = new
                    {
                        id          = new { type = "integer", description = "备忘 ID（必填）" },
                        is_done     = new { type = "boolean", description = "是否标记为已完成" },
                        is_pinned   = new { type = "boolean", description = "是否置顶" },
                        is_archived = new { type = "boolean", description = "是否归档" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "delete_memo",
                description = "删除备忘事项。必须在用户明确确认后才能调用。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "id" },
                    properties = new
                    {
                        id = new { type = "integer", description = "要删除的备忘 ID" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "create_life_log",
                description = "创建一条生活记录/日记。当用户要记录生活感悟、日常事件、心情时调用。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "content", "log_date" },
                    properties = new
                    {
                        content  = new { type = "string", description = "生活记录内容（必填）" },
                        log_date = new { type = "string", description = "记录日期，格式 yyyy-MM-dd（必填）" },
                        mood     = new { type = "string", description = "心情标签，如：开心/平静/疲惫/难过/焦虑" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "update_life_log",
                description = "更新已有的生活记录。需要先通过 search_life_logs 获取 ID。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "id", "content", "log_date" },
                    properties = new
                    {
                        id       = new { type = "integer", description = "生活记录 ID（必填）" },
                        content  = new { type = "string",  description = "新的内容（必填）" },
                        log_date = new { type = "string",  description = "记录日期，格式 yyyy-MM-dd（必填）" },
                        mood     = new { type = "string",  description = "心情标签" }
                    }
                }
            }
        },
        new
        {
            type = "function",
            function = new
            {
                name = "delete_life_log",
                description = "删除生活记录。必须在用户明确确认后才能调用。",
                parameters = new
                {
                    type = "object",
                    required = new[] { "id" },
                    properties = new
                    {
                        id = new { type = "integer", description = "要删除的生活记录 ID" }
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