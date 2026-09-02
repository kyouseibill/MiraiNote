using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Dtos.Mirai;

namespace MiraiNote.Core.Services.Mirai;

/// <summary>
/// 今日流/晨报共用的 EF LINQ 查询（全部经 LINQ，参数化由 EF 保证）。
/// </summary>
internal static class MiraiQueries
{
    /// <summary>今日（本地日 [startUtc, endUtc)）到期未完成 Memo，优先级降序。</summary>
    public static async Task<List<DueTaskDto>> GetDueTasksAsync(
        MiraiNoteDbContext db, int userId, DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        return await db.Memos
            .AsNoTracking()
            .Where(m => m.UserId == userId && !m.IsDone && !m.IsArchived
                && m.RemindAt != null && m.RemindAt >= startUtc && m.RemindAt < endUtc)
            .OrderByDescending(m => m.Priority)
            .ThenBy(m => m.RemindAt)
            .Select(m => new DueTaskDto(
                m.Id, m.Content, m.RemindAt, m.Priority, m.Section, m.IsDone, m.IsPinned))
            .ToListAsync(ct);
    }

    /// <summary>本地日 startUtc 之前逾期未完成 Memo，到期时间升序。</summary>
    public static async Task<List<DueTaskDto>> GetOverdueTasksAsync(
        MiraiNoteDbContext db, int userId, DateTime startUtc, CancellationToken ct)
    {
        return await db.Memos
            .AsNoTracking()
            .Where(m => m.UserId == userId && !m.IsDone && !m.IsArchived
                && m.RemindAt != null && m.RemindAt < startUtc)
            .OrderBy(m => m.RemindAt)
            .Select(m => new DueTaskDto(
                m.Id, m.Content, m.RemindAt, m.Priority, m.Section, m.IsDone, m.IsPinned))
            .ToListAsync(ct);
    }

    /// <summary>收件箱积压数：非终态（排除 Dispatched/Discarded）条数。</summary>
    public static Task<int> GetInboxPendingCountAsync(MiraiNoteDbContext db, int userId, CancellationToken ct)
        => db.InboxItems
            .AsNoTracking()
            .CountAsync(i => i.UserId == userId
                && i.Status != (byte)InboxStatus.Dispatched
                && i.Status != (byte)InboxStatus.Discarded, ct);

    /// <summary>本周（本地周一起始）记录数：WorkLog + LifeLog 按 LogDate 计。</summary>
    public static async Task<int> GetWeekEntryCountAsync(
        MiraiNoteDbContext db, int userId, DateOnly localDate, CancellationToken ct)
    {
        var (weekStart, weekEnd) = MiraiTime.LocalWeekRange(localDate);
        var weekStartDateTime = weekStart.ToDateTime(TimeOnly.MinValue);
        var weekEndDateTime = weekEnd.ToDateTime(TimeOnly.MinValue);

        var workLogCount = await db.WorkLogs
            .AsNoTracking()
            .CountAsync(w => w.UserId == userId && w.LogDate >= weekStartDateTime && w.LogDate <= weekEndDateTime, ct);
        var lifeLogCount = await db.LifeLogs
            .AsNoTracking()
            .CountAsync(l => l.UserId == userId && l.LogDate >= weekStartDateTime && l.LogDate <= weekEndDateTime, ct);
        return workLogCount + lifeLogCount;
    }
}
