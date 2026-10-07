using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared;
using MiraiNote.Shared.Dtos.Mirai;

namespace MiraiNote.Core.Services.Mirai;

/// <summary>
/// AI 调用统计（Mirai M1 设置页）：总量 / 按动作类型 / 近 7 天分布。
/// </summary>
public interface IMiraiStatsService
{
    /// <summary>GET /mirai/stats/ai-actions。</summary>
    Task<AiActionStatsDto> GetAiActionStatsAsync(int userId, CancellationToken ct = default);
}

/// <inheritdoc />
public class MiraiStatsService : IMiraiStatsService
{
    private readonly MiraiNoteDbContext _db;

    public MiraiStatsService(MiraiNoteDbContext db)
    {
        _db = db;
    }

    public async Task<AiActionStatsDto> GetAiActionStatsAsync(int userId, CancellationToken ct = default)
    {
        var logs = _db.AIActionLogs.AsNoTracking().Where(l => l.UserId == userId);

        var total = await logs.CountAsync(ct);
        // 分组计数在库端完成，StringComparer 排序在内存端（EF 无法翻译自定义 comparer）
        var byActionTypeRaw = await logs
            .GroupBy(l => l.ActionType)
            .Select(g => new { ActionType = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var byActionType = byActionTypeRaw
            .Select(x => new ActionTypeCountDto(x.ActionType, x.Count))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.ActionType, StringComparer.Ordinal)
            .ToList();

        // 近 7 天（含今日，按上海日历日）在内存分桶。EF 不能可移植地把 timestamptz 转成 Asia/Shanghai。
        var today = ShanghaiClock.Today(DateTimeOffset.UtcNow);
        var windowStart = ShanghaiClock.DayRangeUtc(today.AddDays(-6)).StartUtc;
        var createdAts = await logs
            .Where(l => l.CreatedAt >= windowStart)
            .Select(l => l.CreatedAt)
            .ToListAsync(ct);
        var countByDate = createdAts
            .GroupBy(created => ShanghaiClock.ToShanghaiDate(new DateTimeOffset(AsUtc(created))))
            .ToDictionary(g => g.Key, g => g.Count());
        var last7Days = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var day = today.AddDays(offset - 6);
                return new DateCountDto(day.ToString("yyyy-MM-dd"), countByDate.GetValueOrDefault(day));
            })
            .ToList();

        return new AiActionStatsDto(total, byActionType, last7Days);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
