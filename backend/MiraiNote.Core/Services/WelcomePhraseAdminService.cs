using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Welcome;

namespace MiraiNote.Core.Services;

public interface IWelcomePhraseAdminService
{
    Task<IReadOnlyList<WelcomePhraseDto>> ListAsync(string? kind, CancellationToken ct = default);
    Task<WelcomePhraseDto> CreateAsync(WelcomePhraseWriteRequest request, CancellationToken ct = default);
    Task<WelcomePhraseDto> UpdateAsync(int id, WelcomePhraseWriteRequest request, CancellationToken ct = default);
    Task<WelcomePhraseDto> SetEnabledAsync(int id, bool isEnabled, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class WelcomePhraseAdminService : IWelcomePhraseAdminService
{
    private readonly MiraiNoteDbContext _db;

    public WelcomePhraseAdminService(MiraiNoteDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<WelcomePhraseDto>> ListAsync(string? kind, CancellationToken ct = default)
    {
        var query = _db.WelcomePhrases.AsNoTracking();
        var normalizedKind = NormalizeOptional(kind);
        if (normalizedKind != null)
        {
            EnsureKnown(WelcomePhraseKind.All, normalizedKind, "类型只能是 greeting、poem 或 quote");
            query = query.Where(row => row.Kind == normalizedKind);
        }

        var rows = await query
            .OrderBy(row => row.SortOrder)
            .ThenBy(row => row.Id)
            .ToListAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<WelcomePhraseDto> CreateAsync(WelcomePhraseWriteRequest request, CancellationToken ct = default)
    {
        var entity = new WelcomePhrase();
        Apply(entity, request);
        _db.WelcomePhrases.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<WelcomePhraseDto> UpdateAsync(int id, WelcomePhraseWriteRequest request, CancellationToken ct = default)
    {
        var entity = await FindAsync(id, ct);
        Apply(entity, request);
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<WelcomePhraseDto> SetEnabledAsync(int id, bool isEnabled, CancellationToken ct = default)
    {
        var entity = await FindAsync(id, ct);
        entity.IsEnabled = isEnabled;
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await FindAsync(id, ct);
        entity.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<WelcomePhrase> FindAsync(int id, CancellationToken ct)
    {
        return await _db.WelcomePhrases.FirstOrDefaultAsync(row => row.Id == id, ct)
            ?? throw new BusinessException("欢迎语不存在", 404);
    }

    private static void Apply(WelcomePhrase entity, WelcomePhraseWriteRequest request)
    {
        var kind = RequireToken(request.Kind, WelcomePhraseKind.All, "类型只能是 greeting、poem 或 quote");
        var text = request.Text?.Trim() ?? "";
        if (text.Length == 0)
            throw new BusinessException("请填写正文");
        if (text.Length > WelcomePhrase.MaxTextLength)
            throw new BusinessException($"正文请控制在 {WelcomePhrase.MaxTextLength} 字以内");

        var author = NormalizeOptional(request.Author);
        var source = NormalizeOptional(request.Source);
        var period = NormalizeOptional(request.Period);
        var special = NormalizeOptional(request.Special);
        var season = NormalizeOptional(request.Season);
        if (author != null && author.Length > WelcomePhrase.MaxAuthorLength)
            throw new BusinessException($"作者请控制在 {WelcomePhrase.MaxAuthorLength} 字以内");
        if (source != null && source.Length > WelcomePhrase.MaxSourceLength)
            throw new BusinessException($"出处请控制在 {WelcomePhrase.MaxSourceLength} 字以内");
        if (period != null)
            EnsureKnown(WelcomePhrasePeriod.All, period, "时段只能是 morning、noon、afternoon、evening 或 latenight");
        if (special != null)
            EnsureKnown(WelcomePhraseSpecial.All, special, "特殊场景只能是 rain 或 friday");
        if (season != null)
            EnsureKnown(WelcomePhraseSeason.All, season, "季节只能是 spring、summer、autumn 或 winter");

        entity.Kind = kind;
        entity.Text = text;
        entity.Author = author;
        entity.Source = source;
        entity.Period = period;
        entity.Special = special;
        entity.Season = season;
        entity.IsEnabled = request.IsEnabled;
        entity.SortOrder = request.SortOrder;
    }

    private static string RequireToken(string? value, IReadOnlyList<string> allowed, string message)
    {
        var token = NormalizeOptional(value) ?? throw new BusinessException(message);
        EnsureKnown(allowed, token, message);
        return token;
    }

    private static void EnsureKnown(IReadOnlyList<string> allowed, string value, string message)
    {
        if (!allowed.Contains(value, StringComparer.Ordinal))
            throw new BusinessException(message);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static WelcomePhraseDto Map(WelcomePhrase entity) => new()
    {
        Id = entity.Id,
        Kind = entity.Kind,
        Text = entity.Text,
        Author = entity.Author,
        Source = entity.Source,
        Period = entity.Period,
        Special = entity.Special,
        Season = entity.Season,
        IsEnabled = entity.IsEnabled,
        SortOrder = entity.SortOrder,
        UpdatedAt = entity.UpdatedAt,
    };
}
