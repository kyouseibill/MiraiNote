using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiraiNote.Data.Context;

namespace MiraiNote.Core.Services;

/// <summary>
/// 记忆衰减后台服务。每 6 小时扫描一次，对长期未访问的低重要性记忆进行衰减或清理。
/// 衰减策略基于访问频率（AccessedCount / 未访问天数）而非单纯的时间流逝：
/// - 高访问频率的低重要性记忆 → 保留（用户可能不需要经常查，但查的时候有用）
/// - 从未被访问过的重要性 1 记忆 → 软删除
/// - 低访问频率的记忆 → 降低重要性
/// </summary>
public class MemoryDecayBackgroundService : BackgroundService
{
    private static readonly TimeSpan DecayInterval = TimeSpan.FromHours(6);
    private readonly IServiceProvider _services;
    private readonly ILogger<MemoryDecayBackgroundService> _logger;

    public MemoryDecayBackgroundService(IServiceProvider services, ILogger<MemoryDecayBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("记忆衰减后台服务已启动，扫描周期：{Interval}", DecayInterval);

        // 启动后延迟，让数据库迁移先完成
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DecayOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "记忆衰减扫描出现异常，将在下一周期重试");
            }

            try
            {
                await Task.Delay(DecayInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("记忆衰减后台服务已停止");
    }

    private async Task DecayOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiraiNoteDbContext>();

        var allMemories = await db.AgentMemories
            .Where(m => !m.IsDeleted)
            .ToListAsync(ct);

        if (allMemories.Count == 0) return;

        var now = DateTime.UtcNow;
        int decayed = 0, deleted = 0;

        foreach (var mem in allMemories)
        {
            var daysSinceAccess = Math.Max(1.0, (now - mem.LastAccessedAt).TotalDays);
            var accessRatio = mem.AccessedCount / daysSinceAccess;

            // 从未被访问过的 Importance 1 记忆 → 软删除（自动提取的噪声记忆）
            if (mem.Importance <= 1 && mem.AccessedCount == 0 && daysSinceAccess >= 7)
            {
                mem.IsDeleted = true;
                deleted++;
                continue;
            }

            // 计算衰减值
            int decay;
            if (accessRatio < 0.1)
                decay = -2; // 几乎没被访问过 → 快速衰减
            else if (accessRatio < 0.3)
                decay = -1; // 低频访问 → 缓慢衰减
            else
                continue;  // 正常访问频率 → 不衰减

            var newImportance = Math.Max(1, (int)mem.Importance + decay);
            if (newImportance != mem.Importance)
            {
                mem.Importance = (byte)newImportance;
                decayed++;
            }
        }

        if (decayed > 0 || deleted > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "记忆衰减扫描完成：{Decayed} 条衰减，{Deleted} 条清理（扫描 {Total} 条）",
                decayed, deleted, allMemories.Count);
        }
    }
}
