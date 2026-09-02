using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Agent;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Core.Services;

public interface IAgentMemoryService
{
    Task<List<AgentMemoryDto>> GetMemoriesAsync(int userId, string? category = null, CancellationToken ct = default);
    Task<AgentMemoryDto?> GetByKeyAsync(int userId, string key, CancellationToken ct = default);
    Task<AgentMemoryDto> CreateAsync(int userId, CreateMemoryRequest request, CancellationToken ct = default);
    Task<AgentMemoryDto> UpdateAsync(int userId, int id, UpdateMemoryRequest request, CancellationToken ct = default);
    Task DeleteAsync(int userId, int id, CancellationToken ct = default);
    Task DeleteByKeyAsync(int userId, string key, CancellationToken ct = default);

    /// <summary>关键词搜索记忆（匹配 key / value / tags）。</summary>
    Task<List<AgentMemoryDto>> SearchAsync(int userId, string? keyword = null, string? category = null, CancellationToken ct = default);

    /// <summary>自动从对话内容中提取关键信息并存储为记忆（LLM 驱动，带关键词回退）。</summary>
    Task AutoExtractAsync(int userId, string userMessage, string? assistantResponse, CancellationToken ct = default);

    /// <summary>语义相关性匹配：从用户记忆中选出与当前查询最相关的 N 条。</summary>
    Task<List<RelevantMemoryDto>> GetRelevantMemoriesAsync(int userId, string query, int maxCount = 5, CancellationToken ct = default);
}

public class AgentMemoryService : IAgentMemoryService
{
    private readonly MiraiNoteDbContext _db;
    private readonly DeepSeekOptions _deepSeekOptions;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AgentMemoryService(
        MiraiNoteDbContext db,
        IOptions<DeepSeekOptions> deepSeekOptions,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _deepSeekOptions = deepSeekOptions.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<AgentMemoryDto>> GetMemoriesAsync(int userId, string? category = null, CancellationToken ct = default)
    {
        var q = _db.AgentMemories.AsNoTracking().Where(m => m.UserId == userId);
        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(m => m.Category == category);

        return await q.OrderByDescending(m => m.Importance).ThenByDescending(m => m.LastAccessedAt)
            .Select(m => Map(m)).ToListAsync(ct);
    }

    public async Task<AgentMemoryDto?> GetByKeyAsync(int userId, string key, CancellationToken ct = default)
    {
        var entity = await _db.AgentMemories
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Key == key, ct);

        if (entity == null) return null;

        // 访问时增加重要性和计数
        if (entity.Importance < 5)
            entity.Importance++;
        entity.AccessedCount++;
        entity.LastAccessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Map(entity);
    }

    public async Task<AgentMemoryDto> CreateAsync(int userId, CreateMemoryRequest r, CancellationToken ct = default)
    {
        string source = r.Source ?? "manual";

        // Upsert：key 已存在则更新
        var existing = await _db.AgentMemories
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Key == r.Key, ct);

        if (existing != null)
        {
            existing.Value = r.Value;
            existing.Category = r.Category;
            existing.Tags = r.Tags;
            existing.Importance = r.Importance;
            existing.Source ??= source; // 保留最早的 source
            existing.LastAccessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Map(existing);
        }

        var entity = new AgentMemory
        {
            UserId = userId,
            Key = r.Key,
            Value = r.Value,
            Category = r.Category,
            Tags = r.Tags,
            Importance = r.Importance,
            Source = source,
            LastAccessedAt = DateTime.UtcNow
        };
        _db.AgentMemories.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<AgentMemoryDto> UpdateAsync(int userId, int id, UpdateMemoryRequest r, CancellationToken ct = default)
    {
        var entity = await _db.AgentMemories
            .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, ct)
            ?? throw new BusinessException("记忆不存在", 404);

