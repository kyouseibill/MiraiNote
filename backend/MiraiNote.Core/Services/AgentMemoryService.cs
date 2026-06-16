using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
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

    /// <summary>自动从对话内容中提取关键信息并存储为记忆</summary>
    Task AutoExtractAsync(int userId, string userMessage, string? assistantResponse, CancellationToken ct = default);
}

public class AgentMemoryService : IAgentMemoryService
{
    private readonly MiraiNoteDbContext _db;

    public AgentMemoryService(MiraiNoteDbContext db) { _db = db; }

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

        // 访问时增加重要性
        if (entity.Importance < 5)
            entity.Importance++;
        entity.LastAccessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Map(entity);
    }

    public async Task<AgentMemoryDto> CreateAsync(int userId, CreateMemoryRequest r, CancellationToken ct = default)
    {
        // Upsert：key 已存在则更新
        var existing = await _db.AgentMemories
            .FirstOrDefaultAsync(m => m.UserId == userId && m.Key == r.Key, ct);

        if (existing != null)
        {
            existing.Value = r.Value;
            existing.Category = r.Category;
            existing.Tags = r.Tags;
            existing.Importance = r.Importance;
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

    /// <summary>
    /// 从对话中自动提取偏好和上下文。简单启发式 + 关键词匹配。
    /// </summary>
    public async Task AutoExtractAsync(int userId, string userMessage, string? assistantResponse, CancellationToken ct = default)
    {
        var text = userMessage;
        if (!string.IsNullOrWhiteSpace(assistantResponse))
            text += " " + assistantResponse;

        // 提取"记住"开头的偏好
        if (userMessage.Contains("记住", StringComparison.Ordinal) ||
            userMessage.Contains("我喜欢", StringComparison.Ordinal) ||
            userMessage.Contains("我习惯", StringComparison.Ordinal) ||
            userMessage.Contains("我常用", StringComparison.Ordinal))
        {
            var key = "pref_" + Guid.NewGuid().ToString("N")[..8];
            var value = userMessage.Length > 200 ? userMessage[..200] : userMessage;
            await CreateAsync(userId, new CreateMemoryRequest
            {
                Key = key,
                Value = value,
                Category = "preference",
                Importance = 4
            }, ct);
        }
    }

    private static AgentMemoryDto Map(AgentMemory m) => new()
    {
        Id = m.Id,
        Key = m.Key,
        Value = m.Value,
        Category = m.Category,
        Tags = m.Tags,
        Importance = m.Importance,
        LastAccessedAt = m.LastAccessedAt,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt
    };
}
