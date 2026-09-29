using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

public interface IHouseholdNotificationDispatcher
{
    Task DispatchAsync(CancellationToken ct = default);
}

/// <summary>
/// 按上海时间和家务时钟决定要不要发。关闭总开关时直接返回。
/// 同一事项、成员、日期、通道只写一条日志；写入冲突视为已发。
/// </summary>
public sealed class HouseholdNotificationDispatcher : IHouseholdNotificationDispatcher
{
    private readonly MiraiNoteDbContext _db;
    private readonly IHouseholdClock _clock;
    private readonly HouseholdOptions _options;
    private readonly HouseholdLinkBuilder _links;
    private readonly BarkNotificationChannel _bark;
    private readonly EmailNotificationChannel _email;
    private readonly IHouseholdSecretProtector _protector;
    private readonly ILogger<HouseholdNotificationDispatcher> _logger;
    private readonly TimeZoneInfo _shanghai;

    /// <summary>测试用来在真正发出前改事项状态，验证发送前的再检查。</summary>
    internal Func<CancellationToken, Task>? BeforeDeliveryAsync { get; set; }

    public HouseholdNotificationDispatcher(
        MiraiNoteDbContext db,
        IHouseholdClock clock,
        IOptions<HouseholdOptions> options,
        HouseholdLinkBuilder links,
        BarkNotificationChannel bark,
        EmailNotificationChannel email,
        IHouseholdSecretProtector protector,
        ILogger<HouseholdNotificationDispatcher> logger)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
        _links = links;
        _bark = bark;
        _email = email;
        _protector = protector;
        _logger = logger;
        _shanghai = ShanghaiClock.Resolve();
    }

    public async Task DispatchAsync(CancellationToken ct = default)
    {
        if (!_options.Notifications.Enabled)
            return;

        var shanghai = TimeZoneInfo.ConvertTime(_clock.UtcNow, _shanghai);
        var today = DateOnly.FromDateTime(shanghai.DateTime);
        var minuteOfDay = shanghai.Hour * 60 + shanghai.Minute;

        var householdIds = await _db.Households.AsNoTracking()
            .Select(h => h.Id)
            .ToListAsync(ct);
        foreach (var householdId in householdIds)
            await DispatchHouseholdAsync(householdId, today, minuteOfDay, ct);
    }

    private async Task DispatchHouseholdAsync(int householdId, DateOnly today, int minuteOfDay, CancellationToken ct)
    {
        var members = await _db.HouseholdMembers.AsNoTracking()
            .Where(m => m.HouseholdId == householdId)
            .Select(m => new MemberRow(m.Id, m.UserId))
            .ToListAsync(ct);
        if (members.Count == 0)
            return;

        var memberIds = members.Select(m => m.Id).ToArray();
        var settings = await _db.HouseholdNotificationSettings.AsNoTracking()
            .Where(s => memberIds.Contains(s.MemberId))
            .ToListAsync(ct);
        var settingByMember = settings.ToDictionary(s => s.MemberId);
        var usernames = await LoadUsernamesAsync(members, ct);

        var items = await _db.HouseholdItems.AsNoTracking()
            .Where(i => i.HouseholdId == householdId && !i.IsPaused && !i.IsArchived && i.NextDueDate != null)
            .Select(i => new ItemRow(
                i.Id,
                i.Name,
                i.NextDueDate!.Value,
                i.LeadDays,
                i.AssigneeMemberId,
                i.PurchaseLink,
                i.ConsumableId))
            .ToListAsync(ct);

        var consumableIds = items.Where(i => i.ConsumableId != null).Select(i => i.ConsumableId!.Value).Distinct().ToArray();
        var consumables = consumableIds.Length == 0
            ? new Dictionary<int, ConsumableRow>()
            : await _db.HouseholdConsumables.AsNoTracking()
                .Where(c => consumableIds.Contains(c.Id))
                .Select(c => new ConsumableRow(c.Id, c.Name, c.CurrentStock, c.RestockThreshold, c.PurchaseLink))
                .ToDictionaryAsync(c => c.Id, ct);

        foreach (var item in items)
        {
            consumables.TryGetValue(item.ConsumableId ?? 0, out var consumable);
            var recipients = item.AssigneeMemberId is int assignee
                ? members.Where(m => m.Id == assignee).ToList()
                : members;
            foreach (var member in recipients)
            {
                var preference = PreferenceOf(settingByMember, member.Id);
                if (minuteOfDay < preference.PushMinuteOfDay)
                    continue;
                await TrySendItemAsync(item, consumable, member, preference, today, usernames, ct);
            }
        }

        await DispatchRestocksAsync(householdId, members, settingByMember, minuteOfDay, ct);
    }

    private async Task TrySendItemAsync(
        ItemRow item,
        ConsumableRow? consumable,
        MemberRow member,
        NotificationPreference preference,
        DateOnly today,
        IReadOnlyDictionary<int, string> usernames,
        CancellationToken ct)
    {
        var lastSent = await _db.HouseholdReminderLogs.AsNoTracking()
            .Where(r => r.HouseholdItemId == item.Id && r.MemberId == member.Id)
            .MaxAsync(r => (DateOnly?)r.ReminderDate, ct);
        if (!HouseholdReminderSchedule.ShouldNotify(today, item.Due, item.LeadDays, preference.OverdueIntervalDays, lastSent))
            return;

        var kind = HouseholdReminderSchedule.LatestKind(today, item.Due, item.LeadDays);
        var channel = kind == HouseholdReminderKind.Lead ? preference.LeadChannel : preference.DueChannel;
        if (!preference.CanDeliver(channel, _protector.IsConfigured))
            return;

        var reserved = await ReserveItemLogAsync(item, member.Id, today, channel, kind, preference.OverdueIntervalDays, ct);
        if (!reserved)
            return;

        if (BeforeDeliveryAsync != null)
            await BeforeDeliveryAsync(ct);

        var fresh = await _db.HouseholdItems.AsNoTracking()
            .Where(i => i.Id == item.Id && !i.IsPaused && !i.IsArchived && i.NextDueDate != null)
            .Select(i => new { i.NextDueDate, i.LeadDays, i.Name, i.PurchaseLink, i.ConsumableId })
            .FirstOrDefaultAsync(ct);
        if (fresh?.NextDueDate is not DateOnly due)
            return;
        if (!HouseholdReminderSchedule.ShouldNotify(today, due, fresh.LeadDays, preference.OverdueIntervalDays, lastSent))
            return;

        var freshKind = HouseholdReminderSchedule.LatestKind(today, due, fresh.LeadDays);
        ConsumableRow? stock = consumable;
        if (fresh.ConsumableId != item.ConsumableId)
        {
            stock = fresh.ConsumableId is int consumableId
                ? await _db.HouseholdConsumables.AsNoTracking()
                    .Where(c => c.Id == consumableId)
                    .Select(c => new ConsumableRow(c.Id, c.Name, c.CurrentStock, c.RestockThreshold, c.PurchaseLink))
                    .FirstOrDefaultAsync(ct)
                : null;
        }
        else if (fresh.ConsumableId is int sameId)
        {
            stock = await _db.HouseholdConsumables.AsNoTracking()
                .Where(c => c.Id == sameId)
                .Select(c => new ConsumableRow(c.Id, c.Name, c.CurrentStock, c.RestockThreshold, c.PurchaseLink))
                .FirstOrDefaultAsync(ct);
        }

        var message = HouseholdNotificationComposer.Item(
            fresh.Name,
            freshKind,
            today,
            due,
            stock?.Stock,
            stock?.Threshold,
            fresh.PurchaseLink,
            stock?.PurchaseLink,
            _links.ItemPage(item.Id));
        usernames.TryGetValue(member.UserId, out var username);
        if (!string.IsNullOrWhiteSpace(username))
            message = message with { Text = $"你好，{username}。\n" + message.Text };

        await DeliverAsync(preference, channel, message, member.Id, item.Id, ct);
    }

    private async Task<bool> ReserveItemLogAsync(
        ItemRow item,
        int memberId,
        DateOnly today,
        HouseholdNotificationChannel channel,
        HouseholdReminderKind kind,
        int overdueInterval,
        CancellationToken ct)
    {
        var log = new HouseholdReminderLog
        {
            HouseholdItemId = item.Id,
            MemberId = memberId,
            ReminderDate = today,
            Channel = channel,
            Kind = kind.ToString(),
            IsCatchUp = !HouseholdReminderSchedule.IsExactScheduleDay(today, item.Due, item.LeadDays, overdueInterval)
        };
        _db.HouseholdReminderLogs.Add(log);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.Entry(log).State = EntityState.Detached;
            return false;
        }
    }

    private async Task DispatchRestocksAsync(
        int householdId,
        List<MemberRow> members,
        Dictionary<int, HouseholdNotificationSetting> settingByMember,
        int minuteOfDay,
        CancellationToken ct)
    {
        var lows = await _db.HouseholdConsumables
            .Where(c => c.HouseholdId == householdId && !c.LowStockReminderSent && c.CurrentStock <= c.RestockThreshold)
            .ToListAsync(ct);

        foreach (var consumable in lows)
        {
            var waitingForPushTime = false;
            foreach (var member in members)
            {
                var preference = PreferenceOf(settingByMember, member.Id);
                var channels = preference.DeliverableChannels(_protector.IsConfigured).ToList();
                if (channels.Count == 0)
                    continue;
                if (minuteOfDay < preference.PushMinuteOfDay)
                {
                    waitingForPushTime = true;
                    continue;
                }

                var message = HouseholdNotificationComposer.Restock(
                    consumable.Name,
                    consumable.CurrentStock,
                    consumable.RestockThreshold,
                    consumable.PurchaseLink);
                foreach (var channel in channels)
                    await TrySendRestockAsync(consumable.Id, member.Id, preference, channel, message, ct);
            }

            if (!waitingForPushTime)
                consumable.LowStockReminderSent = true;
        }

        if (lows.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    private async Task TrySendRestockAsync(
        int consumableId,
        int memberId,
        NotificationPreference preference,
        HouseholdNotificationChannel channel,
        HouseholdNotificationMessage message,
        CancellationToken ct)
    {
        var log = new HouseholdConsumableReminder
        {
            ConsumableId = consumableId,
            MemberId = memberId,
            Channel = channel
        };
        _db.HouseholdConsumableReminders.Add(log);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.Entry(log).State = EntityState.Detached;
            return;
        }

        await DeliverAsync(preference, channel, message, memberId, itemId: null, ct);
    }

    private async Task DeliverAsync(
        NotificationPreference preference,
        HouseholdNotificationChannel channel,
        HouseholdNotificationMessage message,
        int memberId,
        int? itemId,
        CancellationToken ct)
    {
        try
        {
            if (channel == HouseholdNotificationChannel.Bark)
            {
                var address = _protector.Unprotect(preference.BarkAddressProtected!);
                await _bark.SendAsync(address, message, ct);
            }
            else
            {
                await _email.SendAsync(preference.Email!, message, ct);
            }

            _logger.LogInformation(
                "家务通知已发送，成员 {MemberId}，事项 {ItemId}，通道 {Channel}",
                memberId,
                itemId,
                channel.ToString());
        }
        catch (Exception ex) when (ex is HouseholdNotificationDeliveryException or BusinessException)
        {
            _logger.LogError(
                "家务通知发送失败，成员 {MemberId}，事项 {ItemId}，通道 {Channel}，类型 {ExceptionType}",
                memberId,
                itemId,
                channel.ToString(),
                ex.GetType().Name);
        }
    }

    private async Task<Dictionary<int, string>> LoadUsernamesAsync(List<MemberRow> members, CancellationToken ct)
    {
        var userIds = members.Select(m => m.UserId).ToArray();
        return await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
    }

    private static NotificationPreference PreferenceOf(Dictionary<int, HouseholdNotificationSetting> settings, int memberId)
    {
        if (!settings.TryGetValue(memberId, out var setting))
            setting = new HouseholdNotificationSetting();
        return NotificationPreference.From(setting);
    }

    private readonly record struct MemberRow(int Id, int UserId);

    private readonly record struct ItemRow(
        int Id,
        string Name,
        DateOnly Due,
        int LeadDays,
        int? AssigneeMemberId,
        string? PurchaseLink,
        int? ConsumableId);

    private readonly record struct ConsumableRow(int Id, string Name, int Stock, int Threshold, string? PurchaseLink);

    private sealed record NotificationPreference(
        bool BarkEnabled,
        string? BarkAddressProtected,
        bool EmailEnabled,
        string? Email,
        int PushMinuteOfDay,
        HouseholdNotificationChannel LeadChannel,
        HouseholdNotificationChannel DueChannel,
        int OverdueIntervalDays)
    {
        public static NotificationPreference From(HouseholdNotificationSetting setting) => new(
            setting.BarkEnabled,
            setting.BarkAddressProtected,
            setting.EmailEnabled,
            setting.NotificationEmail,
            setting.PushHour * 60 + setting.PushMinute,
            setting.LeadChannel,
            setting.DueChannel,
            setting.OverdueIntervalDays);

        public bool CanDeliver(HouseholdNotificationChannel channel, bool protectorConfigured)
        {
            if (channel == HouseholdNotificationChannel.Bark)
                return BarkEnabled && protectorConfigured && !string.IsNullOrEmpty(BarkAddressProtected);
            return EmailEnabled && !string.IsNullOrWhiteSpace(Email);
        }

        public IEnumerable<HouseholdNotificationChannel> DeliverableChannels(bool protectorConfigured)
        {
            if (CanDeliver(HouseholdNotificationChannel.Email, protectorConfigured))
                yield return HouseholdNotificationChannel.Email;
            if (CanDeliver(HouseholdNotificationChannel.Bark, protectorConfigured))
                yield return HouseholdNotificationChannel.Bark;
        }
    }
}
