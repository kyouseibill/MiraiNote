using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
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

        // 近 7 天（含今日，按 UTC 日）：按天零填充，便于前端画稳定的时间轴。
        var todayUtc = DateTime.UtcNow.Date;
        var windowStart = todayUtc.AddDays(-6);
        var byDate = await logs
            .Where(l => l.CreatedAt >= windowStart)
            .Select(l => new { Day = EF.Property<DateTime>(l, nameof(AIActionLog.CreatedAt)).Date })
            .GroupBy(x => x.Day)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countByDate = byDate.ToDictionary(x => x.Day, x => x.Count);
        var last7Days = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var day = windowStart.AddDays(offset);
                return new DateCountDto(day.ToString("yyyy-MM-dd"), countByDate.GetValueOrDefault(day));
            })
            .ToList();

        return new AiActionStatsDto(total, byActionType, last7Days);
    }
}
