using System.Net.Mail;
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

    private readonly MiraiNoteDbContext _db;
    private readonly IHouseholdAccessService _access;
    private readonly IHouseholdSecretProtector _protector;
    private readonly HouseholdOptions _options;
    private readonly BarkNotificationChannel _bark;
    private readonly EmailNotificationChannel _email;

    public HouseholdNotificationSettingsService(
        MiraiNoteDbContext db,
        IHouseholdAccessService access,
        IHouseholdSecretProtector protector,
        IOptions<HouseholdOptions> options,
        BarkNotificationChannel bark,
        EmailNotificationChannel email)
    {
        _db = db;
        _access = access;
        _protector = protector;
        _options = options.Value;
        _bark = bark;
        _email = email;
    }

    public async Task<HouseholdNotificationSettingsDto> GetAsync(int userId, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var setting = await _db.HouseholdNotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.MemberId == ctx.Member.Id, ct);
        return ToDto(setting);
    }

    public async Task<HouseholdNotificationSettingsDto> UpdateAsync(
        int userId, UpdateHouseholdNotificationSettingsRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var setting = await _db.HouseholdNotificationSettings
            .FirstOrDefaultAsync(s => s.MemberId == ctx.Member.Id, ct);
        if (setting == null)
        {
            setting = new HouseholdNotificationSetting { MemberId = ctx.Member.Id };
            _db.HouseholdNotificationSettings.Add(setting);
        }

        Apply(setting, request);
        await _db.SaveChangesAsync(ct);
        return ToDto(setting);
    }

    public async Task SendBarkTestAsync(int userId, string? barkAddress, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        string address;
        if (!string.IsNullOrWhiteSpace(barkAddress))
        {
            address = HouseholdUrls.Require(barkAddress, BarkAddressMaxLength, "Bark 地址");
        }
        else
        {
            var setting = await _db.HouseholdNotificationSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.MemberId == ctx.Member.Id, ct);
            if (string.IsNullOrEmpty(setting?.BarkAddressProtected))
                throw new BusinessException("请先填写 Bark 地址", 400);
            address = _protector.Unprotect(setting.BarkAddressProtected);
        }

        await DeliverAsync(() => _bark.SendAsync(address, HouseholdNotificationComposer.Test(), ct));
    }

    public async Task SendEmailTestAsync(int userId, string? email, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        string target;
        if (!string.IsNullOrWhiteSpace(email))
        {
            target = NormalizeEmail(email);
        }
        else
        {
            var setting = await _db.HouseholdNotificationSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.MemberId == ctx.Member.Id, ct);
            if (string.IsNullOrWhiteSpace(setting?.NotificationEmail))
                throw new BusinessException("请先填写收件邮箱", 400);
            target = setting.NotificationEmail;
        }

        await DeliverAsync(() => _email.SendAsync(target, HouseholdNotificationComposer.Test(), ct));
    }

    private void Apply(HouseholdNotificationSetting setting, UpdateHouseholdNotificationSettingsRequest request)
    {
        if (request.PushHour is < 0 or > 23 || request.PushMinute is < 0 or > 59)
            throw new BusinessException("推送时间不正确", 400);
        if (request.OverdueIntervalDays is < 1 or > 365)
            throw new BusinessException("逾期重复间隔需在 1 到 365 天之间", 400);
        if (!Enum.IsDefined(request.LeadChannel) || !Enum.IsDefined(request.DueChannel))
            throw new BusinessException("通知通道不正确", 400);

        setting.BarkEnabled = request.BarkEnabled;
        setting.EmailEnabled = request.EmailEnabled;
        setting.PushHour = request.PushHour;
        setting.PushMinute = request.PushMinute;
        setting.LeadChannel = request.LeadChannel;
        setting.DueChannel = request.DueChannel;
        setting.OverdueIntervalDays = request.OverdueIntervalDays;
        setting.NotificationEmail = string.IsNullOrWhiteSpace(request.Email) ? null : NormalizeEmail(request.Email);

        if (request.ClearBarkAddress)
        {
            setting.BarkAddressProtected = null;
            setting.BarkAddressSuffix = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.BarkAddress))
        {
            var normalized = HouseholdUrls.Require(request.BarkAddress, BarkAddressMaxLength, "Bark 地址");
            setting.BarkAddressProtected = _protector.Protect(normalized);
            setting.BarkAddressSuffix = Suffix(normalized);
        }
    }

    private HouseholdNotificationSettingsDto ToDto(HouseholdNotificationSetting? setting)
    {
        setting ??= new HouseholdNotificationSetting();
        return new HouseholdNotificationSettingsDto
        {
            BarkEnabled = setting.BarkEnabled,
            BarkConfigured = !string.IsNullOrEmpty(setting.BarkAddressProtected),
            BarkAddressSuffix = setting.BarkAddressSuffix,
            EmailEnabled = setting.EmailEnabled,
            Email = setting.NotificationEmail,
            PushHour = setting.PushHour,
            PushMinute = setting.PushMinute,
            LeadChannel = setting.LeadChannel,
            DueChannel = setting.DueChannel,
            OverdueIntervalDays = setting.OverdueIntervalDays,
            NotificationsEnabled = _options.Notifications.Enabled
        };
    }

    private static string NormalizeEmail(string value)
    {
        if (HouseholdUrls.ContainsControlOrFormat(value))
            throw new BusinessException("收件邮箱格式不正确", 400);
        var trimmed = value.Trim();
        if (trimmed.Length > EmailMaxLength || trimmed.Contains(' ') || trimmed.Contains('\t'))
            throw new BusinessException("收件邮箱格式不正确", 400);
        if (!MailAddress.TryCreate(trimmed, out var parsed) || !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("收件邮箱格式不正确", 400);
        return parsed.Address;
    }

    private static string? Suffix(string value) => value.Length >= 4 ? value[^4..] : null;

    private static async Task DeliverAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (HouseholdNotificationDeliveryException ex)
        {
            throw new BusinessException(ex.Message, 400);
        }
    }
}
