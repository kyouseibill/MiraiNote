using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;

namespace MiraiNote.Core.Services;

/// <summary>
/// 后台备忘提醒服务：每分钟扫描一次到期且需邮件提醒、未发送过的备忘，
/// 调用 IEmailService 发邮件，并写回 EmailReminderSent / RemindedAt。
/// 用户在数据库里保存了 Bark key 时，另发一条手机通知。Bark 失败不影响邮件，也不再重试。
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

    internal async Task ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiraiNoteDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var bark = scope.ServiceProvider.GetRequiredService<IBarkNotifier>();
        var appOptions = scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>().Value;

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
                (m, u) => new { Memo = m, u.Email, u.Username, u.IsActive, u.BarkDeviceKey })
            .Where(x => x.IsActive && x.Email != null && x.Email != "")
            .OrderBy(x => x.Memo.RemindAt)
            .Take(50) // 单次最多处理 50 条，避免长时间占用
            .ToListAsync(ct);

        if (due.Count > 0)
        {
            _logger.LogInformation("发现 {Count} 条到期邮件提醒，开始处理", due.Count);
        }

        foreach (var item in due)
        {
            if (ct.IsCancellationRequested) break;

            var memo = item.Memo;

            // 超过 2 小时仍未成功，放弃重试以免无限循环
            if (memo.RemindAt < deadline)
            {
                _logger.LogWarning(
                    "备忘提醒已超时放弃：MemoId={MemoId}, RemindAt={RemindAt}",
                    memo.Id, memo.RemindAt);
                memo.EmailReminderSent = true;
                memo.RemindedAt ??= DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(item.BarkDeviceKey))
                    memo.BarkReminderSent = true;
                await db.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                // 转换为本地时区（Asia/Shanghai，UTC+8）用于邮件展示
                var cstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
                var remindLocal = TimeZoneInfo.ConvertTimeFromUtc(memo.RemindAt!.Value, cstZone);
                await emailService.SendMemoReminderAsync(
                    item.Email!, item.Username, memo.Content, remindLocal, memo.Section, ct);

                memo.EmailReminderSent = true;
                memo.RemindedAt ??= DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "发送备忘提醒邮件失败：MemoId={MemoId}，类型 {ExceptionType}",
                    memo.Id,
                    ex.GetType().Name);
                // 未超时则下个周期继续重试
            }

            await TryPushBarkAsync(bark, appOptions, memo, item.BarkDeviceKey, deadline, ct);
            if (db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync(ct);
        }

        await PushBarkOnlyAsync(db, bark, appOptions, now, deadline, ct);
    }

    /// <summary>
    /// 没勾邮件、或邮件已处理过的备忘：只要到点、未完成、未归档且用户填了 key，就推一次。
    /// </summary>
    private async Task PushBarkOnlyAsync(
        MiraiNoteDbContext db,
        IBarkNotifier bark,
        AppOptions appOptions,
        DateTime now,
        DateTime deadline,
        CancellationToken ct)
    {
        var barkDue = await db.Memos
            .Where(m =>
                !m.IsDone &&
                !m.IsArchived &&
                m.RemindAt != null &&
                m.RemindAt <= now &&
                !m.BarkReminderSent)
            .Join(db.Users,
                m => m.UserId,
                u => u.Id,
                (m, u) => new { Memo = m, u.IsActive, u.BarkDeviceKey })
            .Where(x => x.IsActive && x.BarkDeviceKey != null && x.BarkDeviceKey.Trim() != "")
            .OrderBy(x => x.Memo.RemindAt)
            .Take(50)
            .ToListAsync(ct);

        if (barkDue.Count == 0) return;

        _logger.LogInformation("发现 {Count} 条到期手机提醒，开始处理", barkDue.Count);

        foreach (var item in barkDue)
        {
            if (ct.IsCancellationRequested) break;

            var memo = item.Memo;
            if (memo.RemindAt < deadline)
            {
                memo.BarkReminderSent = true;
                await db.SaveChangesAsync(ct);
                continue;
            }

            await TryPushBarkAsync(bark, appOptions, memo, item.BarkDeviceKey, deadline, ct);
            if (db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// 推送失败也把 BarkReminderSent 标上，避免整次提醒失败后无限重试。不改邮件标记。
    /// </summary>
    private async Task TryPushBarkAsync(
        IBarkNotifier bark,
        AppOptions appOptions,
        Memo memo,
        string? deviceKey,
        DateTime deadline,
        CancellationToken ct)
    {
        if (memo.BarkReminderSent || string.IsNullOrWhiteSpace(deviceKey))
            return;

        if (memo.RemindAt < deadline)
        {
            memo.BarkReminderSent = true;
            return;
        }

        try
        {
            var cstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
            var remindLocal = TimeZoneInfo.ConvertTimeFromUtc(memo.RemindAt!.Value, cstZone);
            var sectionLabel = string.Equals(memo.Section, "life", StringComparison.OrdinalIgnoreCase) ? "生活" : "工作";
            await bark.PushAsync(new BarkPushRequest
            {
                DeviceKey = deviceKey.Trim(),
                Title = $"【未来ノート · {sectionLabel}提醒】{TrimTitle(memo.Content)}",
                Body = $"{memo.Content}\n提醒时间 {remindLocal:yyyy-MM-dd HH:mm} (UTC+8)",
                OpenUrl = AppLinks.MemoList(appOptions, memo.Section)
            }, ct);
            memo.RemindedAt ??= DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "发送备忘 Bark 提醒失败：MemoId={MemoId}，类型 {ExceptionType}",
                memo.Id,
                ex.GetType().Name);
        }

        memo.BarkReminderSent = true;
    }

    private static string TrimTitle(string content)
    {
        var line = content.Replace("\r", " ").Replace("\n", " ").Trim();
        return line.Length <= 30 ? line : line.Substring(0, 30) + "…";
    }
}
