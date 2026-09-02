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
    private const int MaxImages = 9;
    private const string MultiImagePrefix = "multi:v1:";
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

        var imagePaths = NormalizeImagePaths(request.ImagePaths, request.ImagePath);
        var entity = new LifeLog
        {
            UserId = userId,
            Content = request.Content.Trim(),
            Mood = NullIfBlank(request.Mood),
            ImagePath = SerializeImagePaths(imagePaths),
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

        var imagePaths = NormalizeImagePaths(request.ImagePaths, request.ImagePath);

        entity.Content = request.Content.Trim();
        entity.Mood = NullIfBlank(request.Mood);
        entity.ImagePath = SerializeImagePaths(imagePaths);
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

    private static LifeLogDto Map(LifeLog l)
    {
        var imagePaths = DeserializeImagePaths(l.ImagePath);
        return new LifeLogDto
        {
            Id = l.Id,
            Content = l.Content,
            Mood = l.Mood,
            ImagePath = imagePaths.FirstOrDefault(),
            ImagePaths = imagePaths,
            LogDate = l.LogDate,
            CreatedAt = l.CreatedAt,
            UpdatedAt = l.UpdatedAt
        };
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static List<string> NormalizeImagePaths(IEnumerable<string>? imagePaths, string? legacyImagePath)
    {
        var paths = (imagePaths ?? [])
            .Select(NullIfBlank)
            .Where(path => path != null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (paths.Count == 0 && NullIfBlank(legacyImagePath) is { } legacyPath)
            paths.Add(legacyPath);

        if (paths.Count > MaxImages)
            throw new BusinessException($"每条生活记录最多上传 {MaxImages} 张图片", 400);

        if (paths.Any(path => path.Length > 500))
            throw new BusinessException("图片路径过长", 400);

        return paths;
    }

    private static string? SerializeImagePaths(List<string> paths)
    {
        if (paths.Count == 0) return null;
        if (paths.Count == 1) return paths[0];

        var slashIndex = paths[0].LastIndexOf('/');
        var commonDirectory = slashIndex >= 0 ? paths[0][..(slashIndex + 1)] : string.Empty;
        var canCompact = commonDirectory.Length > 0 && paths.All(path =>
            path.StartsWith(commonDirectory, StringComparison.Ordinal) &&
            !path[commonDirectory.Length..].Contains('/'));

        var values = canCompact
            ? paths.Select(path => path[commonDirectory.Length..])
            : paths;
        var encoded = $"{MultiImagePrefix}{(canCompact ? commonDirectory : string.Empty)}|{string.Join('|', values)}";

        if (encoded.Length > 500)
            throw new BusinessException("图片路径总长度超过存储限制", 400);

        return encoded;
    }

    private static List<string> DeserializeImagePaths(string? storedValue)
    {
        var value = NullIfBlank(storedValue);
        if (value == null) return [];
        if (!value.StartsWith(MultiImagePrefix, StringComparison.Ordinal)) return [value];

        var parts = value[MultiImagePrefix.Length..].Split('|');
        if (parts.Length < 2) return [];

        var commonDirectory = parts[0];
        return parts
            .Skip(1)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Take(MaxImages)
            .Select(part => commonDirectory + part)
            .ToList();
    }
}
