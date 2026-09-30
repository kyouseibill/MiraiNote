using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

public interface IHouseholdNotificationSettingsService
{
    Task<HouseholdNotificationSettingsDto> GetAsync(int userId, CancellationToken ct = default);
    Task<HouseholdNotificationSettingsDto> UpdateAsync(int userId, UpdateHouseholdNotificationSettingsRequest request, CancellationToken ct = default);
    Task SendBarkTestAsync(int userId, string? barkAddress, CancellationToken ct = default);
    Task SendEmailTestAsync(int userId, string? email, CancellationToken ct = default);
}

public sealed class HouseholdNotificationSettingsService : IHouseholdNotificationSettingsService
{
    public const int BarkAddressMaxLength = 500;
    public const int EmailMaxLength = 200;

    /// <summary>测试发送失败时对调用方只给这一句，避免用耗时或文案区分超时和连接拒绝。</summary>
    public const string TestFailureMessage = "测试通知发送失败";

    private readonly MiraiNoteDbContext _db;
    private readonly IHouseholdAccessService _access;
    private readonly IHouseholdSecretProtector _protector;
    private readonly HouseholdOptions _options;
    private readonly BarkNotificationChannel _bark;
    private readonly EmailNotificationChannel _email;
    private readonly HouseholdNotificationRateLimiter _rates;
    private readonly IHouseholdClock _clock;

    public HouseholdNotificationSettingsService(
        MiraiNoteDbContext db,
        IHouseholdAccessService access,
        IHouseholdSecretProtector protector,
        IOptions<HouseholdOptions> options,
        BarkNotificationChannel bark,
        EmailNotificationChannel email,
        HouseholdNotificationRateLimiter rates,
        IHouseholdClock clock)
    {
        _db = db;
        _access = access;
        _protector = protector;
        _options = options.Value;
        _bark = bark;
        _email = email;
        _rates = rates;
        _clock = clock;
    }

