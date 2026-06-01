using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;
using MiraiNote.Shared.Dtos.WorkLogs;

namespace MiraiNote.Core.Services;

public interface IWorkLogService
{
    Task<PagedResult<WorkLogDto>> GetListAsync(int userId, WorkLogListQuery query, CancellationToken ct = default);
    Task<WorkLogDto> GetByIdAsync(int userId, int id, CancellationToken ct = default);
    Task<WorkLogDto> CreateAsync(int userId, CreateWorkLogRequest request, CancellationToken ct = default);
    Task<WorkLogDto> UpdateAsync(int userId, int id, UpdateWorkLogRequest request, CancellationToken ct = default);
    Task DeleteAsync(int userId, int id, CancellationToken ct = default);
    /// <summary>返回当前用户所有已用过的分类（去重，升序），用于前端自动补全。</summary>
    Task<List<string>> GetCategoriesAsync(int userId, CancellationToken ct = default);
}

/// <summary>
/// 工作记录业务实现。
/// 所有方法均按 UserId 隔离数据，防止越权访问。
/// </summary>
public class WorkLogService : IWorkLogService
{
    private readonly MiraiNoteDbContext _db;

    public WorkLogService(MiraiNoteDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<WorkLogDto>> GetListAsync(int userId, WorkLogListQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var q = _db.WorkLogs.AsNoTracking().Where(w => w.UserId == userId);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(w =>
                w.Title.Contains(kw) ||
                (w.Purpose != null && w.Purpose.Contains(kw)) ||
                (w.Content != null && w.Content.Contains(kw)) ||
                (w.Tags != null && w.Tags.Contains(kw)));
        }
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            q = q.Where(w => w.Category == query.Category);
        }
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim();
            q = q.Where(w => w.Tags != null && w.Tags.Contains(tag));
        }
        if (query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value.Date;
            q = q.Where(w => w.LogDate >= from);
        }
        if (query.DateTo.HasValue)
        {
            var to = query.DateTo.Value.Date;
            q = q.Where(w => w.LogDate <= to);
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(w => w.LogDate)
            .ThenByDescending(w => w.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(w => Map(w))
            .ToListAsync(ct);

        return new PagedResult<WorkLogDto>
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items
        };
    }

    public async Task<WorkLogDto> GetByIdAsync(int userId, int id, CancellationToken ct = default)
    {
        var entity = await _db.WorkLogs.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId, ct)
            ?? throw new BusinessException("工作记录不存在", 404);
        return Map(entity);
    }

    public async Task<WorkLogDto> CreateAsync(int userId, CreateWorkLogRequest request, CancellationToken ct = default)
    {
        Validate(request.Title, request.LogDate);

        var entity = new WorkLog
        {
            UserId = userId,
            Title = request.Title.Trim(),
            Purpose = NullIfBlank(request.Purpose),
            Content = NullIfBlank(request.Content),
            Tags = NormalizeTags(request.Tags),
            Category = NullIfBlank(request.Category),
            LogDate = request.LogDate.Date
        };
        _db.WorkLogs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<WorkLogDto> UpdateAsync(int userId, int id, UpdateWorkLogRequest request, CancellationToken ct = default)
    {
        Validate(request.Title, request.LogDate);

        var entity = await _db.WorkLogs.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId, ct)
            ?? throw new BusinessException("工作记录不存在", 404);

        entity.Title = request.Title.Trim();
        entity.Purpose = NullIfBlank(request.Purpose);
        entity.Content = NullIfBlank(request.Content);
        entity.Tags = NormalizeTags(request.Tags);
        entity.Category = NullIfBlank(request.Category);
        entity.LogDate = request.LogDate.Date;

        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var entity = await _db.WorkLogs.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId, ct)
            ?? throw new BusinessException("工作记录不存在", 404);
        entity.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<string>> GetCategoriesAsync(int userId, CancellationToken ct = default)
    {
        return await _db.WorkLogs
            .AsNoTracking()
            .Where(w => w.UserId == userId && w.Category != null)
            .Select(w => w.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);
    }

    // ===== 私有辅助 =====

    private static void Validate(string title, DateTime logDate)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new BusinessException("标题不能为空");
        }
        if (title.Length > 200)
        {
            throw new BusinessException("标题不能超过 200 字");
        }
        if (logDate == default)
        {
            throw new BusinessException("请选择记录日期");
        }
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>规范化标签：去空、去重、用逗号拼接。</summary>
    private static string? NormalizeTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(",", parts);
    }

    private static WorkLogDto Map(WorkLog w) => new()
    {
        Id = w.Id,
        Title = w.Title,
        Purpose = w.Purpose,
        Content = w.Content,
        Tags = w.Tags,
        Category = w.Category,
        LogDate = w.LogDate,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt
    };
}
