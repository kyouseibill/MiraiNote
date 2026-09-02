using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Dtos.Mirai;

namespace MiraiNote.Core.Services.Mirai;

/// <summary>
/// 今日流聚合业务（Mirai M1）：晨报（无则触发生成）+ 到期/逾期任务 + 当日时间线 + 统计角标。
/// </summary>
public interface IDayOverviewService
{
    /// <summary>GET /mirai/day/overview?date=&amp;tzOffsetMinutes=：聚合当日视图。</summary>
    Task<DayOverviewDto> GetOverviewAsync(
        int userId, DateOnly date, int tzOffsetMinutes, CancellationToken ct = default);
}

/// <inheritdoc />
public class DayOverviewService : IDayOverviewService
{
    private const int TitleMaxLength = 50;

    private readonly MiraiNoteDbContext _db;
    private readonly IBriefingService _briefingService;

    public DayOverviewService(MiraiNoteDbContext db, IBriefingService briefingService)
    {
        _db = db;
        _briefingService = briefingService;
    }

    public async Task<DayOverviewDto> GetOverviewAsync(
        int userId, DateOnly date, int tzOffsetMinutes, CancellationToken ct = default)
    {
        // 晨报：当日无则触发生成（占位行防并发）；生成失败不抛错，降级为 briefingError。
        var briefingOutcome = await _briefingService.GetOrGenerateAsync(userId, date, tzOffsetMinutes, ct);

        var (dayStartUtc, dayEndUtc) = MiraiTime.LocalDayRangeUtc(date, tzOffsetMinutes);
        var dueTasks = await MiraiQueries.GetDueTasksAsync(_db, userId, dayStartUtc, dayEndUtc, ct);
        var overdueTasks = await MiraiQueries.GetOverdueTasksAsync(_db, userId, dayStartUtc, ct);

        var feed = await BuildTodayFeedAsync(userId, date, dayStartUtc, dayEndUtc, ct);
        var inboxPendingCount = await MiraiQueries.GetInboxPendingCountAsync(_db, userId, ct);
        var weekEntryCount = await MiraiQueries.GetWeekEntryCountAsync(_db, userId, date, ct);

        return new DayOverviewDto(
            date.ToString("yyyy-MM-dd"),
            briefingOutcome.Briefing,
            briefingOutcome.Error,
            dueTasks,
            overdueTasks,
            feed,
            inboxPendingCount,
            weekEntryCount);
    }

    /// <summary>
    /// 当地时间线（按时间升序）：capture / worklog / lifelog / memo / task / briefing。
    /// kind 约定：Memo 带 RemindAt 视为 task，否则 memo；aiSummary M1 恒 null（M2 写后提炼预留）。
    /// </summary>
    private async Task<List<FeedItemDto>> BuildTodayFeedAsync(
        int userId, DateOnly date, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken ct)
    {
        var feed = new List<FeedItemDto>();

        feed.AddRange((await _db.InboxItems
                .AsNoTracking()
                .Where(i => i.UserId == userId && i.CreatedAt >= dayStartUtc && i.CreatedAt < dayEndUtc)
                .Select(i => new { i.Id, i.Raw, i.CreatedAt })
                .ToListAsync(ct))
            .Select(i => new FeedItemDto(i.CreatedAt, "capture", Truncate(i.Raw), i.Id, null)));

        feed.AddRange((await _db.WorkLogs
                .AsNoTracking()
                .Where(w => w.UserId == userId && w.CreatedAt >= dayStartUtc && w.CreatedAt < dayEndUtc)
                .Select(w => new { w.Id, w.Title, w.CreatedAt })
                .ToListAsync(ct))
            .Select(w => new FeedItemDto(w.CreatedAt, "worklog", Truncate(w.Title), w.Id, null)));

        feed.AddRange((await _db.LifeLogs
                .AsNoTracking()
                .Where(l => l.UserId == userId && l.CreatedAt >= dayStartUtc && l.CreatedAt < dayEndUtc)
                .Select(l => new { l.Id, l.Content, l.CreatedAt })
                .ToListAsync(ct))
            .Select(l => new FeedItemDto(l.CreatedAt, "lifelog", Truncate(l.Content), l.Id, null)));

        feed.AddRange((await _db.Memos
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.CreatedAt >= dayStartUtc && m.CreatedAt < dayEndUtc)
                .Select(m => new { m.Id, m.Content, m.RemindAt, m.CreatedAt })
                .ToListAsync(ct))
            .Select(m => new FeedItemDto(
                m.CreatedAt, m.RemindAt != null ? "task" : "memo", Truncate(m.Content), m.Id, null)));

        var briefing = await _db.DailyBriefings
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.BriefDate == date)
            .OrderByDescending(b => b.GeneratedAt)
            .Select(b => new { b.Id, b.GeneratedAt })
            .FirstOrDefaultAsync(ct);
        if (briefing != null)
            feed.Add(new FeedItemDto(briefing.GeneratedAt, "briefing", "今日晨报", briefing.Id, null));

        return feed.OrderBy(f => f.Time).ThenBy(f => f.RefId ?? 0).ToList();
    }

    private static string Truncate(string value) =>
        value.Length <= TitleMaxLength ? value : value[..TitleMaxLength];
}