        if (r.Value != null) entity.Value = r.Value;
        if (r.Category != null) entity.Category = r.Category;
        if (r.Tags != null) entity.Tags = r.Tags;
        if (r.Importance.HasValue) entity.Importance = r.Importance.Value;
        entity.LastAccessedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var entity = await _db.AgentMemories
            .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, ct)
            ?? throw new BusinessException("记忆不存在", 404);

        entity.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteByKeyAsync(int userId, string key, CancellationToken ct = default)
    {
        var entity = await _db.AgentMemories
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Key == key, ct);
        if (entity != null)
        {
            entity.IsDeleted = true;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<AgentMemoryDto>> SearchAsync(int userId, string? keyword = null, string? category = null, CancellationToken ct = default)
    {
        var q = _db.AgentMemories.AsNoTracking().Where(m => m.UserId == userId);

        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(m => m.Category == category);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(m =>
                m.Key.Contains(keyword) ||
                m.Value.Contains(keyword) ||
                (m.Tags != null && m.Tags.Contains(keyword)));
        }

        return await q.OrderByDescending(m => m.Importance).ThenByDescending(m => m.LastAccessedAt)
            .Select(m => Map(m)).ToListAsync(ct);
    }

    /// <summary>
    /// 从对话中自动提取偏好和上下文。
    /// 优先使用 LLM 语义提取，失败时回退到关键词匹配。
    /// </summary>
    public async Task AutoExtractAsync(int userId, string userMessage, string? assistantResponse, CancellationToken ct = default)
    {
        var combinedText = userMessage;
        if (!string.IsNullOrWhiteSpace(assistantResponse))
            combinedText += " " + assistantResponse;

        // 噪声过滤：极短对话不提取
        if (combinedText.Length < 20) return;

        // 尝试 LLM 提取
        bool llmSuccess = false;
        try
        {
            llmSuccess = await TryLlmExtractAsync(userId, userMessage, assistantResponse, ct);
        }
        catch
        {
            // LLM 提取失败，静默回退
        }

        // LLM 失败时回退到关键词匹配
        if (!llmSuccess)
        {
            KeywordExtract(userId, userMessage);
        }
    }

    /// <summary>
    /// 使用 DeepSeek 从对话中语义提取记忆。
    /// </summary>
    private async Task<bool> TryLlmExtractAsync(int userId, string userMessage, string? assistantResponse, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_deepSeekOptions.ApiKey)) return false;

        var client = _httpClientFactory.CreateClient("DeepSeek");
        client.BaseAddress = new Uri(_deepSeekOptions.BaseUrl);

        var systemPrompt = """
            你是一个个人信息提取助手。从对话中提取用户的偏好、习惯、事实和重要信息。

            提取规则：
            1. 只提取可复用的、长期有价值的信息
            2. category 分为：preference（偏好）、fact（事实）、context（上下文）、pattern（模式/习惯）、date（重要日期）
            3. importance 1-5：5=非常重要（姓名、纪念日、核心偏好等），1=临时信息，默认 3
            4. key 使用英文下划线命名（如 coffee_preference、birthday、work_style）
            5. value 使用中文描述，简洁清晰（不超过 100 字）
            6. 不要提取临时性的、只与当前对话相关的信息
            7. 如果对话中没有值得长期记住的信息，返回空数组

            请仅用以下 JSON 格式回复，不要其他文字：
            {"memories":[{"key":"...","value":"...","category":"...","importance":3}]}
            """;

        var userContent = $"对话内容：\n用户：{userMessage}";
        if (!string.IsNullOrWhiteSpace(assistantResponse))
            userContent += $"\n助手：{assistantResponse[..Math.Min(500, assistantResponse.Length)]}";

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userContent }
        };

        var body = JsonSerializer.Serialize(new
        {
            model = _deepSeekOptions.Model,
            messages,
            temperature = 0.3,
            max_tokens = 1000,
            stream = false
        }, _jsonOpts);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        // Auth header already set via named client setup in ChatService; set here for safety
        if (req.Headers.Authorization == null)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _deepSeekOptions.ApiKey);

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return false;

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(respJson);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content)) return false;

        // 解析 JSON（处理 markdown 代码块包裹）
        var json = content.Trim();
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }

        using var resultDoc = JsonDocument.Parse(json);
        if (!resultDoc.RootElement.TryGetProperty("memories", out var memoriesEl)) return false;
        if (memoriesEl.ValueKind != JsonValueKind.Array) return false;

        int count = 0;
        foreach (var mem in memoriesEl.EnumerateArray())
        {
            if (count >= 5) break; // 每次最多 5 条

            var key = mem.TryGetProperty("key", out var k) ? k.GetString() : null;
            var value = mem.TryGetProperty("value", out var v) ? v.GetString() : null;
            var category = mem.TryGetProperty("category", out var c) ? c.GetString() : "context";
            var importance = mem.TryGetProperty("importance", out var i) && i.TryGetByte(out var imp) ? imp : (byte)3;

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) continue;

            try
            {
                await CreateAsync(userId, new CreateMemoryRequest
                {
                    Key = key,
                    Value = value,
                    Category = category ?? "context",
                    Importance = Math.Min(importance, (byte)5),
                    Source = "auto_extract"
                }, ct);
                count++;
            }
            catch
            {
                // 单条创建失败不影响其他
            }
        }

        return count > 0;
    }

    /// <summary>
    /// 关键词回退提取：检测"记住""我喜欢"等触发词。
    /// </summary>
    private void KeywordExtract(int userId, string userMessage)
    {
        if (userMessage.Contains("记住", StringComparison.Ordinal) ||
            userMessage.Contains("我喜欢", StringComparison.Ordinal) ||
            userMessage.Contains("我习惯", StringComparison.Ordinal) ||
            userMessage.Contains("我常用", StringComparison.Ordinal))
        {
            var key = "pref_" + Guid.NewGuid().ToString("N")[..8];
            var value = userMessage.Length > 200 ? userMessage[..200] : userMessage;

            // 使用同步等待因为这是回退路径
            CreateAsync(userId, new CreateMemoryRequest
            {
                Key = key,
                Value = value,
                Category = "preference",
                Importance = 4,
                Source = "auto_extract"
            }).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// 语义相关性匹配：从用户记忆中选出与当前查询最相关的 N 条。
    /// 记忆少时直接返回全部，记忆多时通过 LLM 筛选。
    /// </summary>
    public async Task<List<RelevantMemoryDto>> GetRelevantMemoriesAsync(int userId, string query, int maxCount = 5, CancellationToken ct = default)
    {
        var allMemories = await GetMemoriesAsync(userId, ct: ct);
        var candidates = allMemories.Where(m => m.Importance >= 2).ToList();

        // 候选记忆少时直接返回全部（不需要 LLM）
        if (candidates.Count <= maxCount)
        {
            return candidates.Select(m => new RelevantMemoryDto
            {
                Id = m.Id, Key = m.Key, Value = m.Value, Category = m.Category,
                Tags = m.Tags, Importance = m.Importance, AccessedCount = m.AccessedCount,
                Source = m.Source, LastAccessedAt = m.LastAccessedAt,
                CreatedAt = m.CreatedAt, UpdatedAt = m.UpdatedAt,
                Relevance = null
            }).ToList();
        }

        // 候选记忆多，尝试 LLM 筛选
        try
        {
            return await LlmRankMemoriesAsync(candidates, query, maxCount, ct);
        }
        catch
        {
            // 回退：按重要性取 top N
            return candidates.OrderByDescending(m => m.Importance)
                .Take(maxCount)
                .Select(m => new RelevantMemoryDto
                {
                    Id = m.Id, Key = m.Key, Value = m.Value, Category = m.Category,
                    Tags = m.Tags, Importance = m.Importance, AccessedCount = m.AccessedCount,
                    Source = m.Source, LastAccessedAt = m.LastAccessedAt,
                    CreatedAt = m.CreatedAt, UpdatedAt = m.UpdatedAt,
                    Relevance = null
                }).ToList();
        }
    }

    private async Task<List<RelevantMemoryDto>> LlmRankMemoriesAsync(
        List<AgentMemoryDto> candidates, string query, int maxCount, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_deepSeekOptions.ApiKey))
            throw new InvalidOperationException("No API key");

        var client = _httpClientFactory.CreateClient("DeepSeek");
        client.BaseAddress = new Uri(_deepSeekOptions.BaseUrl);

        // 构建记忆列表
        var memoList = string.Join("\n", candidates.Select((m, i) =>
            $"{i}|{m.Key}|{m.Category}|{m.Value}"));

        var systemPrompt = string.Format(
            "给定用户查询和记忆列表，选出最相关的 {0} 条记忆。\n\n" +
            "用户查询：{1}\n\n" +
            "记忆列表（每条以 id|key|category|value 格式给出）：\n{2}\n\n" +
            "请仅用以下 JSON 格式回复（不要其他文字）：\n" +
            "{{\"selected\":[{{\"key\":\"...\",\"relevance\":\"...\"}}]}}",
            maxCount, query, memoList);

        var messages = new List<object>
        {
            new { role = "user", content = systemPrompt }
        };

        var body = JsonSerializer.Serialize(new
        {
            model = _deepSeekOptions.Model,
            messages,
            temperature = 0.2,
            max_tokens = 1000,
            stream = false
        }, _jsonOpts);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (req.Headers.Authorization == null)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _deepSeekOptions.ApiKey);

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new Exception("LLM call failed");

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(respJson);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content)) throw new Exception("No content");

        var json = content.Trim();
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }

        using var resultDoc = JsonDocument.Parse(json);
        if (!resultDoc.RootElement.TryGetProperty("selected", out var selectedEl)) throw new Exception("No selected");
        if (selectedEl.ValueKind != JsonValueKind.Array) throw new Exception("selected not array");

        var selectedKeys = new HashSet<string>(
            selectedEl.EnumerateArray()
                .Where(s => s.TryGetProperty("key", out _))
                .Select(s => s.GetProperty("key").GetString() ?? ""));

        var relevanceMap = selectedEl.EnumerateArray()
            .Where(s => s.TryGetProperty("key", out var rk))
            .ToDictionary(
                s => s.GetProperty("key").GetString() ?? "",
                s => s.TryGetProperty("relevance", out var r) ? r.GetString() : null
            );

        return candidates
            .Where(m => selectedKeys.Contains(m.Key))
            .Select(m => new RelevantMemoryDto
            {
                Id = m.Id, Key = m.Key, Value = m.Value, Category = m.Category,
                Tags = m.Tags, Importance = m.Importance, AccessedCount = m.AccessedCount,
                Source = m.Source, LastAccessedAt = m.LastAccessedAt,
                CreatedAt = m.CreatedAt, UpdatedAt = m.UpdatedAt,
                Relevance = relevanceMap.TryGetValue(m.Key, out var rel) ? rel : null
            }).ToList();
    }

    private static AgentMemoryDto Map(AgentMemory m) => new()
    {
        Id = m.Id,
        Key = m.Key,
        Value = m.Value,
        Category = m.Category,
        Tags = m.Tags,
        Importance = m.Importance,
        AccessedCount = m.AccessedCount,
        Source = m.Source,
        LastAccessedAt = m.LastAccessedAt,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt
    };
}
