using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;

namespace MiraiNote.Core.Services;

public interface IWelcomeNewsSeenStore
{
    /// <summary>该用户近 7 天内展示过的链接。读取时会删掉更早的行。</summary>
    Task<IReadOnlySet<string>> GetRecentUrlsAsync(int userId, DateTimeOffset utcNow, CancellationToken ct = default);

    /// <summary>记下这次展示的链接，并删掉该用户 7 天前的行。</summary>
    Task RecordAsync(int userId, IReadOnlyList<string> urls, DateTimeOffset utcNow, CancellationToken ct = default);
}

/// <summary>
/// 欢迎语已展示链接。每次调用单独开一个数据库上下文，避免和问候语里并行的用户查询抢同一个上下文。
/// </summary>
public sealed class WelcomeNewsSeenStore : IWelcomeNewsSeenStore
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    private readonly Func<CancellationToken, Task<Lease>> _open;

    public WelcomeNewsSeenStore(IServiceScopeFactory scopes)
    {
        _open = _ => OpenScopeAsync(scopes);
    }

    internal WelcomeNewsSeenStore(MiraiNoteDbContext db)
    {
        _open = _ => Task.FromResult(new Lease(db, scope: null));
    }

    public async Task<IReadOnlySet<string>> GetRecentUrlsAsync(
        int userId,
        DateTimeOffset utcNow,
        CancellationToken ct = default)
    {
        if (userId <= 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var cutoff = Cutoff(utcNow);
        await using var lease = await _open(ct);
        await PruneAsync(lease.Db, userId, cutoff, ct);
        var urls = await lease.Db.WelcomeNewsSeens.AsNoTracking()
            .Where(row => row.UserId == userId && row.ShownAt >= cutoff)
            .Select(row => row.Url)
            .ToListAsync(ct);
        return urls.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task RecordAsync(
        int userId,
        IReadOnlyList<string> urls,
        DateTimeOffset utcNow,
        CancellationToken ct = default)
    {
        if (userId <= 0 || urls.Count == 0)
            return;

        var normalized = Normalize(urls);
        if (normalized.Count == 0)
            return;

        var cutoff = Cutoff(utcNow);
        var shownAt = utcNow.ToUniversalTime().UtcDateTime;
        await using var lease = await _open(ct);
        var db = lease.Db;
        await PruneAsync(db, userId, cutoff, ct);

        var existing = await db.WelcomeNewsSeens
            .Where(row => row.UserId == userId && normalized.Contains(row.Url))
            .ToListAsync(ct);
        var byUrl = existing.ToDictionary(row => row.Url, StringComparer.Ordinal);

        foreach (var url in normalized)
        {
            if (byUrl.TryGetValue(url, out var row))
            {
                row.ShownAt = shownAt;
                continue;
            }

            db.WelcomeNewsSeens.Add(new WelcomeNewsSeen
            {
                UserId = userId,
                Url = url,
                ShownAt = shownAt
            });
        }

        await db.SaveChangesAsync(ct);
    }

    internal static DateTime Cutoff(DateTimeOffset utcNow) =>
        utcNow.ToUniversalTime().Add(-Window).UtcDateTime;

    private static async Task PruneAsync(MiraiNoteDbContext db, int userId, DateTime cutoff, CancellationToken ct)
    {
        await db.WelcomeNewsSeens
            .Where(row => row.UserId == userId && row.ShownAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    private static List<string> Normalize(IReadOnlyList<string> urls)
    {
        var result = new List<string>(urls.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in urls)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
                continue;
            if (uri.Scheme is not ("https" or "http"))
                continue;
            var key = WelcomeNewsMerge.DedupeKey(uri);
            if (key.Length == 0 || key.Length > WelcomeNewsSeen.MaxUrlLength)
                continue;
            if (seen.Add(key))
                result.Add(key);
        }

        return result;
    }

    private static async Task<Lease> OpenScopeAsync(IServiceScopeFactory scopes)
    {
        var scope = scopes.CreateAsyncScope();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<MiraiNoteDbContext>();
            return new Lease(db, scope);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(MiraiNoteDbContext db, IAsyncDisposable? scope) : IAsyncDisposable
    {
        public MiraiNoteDbContext Db { get; } = db;

        public ValueTask DisposeAsync() => scope?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
