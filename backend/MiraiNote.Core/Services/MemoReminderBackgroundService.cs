using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiraiNote.Data.Context;

namespace MiraiNote.Core.Services;

/// <summary>
/// 后台备忘提醒服务：每分钟扫描一次到期且需邮件提醒、未发送过的备忘，
/// 调用 IEmailService 发邮件，并写回 EmailReminderSent / RemindedAt。
/// 该服务独立于前端，只要后端在运行即可触发。
/// </summary>
public class MemoReminderBackgroundService : BackgroundService
{
    private const byte ReminderEmail = 2;
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;
    private readonly ILogger<MemoReminderBackgroundService> _logger;

    public MemoReminderBackgroundService(IServiceProvider services, ILogger<MemoReminderBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("备忘提醒后台服务已启动，扫描周期：{Interval}", ScanInterval);

        // 启动后稍延迟一点，让数据库迁移先完成
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "备忘提醒扫描发生异常");
            }

            try { await Task.Delay(ScanInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiraiNoteDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var now = DateTime.UtcNow;

        // 找出所有到期、需要邮件提醒、未发送过、未完成/归档的备忘 + 关联用户邮箱
        // 只取 2 小时内到期的，超过 2 小时仍未发出则视为放弃，避免无限重试
        var deadline = now.AddHours(-2);
        var due = await db.Memos
            .Where(m =>
                !m.IsDone &&
                !m.IsArchived &&
                m.RemindAt != null &&
                m.RemindAt <= now &&
                (m.RemindMethods & ReminderEmail) == ReminderEmail &&
                !m.EmailReminderSent)
            .Join(db.Users,
                m => m.UserId,
                u => u.Id,
                (m, u) => new { Memo = m, u.Email, u.Username, u.IsActive })
            .Where(x => x.IsActive && x.Email != null && x.Email != "")
            .OrderBy(x => x.Memo.RemindAt)
            .Take(50) // 单次最多处理 50 条，避免长时间占用
            .ToListAsync(ct);

        if (due.Count == 0) return;

        _logger.LogInformation("发现 {Count} 条到期邮件提醒，开始处理", due.Count);

        foreach (var item in due)
        {
            if (ct.IsCancellationRequested) break;

            var memo = item.Memo;

            // 超过 2 小时仍未成功，放弃重试以免无限循环
            if (memo.RemindAt < deadline)
            {
                _logger.LogWarning(
                    "备忘提醒已超时放弃：MemoId={MemoId}, RemindAt={RemindAt}, To={Email}",
                    memo.Id, memo.RemindAt, item.Email);
                memo.EmailReminderSent = true;
                memo.RemindedAt ??= DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                // 转换为 UTC+8 用于邮件展示
                var remindLocal = memo.RemindAt!.Value.AddHours(8);
                await emailService.SendMemoReminderAsync(
                    item.Email!, item.Username, memo.Content, remindLocal, memo.Section, ct);

                memo.EmailReminderSent = true;
                memo.RemindedAt ??= DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送备忘提醒邮件失败：MemoId={MemoId}, To={Email}", memo.Id, item.Email);
                // 未超时则下个周期继续重试
            }
        }
    }
}
