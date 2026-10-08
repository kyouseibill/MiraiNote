using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Shared;

namespace MiraiNote.Core.Services;

public interface IWelcomeGreetingService
{
    Task<WelcomeGreeting> GetGreetingAsync(int userId, DateTimeOffset utcNow, CancellationToken ct = default);
}

/// <summary>工作台欢迎语：名字、上海日历日，以及今天的备忘情况。</summary>
public sealed record WelcomeGreeting(string Content, string? FeatureNote);

/// <summary>
/// 工作台欢迎语。只根据当前用户的备忘和随发版写入的功能句生成，不调用外部接口。
/// </summary>
public sealed class WelcomeGreetingService : IWelcomeGreetingService
{
    private readonly MiraiNoteDbContext _db;
    private readonly IReadOnlyList<FeatureLaunchNote> _featureNotes;

    public WelcomeGreetingService(MiraiNoteDbContext db)
        : this(db, FeatureLaunchCatalog.Release)
    {
    }

    public WelcomeGreetingService(MiraiNoteDbContext db, FeatureLaunchCatalog catalog)
    {
        _db = db;
        _featureNotes = catalog.Notes;
    }

    public async Task<WelcomeGreeting> GetGreetingAsync(
        int userId,
        DateTimeOffset utcNow,
        CancellationToken ct = default)
    {
        var today = ShanghaiClock.Today(utcNow);
        var (startUtc, endUtc) = ShanghaiClock.DayRangeUtc(today);

        var username = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(ct);
        var name = string.IsNullOrWhiteSpace(username) ? "你" : username.Trim();

        var reminds = await _db.Memos.AsNoTracking()
            .Where(m => m.UserId == userId && !m.IsDone && !m.IsArchived)
            .Select(m => m.RemindAt)
            .ToListAsync(ct);

        var dueToday = 0;
        var unfinishedElsewhere = 0;
        foreach (var remindAt in reminds)
        {
            if (remindAt is DateTime at && at >= startUtc && at < endUtc)
                dueToday++;
            else
                unfinishedElsewhere++;
        }

        return new WelcomeGreeting(
            Compose(name, today, dueToday, unfinishedElsewhere),
            FeatureLaunchNotes.Select(_featureNotes, today));
    }

    /// <summary>
    /// 没有今天到期的备忘、也没有其他未完成备忘时，只留名字和日期。
    /// 到期只统计上海当天、尚未完成的备忘；其余未完成备忘另说。
    /// </summary>
    public static string Compose(string name, DateOnly today, int dueToday, int unfinishedElsewhere)
    {
        var head = $"{name}，{today.ToString("M月d日", CultureInfo.InvariantCulture)}";
        var parts = new List<string>(2);
        if (dueToday > 0)
            parts.Add($"今天有 {dueToday} 条备忘到期");
        if (unfinishedElsewhere > 0)
            parts.Add($"还有 {unfinishedElsewhere} 条备忘没做完");
        if (parts.Count == 0)
            return head;
        return $"{head}。{string.Join("，", parts)}。";
    }
}
