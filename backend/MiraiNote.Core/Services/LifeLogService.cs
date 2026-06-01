using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;
using MiraiNote.Shared.Dtos.LifeLogs;

namespace MiraiNote.Core.Services;

public interface ILifeLogService
{
    Task<PagedResult<LifeLogDto>> GetListAsync(int userId, LifeLogListQuery query, CancellationToken ct = default);
    Task<LifeLogDto> GetByIdAsync(int userId, int id, CancellationToken ct = default);
    Task<LifeLogDto> CreateAsync(int userId, CreateLifeLogRequest request, CancellationToken ct = default);
    Task<LifeLogDto> UpdateAsync(int userId, int id, UpdateLifeLogRequest request, CancellationToken ct = default);
    Task DeleteAsync(int userId, int id, CancellationToken ct = default);
}

/// <summary>
/// 生活记录业务实现。所有方法均按 UserId 隔离数据。
/// </summary>
public class LifeLogService : ILifeLogService
{
    private readonly MiraiNoteDbContext _db;

    public LifeLogService(MiraiNoteDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<LifeLogDto>> GetListAsync(int userId, LifeLogListQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var q = _db.LifeLogs.AsNoTracking().Where(l => l.UserId == userId);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(l => l.Content.Contains(kw));
        }

        if (!string.IsNullOrWhiteSpace(query.Mood))
        {
            q = q.Where(l => l.Mood == query.Mood);
        }

        if (!string.IsNullOrWhiteSpace(query.Month))
        {
            // 格式 yyyy-MM
            if (DateTime.TryParseExact(query.Month, "yyyy-MM",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var monthStart))
            {
                var monthEnd = monthStart.AddMonths(1);
                q = q.Where(l => l.LogDate >= monthStart && l.LogDate < monthEnd);
            }
        }

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(l => l.LogDate)
            .ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => Map(l))
            .ToListAsync(ct);

        return new PagedResult<LifeLogDto>
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items
        };
    }

    public async Task<LifeLogDto> GetByIdAsync(int userId, int id, CancellationToken ct = default)
    {
        var entity = await _db.LifeLogs.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId, ct)
            ?? throw new BusinessException("生活记录不存在", 404);
        return Map(entity);
    }

    public async Task<LifeLogDto> CreateAsync(int userId, CreateLifeLogRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new BusinessException("内容不能为空", 400);

        var entity = new LifeLog
        {
            UserId = userId,
            Content = request.Content.Trim(),
            Mood = NullIfBlank(request.Mood),
            ImagePath = NullIfBlank(request.ImagePath),
            LogDate = request.LogDate.Date
        };
        _db.LifeLogs.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<LifeLogDto> UpdateAsync(int userId, int id, UpdateLifeLogRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new BusinessException("内容不能为空", 400);

        var entity = await _db.LifeLogs.FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId, ct)
            ?? throw new BusinessException("生活记录不存在", 404);

        entity.Content = request.Content.Trim();
        entity.Mood = NullIfBlank(request.Mood);
        entity.ImagePath = NullIfBlank(request.ImagePath);
        entity.LogDate = request.LogDate.Date;

        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var entity = await _db.LifeLogs.FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId, ct)
            ?? throw new BusinessException("生活记录不存在", 404);

        entity.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    private static LifeLogDto Map(LifeLog l) => new()
    {
        Id = l.Id,
        Content = l.Content,
        Mood = l.Mood,
        ImagePath = l.ImagePath,
        LogDate = l.LogDate,
        CreatedAt = l.CreatedAt,
        UpdatedAt = l.UpdatedAt
    };

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