    public async Task<HouseholdNotificationSettingsDto> GetAsync(int userId, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var setting = await _db.HouseholdNotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.MemberId == ctx.Member.Id, ct);
        return await WithDeliveriesAsync(ToDto(setting, await AccountEmailAsync(userId, ct)), setting, ctx.Member.Id, ct);
    }

    public async Task<HouseholdNotificationSettingsDto> UpdateAsync(
        int userId, UpdateHouseholdNotificationSettingsRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var setting = await _db.HouseholdNotificationSettings
            .FirstOrDefaultAsync(s => s.MemberId == ctx.Member.Id, ct);
        var added = false;
        if (setting == null)
        {
            setting = new HouseholdNotificationSetting { MemberId = ctx.Member.Id };
            _db.HouseholdNotificationSettings.Add(setting);
            added = true;
        }

        var accountEmail = await AccountEmailAsync(userId, ct);
        try
        {
            Apply(setting, request, accountEmail);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            if (added)
                _db.Entry(setting).State = EntityState.Detached;
            else
                await _db.Entry(setting).ReloadAsync(ct);
            throw;
        }

        return await WithDeliveriesAsync(ToDto(setting, accountEmail), setting, ctx.Member.Id, ct);
    }

    public async Task SendBarkTestAsync(int userId, string? barkAddress, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        string address;
        if (!string.IsNullOrWhiteSpace(barkAddress))
        {
            address = HouseholdBarkAddresses.Require(barkAddress, _options.Notifications, BarkAddressMaxLength);
        }
        else
        {
            var setting = await _db.HouseholdNotificationSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.MemberId == ctx.Member.Id, ct);
            if (string.IsNullOrEmpty(setting?.BarkAddressProtected))
                throw new BusinessException("请先填写 Bark 地址", 400);
            address = _protector.Unprotect(setting.BarkAddressProtected);
            address = HouseholdBarkAddresses.Require(address, _options.Notifications, BarkAddressMaxLength);
        }

        _rates.EnsureAllowed(HouseholdNotificationRateLimiter.TestBark, userId);
        await DeliverTestAsync(() => _bark.SendAsync(address, HouseholdNotificationComposer.Test(), ct));
    }

    public async Task SendEmailTestAsync(int userId, string? email, CancellationToken ct = default)
    {
        var accountEmail = await AccountEmailAsync(userId, ct);
        EnsureAccountEmail(email, accountEmail);
        _rates.EnsureAllowed(HouseholdNotificationRateLimiter.TestEmail, userId);
        await DeliverTestAsync(() => _email.SendAsync(accountEmail, HouseholdNotificationComposer.Test(), ct));
    }

    private void Apply(HouseholdNotificationSetting setting, UpdateHouseholdNotificationSettingsRequest request, string accountEmail)
    {
        if (request.PushHour is < 0 or > 23 || request.PushMinute is < 0 or > 59)
            throw new BusinessException("推送时间不正确", 400);
        if (request.OverdueIntervalDays is < 1 or > 365)
            throw new BusinessException("逾期重复间隔需在 1 到 365 天之间", 400);
        if (!Enum.IsDefined(request.LeadChannel) || !Enum.IsDefined(request.DueChannel))
            throw new BusinessException("通知通道不正确", 400);

        EnsureAccountEmail(request.Email, accountEmail);
        var barkWasEnabled = setting.BarkEnabled;
        var emailWasEnabled = setting.EmailEnabled;
        setting.BarkEnabled = request.BarkEnabled;
        setting.EmailEnabled = request.EmailEnabled;
        setting.PushHour = request.PushHour;
        setting.PushMinute = request.PushMinute;
        setting.LeadChannel = request.LeadChannel;
        setting.DueChannel = request.DueChannel;
        setting.OverdueIntervalDays = request.OverdueIntervalDays;
        // 第一版不保存自填邮箱。以后若允许改地址，只改 EnsureAccountEmail / 调度里的收件人。
        setting.NotificationEmail = null;

        if (request.ClearBarkAddress)
        {
            setting.BarkAddressProtected = null;
            setting.BarkAddressSuffix = null;
            AcknowledgeBark(setting);
        }
        else if (!string.IsNullOrWhiteSpace(request.BarkAddress))
        {
            var normalized = HouseholdBarkAddresses.Require(request.BarkAddress, _options.Notifications, BarkAddressMaxLength);
            setting.BarkAddressProtected = _protector.Protect(normalized);
            setting.BarkAddressSuffix = Suffix(normalized);
            AcknowledgeBark(setting);
        }

        if (barkWasEnabled && !setting.BarkEnabled)
            AcknowledgeBark(setting);
        if (emailWasEnabled && !setting.EmailEnabled)
            AcknowledgeEmail(setting);
    }

    private void AcknowledgeBark(HouseholdNotificationSetting setting) =>
        setting.BarkFailureAcknowledgedAt = UtcNow();

    private void AcknowledgeEmail(HouseholdNotificationSetting setting) =>
        setting.EmailFailureAcknowledgedAt = UtcNow();

    private DateTime UtcNow() => DateTime.SpecifyKind(_clock.UtcNow.UtcDateTime, DateTimeKind.Utc);

    private HouseholdNotificationSettingsDto ToDto(HouseholdNotificationSetting? setting, string accountEmail)
    {
        var unsaved = setting == null;
        setting ??= new HouseholdNotificationSetting();
        if (unsaved)
            setting.DueChannel = HouseholdNotificationChannel.Email;
        var readable = TryReadBark(setting.BarkAddressProtected);
        return new HouseholdNotificationSettingsDto
        {
            BarkEnabled = setting.BarkEnabled,
            BarkConfigured = readable,
            BarkAddressUnreadable = !readable && !string.IsNullOrEmpty(setting.BarkAddressProtected),
            BarkAddressSuffix = readable ? setting.BarkAddressSuffix : null,
            EmailEnabled = setting.EmailEnabled,
            Email = accountEmail,
            PushHour = setting.PushHour,
            PushMinute = setting.PushMinute,
            LeadChannel = setting.LeadChannel,
            DueChannel = setting.DueChannel,
            OverdueIntervalDays = setting.OverdueIntervalDays,
            NotificationsEnabled = _options.Notifications.Enabled,
            HasDeliverableChannel = HouseholdNotificationAvailability.HasDeliverableChannel(
                setting.BarkEnabled,
                setting.BarkAddressProtected,
                setting.EmailEnabled,
                accountEmail,
                _protector.IsConfigured)
        };
    }

    private bool TryReadBark(string? protectedPayload)
    {
        if (string.IsNullOrEmpty(protectedPayload))
            return false;
        try
        {
            _protector.Unprotect(protectedPayload);
            return true;
        }
        catch (BusinessException)
        {
            return false;
        }
    }

    private async Task<HouseholdNotificationSettingsDto> WithDeliveriesAsync(
        HouseholdNotificationSettingsDto dto,
        HouseholdNotificationSetting? setting,
        int memberId,
        CancellationToken ct)
    {
        var attempts = await LoadDeliveryAttemptsAsync(memberId, ct);
        var barkEnabled = setting?.BarkEnabled ?? true;
        var emailEnabled = setting?.EmailEnabled ?? true;
        dto.BarkFailure = barkEnabled
            ? LatestFailure(attempts, HouseholdNotificationChannel.Bark, setting?.BarkFailureAcknowledgedAt)
            : null;
        dto.EmailFailure = emailEnabled
            ? LatestFailure(attempts, HouseholdNotificationChannel.Email, setting?.EmailFailureAcknowledgedAt)
            : null;
        return dto;
    }

    private async Task<List<DeliveryAttempt>> LoadDeliveryAttemptsAsync(int memberId, CancellationToken ct)
    {
        var reminders = await (
            from log in _db.HouseholdReminderLogs.AsNoTracking()
            join item in _db.HouseholdItems.AsNoTracking() on log.HouseholdItemId equals item.Id
            where log.MemberId == memberId
                && (log.Status == HouseholdReminderDeliveryStatus.Sent || log.Status == HouseholdReminderDeliveryStatus.Failed)
            select new DeliveryAttempt(log.Channel, log.Status, log.LastAttemptAt ?? log.UpdatedAt, log.LastError, log.Id)
        ).ToListAsync(ct);
        var restocks = await (
            from log in _db.HouseholdConsumableReminders.AsNoTracking()
            join consumable in _db.HouseholdConsumables.AsNoTracking() on log.ConsumableId equals consumable.Id
            where log.MemberId == memberId
                && (log.Status == HouseholdReminderDeliveryStatus.Sent || log.Status == HouseholdReminderDeliveryStatus.Failed)
            select new DeliveryAttempt(log.Channel, log.Status, log.LastAttemptAt ?? log.UpdatedAt, log.LastError, log.Id)
        ).ToListAsync(ct);
        reminders.AddRange(restocks);
        return reminders;
    }

    private static HouseholdNotificationDeliveryFailureDto? LatestFailure(
        List<DeliveryAttempt> attempts,
        HouseholdNotificationChannel channel,
        DateTime? acknowledgedAt)
    {
        var acknowledged = acknowledgedAt.HasValue ? AsUtc(acknowledgedAt.Value) : (DateTime?)null;
        var latest = attempts
            .Where(attempt => attempt.Channel == channel && (acknowledged == null || AsUtc(attempt.At) > acknowledged.Value))
            .OrderByDescending(attempt => attempt.At)
            .ThenByDescending(attempt => attempt.Status == HouseholdReminderDeliveryStatus.Sent)
            .ThenByDescending(attempt => attempt.Id)
            .FirstOrDefault();
        if (latest == null || latest.Status != HouseholdReminderDeliveryStatus.Failed)
            return null;

        return new HouseholdNotificationDeliveryFailureDto
        {
            FailedAt = ToShanghai(latest.At),
            Reason = HouseholdDeliveryFailure.Classify(latest.Error)
        };
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTimeOffset ToShanghai(DateTime utc)
    {
        var specified = utc.Kind == DateTimeKind.Local
            ? utc.ToUniversalTime()
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(specified), ShanghaiClock.Resolve());
    }

    private sealed record DeliveryAttempt(
        HouseholdNotificationChannel Channel,
        HouseholdReminderDeliveryStatus Status,
        DateTime At,
        string? Error,
        int Id);

    private async Task<string> AccountEmailAsync(int userId, CancellationToken ct)
    {
        var email = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(email))
            throw new BusinessException("账号没有邮箱", 400);
        return email.Trim();
    }

    /// <summary>空值表示沿用账号邮箱。传入其它地址时拒绝。换邮箱应改账号本身。</summary>
    private static void EnsureAccountEmail(string? requested, string accountEmail)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return;
        if (HouseholdUrls.ContainsControlOrFormat(requested))
            throw new BusinessException("收件邮箱只能是账号邮箱", 400);
        var trimmed = requested.Trim();
        if (trimmed.Length > EmailMaxLength || !string.Equals(trimmed, accountEmail, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("收件邮箱只能是账号邮箱", 400);
    }

    private static string? Suffix(string value) => value.Length >= 4 ? value[^4..] : null;

    private static async Task DeliverTestAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (HouseholdNotificationDeliveryException)
        {
            throw new BusinessException(TestFailureMessage, 400);
        }
    }
}
