using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.Household;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Tests;

public class HouseholdNotificationTests
{
    private const string BarkAddress = "https://api.day.app/device-key-test-9f3a";
    private const string ProtectionKey = "unit-test-protection-key";
    private const string SmtpPassword = "smtp-secret-should-not-log";

    [Fact]
    public void Schedule_SendsOnLeadAndDue_AndOnlyOneCatchUpAfterAGap()
    {
        var due = new DateOnly(2026, 10, 8);
        Assert.Equal(HouseholdReminderKind.Lead, HouseholdReminderSchedule.LatestKind(new DateOnly(2026, 10, 1), due, 7));
        Assert.True(HouseholdReminderSchedule.ShouldNotify(new DateOnly(2026, 10, 1), due, 7, 3, null));
        Assert.False(HouseholdReminderSchedule.ShouldNotify(new DateOnly(2026, 10, 2), due, 7, 3, new DateOnly(2026, 10, 1)));
        Assert.True(HouseholdReminderSchedule.ShouldNotify(new DateOnly(2026, 10, 8), due, 7, 3, new DateOnly(2026, 10, 1)));
        Assert.Equal(HouseholdReminderKind.Overdue, HouseholdReminderSchedule.LatestKind(new DateOnly(2026, 10, 9), due, 7));
        Assert.True(HouseholdReminderSchedule.ShouldNotify(new DateOnly(2026, 10, 9), due, 7, 3, null));
        Assert.False(HouseholdReminderSchedule.ShouldNotify(new DateOnly(2026, 10, 10), due, 7, 3, new DateOnly(2026, 10, 9)));
        Assert.True(HouseholdReminderSchedule.ShouldNotify(new DateOnly(2026, 10, 11), due, 7, 3, new DateOnly(2026, 10, 8)));
    }

    [Fact]
    public void Schedule_JoinBaseline_SkipsEarlierDays_AndSendsTheNextExactDay()
    {
        var due = new DateOnly(2026, 10, 8);
        var joined = new DateOnly(2026, 10, 2);
        Assert.False(HouseholdReminderSchedule.ShouldNotify(joined, due, 7, 3, null, joined));
        Assert.True(HouseholdReminderSchedule.ShouldNotify(due, due, 7, 3, null, joined));

        var afterDue = new DateOnly(2026, 10, 9);
        Assert.False(HouseholdReminderSchedule.ShouldNotify(afterDue, due, 7, 3, null, afterDue));
        Assert.False(HouseholdReminderSchedule.ShouldNotify(new DateOnly(2026, 10, 10), due, 7, 3, null, afterDue));
        Assert.True(HouseholdReminderSchedule.ShouldNotify(new DateOnly(2026, 10, 11), due, 7, 3, null, afterDue));

        var zone = ShanghaiClock.Resolve();
        Assert.Equal(new DateOnly(2026, 10, 2), HouseholdReminderSchedule.FirstEligibleDay(new DateTime(2026, 10, 2, 0, 30, 0, DateTimeKind.Utc), 9 * 60, zone));
        Assert.Equal(new DateOnly(2026, 10, 2), HouseholdReminderSchedule.FirstEligibleDay(new DateTime(2026, 10, 2, 1, 0, 0, DateTimeKind.Utc), 9 * 60, zone));
        Assert.Equal(new DateOnly(2026, 10, 3), HouseholdReminderSchedule.FirstEligibleDay(new DateTime(2026, 10, 2, 1, 1, 0, DateTimeKind.Utc), 9 * 60, zone));
    }

    [Fact]
    public void EmailHtml_EncodesTextAndOnlyUsesNormalizedLinks()
    {
        var html = HouseholdNotificationComposer.BuildEmailHtml(new HouseholdNotificationMessage(
            "家务提醒",
            "<script>alert(1)</script>\n库存 0，需先买",
            "javascript:alert(1)",
            "https://example.com/a?x=1&y=2"));

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("https://example.com/a?x=1&amp;y=2", html);
        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public async Task PurchaseLink_ControlCharactersAndSchemelessHosts_AreRejected()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0));
        var broken = await Assert.ThrowsAsync<BusinessException>(() => lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "滤网",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 8),
            PurchaseLink = "http:evil.com"
        }));
        Assert.Equal(400, broken.StatusCode);

        var slash = await Assert.ThrowsAsync<BusinessException>(() => lab.Consumables.CreateAsync(lab.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "棉芯",
            CurrentStock = 1,
            PurchaseLink = "http:///evil"
        }));
        Assert.Equal(400, slash.StatusCode);
    }

    [Fact]
    public async Task Settings_HideBarkAddress_AndDoNotLeakToAnotherMember()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0));
        var saved = await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress));
        Assert.True(saved.BarkConfigured);
        Assert.Equal("9f3a", saved.BarkAddressSuffix);
        Assert.Equal("tester@example.com", saved.Email);
        var json = JsonSerializer.Serialize(saved);
        Assert.DoesNotContain(BarkAddress, json);
        Assert.DoesNotContain("device-key-test", json);
        Assert.DoesNotContain(ProtectionKey, json);

        var stored = await lab.Db.HouseholdNotificationSettings.SingleAsync();
        Assert.NotEqual(BarkAddress, stored.BarkAddressProtected);
        Assert.DoesNotContain("device-key-test", stored.BarkAddressProtected);

        var otherId = await lab.AddUserAsync("linxia");
        await lab.JoinAsync(lab.OwnerId, "linxia");
        var other = await lab.Settings.GetAsync(otherId);
        Assert.Equal("linxia@example.com", other.Email);
        Assert.False(other.BarkConfigured);
        Assert.Null(other.BarkAddressSuffix);
        Assert.DoesNotContain("tester@example.com", JsonSerializer.Serialize(other));
    }

    [Fact]
    public async Task Dispatch_UsesShanghaiClock_AndDoesNotRepeatTheSameChannel()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 8, 59));
        await lab.CreateDueInSevenDaysAsync("滤网", " https://example.com/filter ");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith());

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);

        lab.Clock.UtcNow = Shanghai(2026, 10, 1, 9, 0);
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Contains("将于 2026-10-08 到期", lab.Email.Sent[0].Html);
        Assert.Contains("https://example.com/filter", lab.Email.Sent[0].Html);

        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(hour: 18));
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
    }

    [Fact]
    public async Task PushTimeChange_SendsAtTheNewTimeWhenNothingWentOutYet()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 10, 0));
        await lab.CreateDueInSevenDaysAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(hour: 18));
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);

        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(hour: 9));
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
    }

    [Fact]
    public async Task CatchUp_SendsOneLatestNotice_NotTheMissedOnes()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 9, 9, 0));
        await lab.CreateDueInSevenDaysAsync("护照");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress));

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Single(lab.Bark.Bodies);
        Assert.Contains("已逾期 1 天", lab.Bark.Bodies[0]);
        Assert.Contains("\"level\":\"timeSensitive\"", lab.Bark.Bodies[0]);
        Assert.Contains(BarkAddress, lab.Bark.Urls[0]);

        await lab.DispatchAsync();
        Assert.Single(lab.Bark.Bodies);
        Assert.DoesNotContain(lab.Logs.Messages, message => message.Contains(BarkAddress, StringComparison.Ordinal));
        Assert.DoesNotContain(lab.Logs.Messages, message => message.Contains(ProtectionKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Join_SkipsRemindersThatWereDueBeforeJoin_AndExistingMemberStillCatchesUp()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 2, 9, 0));
        await lab.CreateDueInSevenDaysAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress));
        var joinerId = await lab.AddUserAsync("joiner");
        var joiner = await lab.JoinAtAsync(lab.OwnerId, "joiner", lab.Clock.UtcNow);
        await lab.Settings.UpdateAsync(joinerId, SettingsWith(bark: BarkAddress));

        await lab.DispatchAsync();

        Assert.Single(lab.Email.Sent);
        Assert.Equal("tester@example.com", lab.Email.Sent[0].To);
        Assert.Empty(lab.Bark.Bodies);
        Assert.Equal(0, await lab.Db.HouseholdReminderLogs.CountAsync(r => r.MemberId == joiner.Id));
    }

    [Fact]
    public async Task Join_SendsWhenTheItemBecomesDueAfterTheBaseline()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 2, 9, 0));
        var joinerId = await lab.AddUserAsync("joiner");
        var joiner = await lab.JoinAtAsync(lab.OwnerId, "joiner", lab.Clock.UtcNow);
        await lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "滤网",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 8),
            AssigneeMemberId = joiner.Id
        });
        await lab.Settings.UpdateAsync(joinerId, SettingsWith(bark: BarkAddress));

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);

        lab.Clock.UtcNow = Shanghai(2026, 10, 8, 9, 0);
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Single(lab.Bark.Bodies);
        Assert.Contains("今天到期", lab.Bark.Bodies[0]);
    }

    [Fact]
    public async Task Join_DoesNotSendAnOverdueReminderImmediately_UntilTheNextInterval()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 5, 9, 0));
        var joinerId = await lab.AddUserAsync("joiner");
        var joiner = await lab.JoinAtAsync(lab.OwnerId, "joiner", lab.Clock.UtcNow);
        await lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "年检",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 1),
            AssigneeMemberId = joiner.Id
        });
        await lab.Settings.UpdateAsync(joinerId, SettingsWith(bark: BarkAddress));

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);

        lab.Clock.UtcNow = Shanghai(2026, 10, 7, 9, 0);
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Single(lab.Bark.Bodies);
        Assert.Contains("已逾期 6 天", lab.Bark.Bodies[0]);
    }

    [Fact]
    public async Task Rejoin_UsesTheNewBaseline_AndDoesNotReplayThePreviousMembership()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 2, 9, 0));
        var userId = await lab.AddUserAsync("rejoiner");
        var first = await lab.JoinAtAsync(lab.OwnerId, "rejoiner", Shanghai(2026, 9, 1, 9, 0));
        var item = await lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "滤网",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 8),
            AssigneeMemberId = first.Id
        });
        await lab.Settings.UpdateAsync(userId, SettingsWith(bark: BarkAddress));
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);

        await lab.Household.LeaveAsync(userId);
        lab.Email.Sent.Clear();
        var second = await lab.JoinAtAsync(lab.OwnerId, "rejoiner", lab.Clock.UtcNow);
        Assert.NotEqual(first.Id, second.Id);
        var stored = await lab.Db.HouseholdItems.SingleAsync(i => i.Id == item.Id);
        stored.AssigneeMemberId = second.Id;
        await lab.Db.SaveChangesAsync();
        await lab.Settings.UpdateAsync(userId, SettingsWith(bark: BarkAddress));

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);

        lab.Clock.UtcNow = Shanghai(2026, 10, 8, 9, 0);
        await lab.DispatchAsync();
        Assert.Single(lab.Bark.Bodies);
        Assert.Contains("今天到期", lab.Bark.Bodies[0]);
        Assert.Equal(1, await lab.Db.HouseholdReminderLogs.CountAsync(r =>
            r.MemberId == second.Id && r.Status == HouseholdReminderDeliveryStatus.Sent));
    }

    [Fact]
    public async Task DisabledModule_DoesNotDispatch_ButTestSendStillWorks()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0), enabled: false);
        await lab.CreateDueInSevenDaysAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith());
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);

        await lab.Settings.SendEmailTestAsync(lab.OwnerId, null);
        Assert.Single(lab.Email.Sent);
        Assert.Contains("测试通知", lab.Email.Sent[0].Html);
    }

    [Fact]
    public async Task PausedArchivedDeletedAndCompleted_AreNotSent()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var paused = await lab.CreateDueInSevenDaysAsync("暂停的");
        await lab.Items.SetPausedAsync(lab.OwnerId, paused.Id, true);
        var archived = await lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "护照",
            ItemType = HouseholdItemType.OneOffExpiry,
            ExpiryDate = new DateOnly(2026, 10, 8)
        });
        await lab.Items.CompleteAsync(lab.OwnerId, archived.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 10, 8)
        });
        var deleted = await lab.CreateDueTodayAsync("要删的");
        await lab.Items.DeleteAsync(lab.OwnerId, deleted.Id);
        var done = await lab.CreateDueTodayAsync("刚完成");
        await lab.Items.CompleteAsync(lab.OwnerId, done.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 10, 8)
        });
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress));

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);
    }

    [Fact]
    public async Task SendRechecksStatus_AndSkipsWhenTheItemWasPaused()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0));
        var item = await lab.CreateDueInSevenDaysAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith());
        lab.Dispatcher.BeforeDeliveryAsync = async _ =>
        {
            var entity = await lab.Db.HouseholdItems.FirstAsync(i => i.Id == item.Id);
            entity.IsPaused = true;
            await lab.Db.SaveChangesAsync();
        };

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
    }

    [Fact]
    public async Task Assignee_IsTheOnlyRecipient()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var otherId = await lab.AddUserAsync("linxia");
        var member = await lab.JoinAsync(lab.OwnerId, "linxia");
        var item = await lab.CreateDueTodayAsync("滤网");
        await lab.Items.UpdateAsync(lab.OwnerId, item.Id, new UpdateHouseholdItemRequest
        {
            Name = item.Name,
            ItemType = item.ItemType,
            CycleValue = item.CycleValue,
            CycleUnit = item.CycleUnit,
            LastDoneDate = item.LastDoneDate,
            AssigneeMemberId = member.Id
        });
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith());
        await lab.Settings.UpdateAsync(otherId, SettingsWith(email: "linxia@example.com", bark: BarkAddress));

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Single(lab.Bark.Bodies);
    }

    [Fact]
    public async Task DueReminder_IncludesStock_AndRestockAlertsOnceUntilReplenished()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0));
        var consumable = await lab.Consumables.CreateAsync(lab.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 0,
            RestockThreshold = 1,
            PurchaseLink = "https://example.com/cotton?a=1&b=2"
        });
        await lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "净水器",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 8),
            ConsumableId = consumable.Id
        });
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith());

        await lab.DispatchAsync();
        Assert.Equal(2, lab.Email.Sent.Count);
        Assert.Contains(lab.Email.Sent, mail => mail.Html.Contains("库存 0，需先买", StringComparison.Ordinal));
        Assert.Contains(lab.Email.Sent, mail => mail.Subject.Contains("补货", StringComparison.Ordinal));
        Assert.Contains("https://example.com/cotton?a=1&amp;b=2", string.Join('\n', lab.Email.Sent.Select(mail => mail.Html)));

        lab.Email.Sent.Clear();
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);

        await lab.Consumables.RestockAsync(lab.OwnerId, consumable.Id, new RestockHouseholdConsumableRequest { Quantity = 1 });
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Contains("补货", lab.Email.Sent[0].Subject);
    }

    [Fact]
    public async Task SmtpFailure_DoesNotBlockBark_OrLogSecrets()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0));
        lab.Email.FailMessage = "auth failed " + SmtpPassword;
        await lab.Consumables.CreateAsync(lab.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "滤网",
            CurrentStock = 0
        });
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress));

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Single(lab.Bark.Bodies);
        Assert.DoesNotContain(lab.Logs.Messages, message => message.Contains(SmtpPassword, StringComparison.Ordinal));
        Assert.DoesNotContain(lab.Logs.Messages, message => message.Contains(BarkAddress, StringComparison.Ordinal));
        Assert.DoesNotContain(lab.Logs.Messages, message => message.Contains(ProtectionKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task BarkAddress_RejectsUnsafeUrls_AndRequiresTheProtectionKey()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0));
        var rejected = await Assert.ThrowsAsync<BusinessException>(() => lab.Settings.UpdateAsync(
            lab.OwnerId, SettingsWith(bark: "http:evil.com")));
        Assert.Equal(400, rejected.StatusCode);

        await using var unlocked = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0), protectionKey: null);
        var missingKey = await Assert.ThrowsAsync<BusinessException>(() => unlocked.Settings.UpdateAsync(
            unlocked.OwnerId, SettingsWith(bark: BarkAddress)));
        Assert.Equal(400, missingKey.StatusCode);
        await unlocked.Settings.SendBarkTestAsync(unlocked.OwnerId, BarkAddress);
        Assert.Single(unlocked.Bark.Bodies);
    }

    [Fact]
    public async Task Bark_RejectsHttpPrivateHostsAndHostsOutsideTheAllowList()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0));
        var http = await Assert.ThrowsAsync<BusinessException>(() => lab.Settings.UpdateAsync(
            lab.OwnerId, SettingsWith(bark: "http://api.day.app/device-key")));
        Assert.Equal(400, http.StatusCode);
        Assert.Equal(HouseholdBarkAddresses.HttpsOnlyMessage, http.Message);

        var loopback = await Assert.ThrowsAsync<BusinessException>(() => lab.Settings.SendBarkTestAsync(
            lab.OwnerId, "https://127.0.0.1/device-key"));
        Assert.Equal(HouseholdBarkAddresses.UnusableMessage, loopback.Message);
        Assert.Empty(lab.Bark.Urls);

        var foreign = await Assert.ThrowsAsync<BusinessException>(() => lab.Settings.SendBarkTestAsync(
            lab.OwnerId, "https://bark.example.test/device-key"));
        Assert.Equal(HouseholdBarkAddresses.HostRejectedMessage, foreign.Message);

        await using var extra = await NotificationLab.CreateAsync(
            Shanghai(2026, 10, 1, 9, 0),
            barkAllowedHosts: ["bark.example.test"]);
        await extra.Settings.UpdateAsync(extra.OwnerId, SettingsWith(bark: "https://bark.example.test/device-key-test-9f3a"));
        Assert.Equal("9f3a", (await extra.Settings.GetAsync(extra.OwnerId)).BarkAddressSuffix);
    }

    [Fact]
    public async Task Email_OnlyGoesToTheAccountAddress_AndTestSendsAreLimited()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var rejected = await Assert.ThrowsAsync<BusinessException>(() => lab.Settings.UpdateAsync(
            lab.OwnerId, SettingsWith(email: "other@example.com")));
        Assert.Equal(400, rejected.StatusCode);
        Assert.Equal("收件邮箱只能是账号邮箱", rejected.Message);

        var testRejected = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Settings.SendEmailTestAsync(lab.OwnerId, "other@example.com"));
        Assert.Equal("收件邮箱只能是账号邮箱", testRejected.Message);

        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Email));
        var setting = await lab.Db.HouseholdNotificationSettings.SingleAsync();
        setting.NotificationEmail = "other@example.com";
        await lab.Db.SaveChangesAsync();
        await lab.CreateDueTodayAsync("滤网");
        await lab.DispatchAsync();
        Assert.Equal("tester@example.com", lab.Email.Sent[0].To);

        for (var i = 0; i < 3; i++)
            await lab.Settings.SendEmailTestAsync(lab.OwnerId, null);
        var limited = await Assert.ThrowsAsync<BusinessException>(() => lab.Settings.SendEmailTestAsync(lab.OwnerId, null));
        Assert.Equal(429, limited.StatusCode);
        Assert.Equal(HouseholdNotificationRateLimiter.LimitedMessage, limited.Message);

        lab.Bark.Status = HttpStatusCode.InternalServerError;
        var httpFailed = await Assert.ThrowsAsync<BusinessException>(() => lab.Settings.SendBarkTestAsync(lab.OwnerId, BarkAddress));
        Assert.Equal(HouseholdNotificationSettingsService.TestFailureMessage, httpFailed.Message);
        lab.Bark.Failure = new TaskCanceledException();
        var timedOut = await Assert.ThrowsAsync<BusinessException>(() => lab.Settings.SendBarkTestAsync(lab.OwnerId, BarkAddress));
        lab.Bark.Failure = new HttpRequestException("connection refused 127.0.0.1:8088");
        var refused = await Assert.ThrowsAsync<BusinessException>(() => lab.Settings.SendBarkTestAsync(lab.OwnerId, BarkAddress));
        Assert.Equal(HouseholdNotificationSettingsService.TestFailureMessage, timedOut.Message);
        Assert.Equal(timedOut.Message, refused.Message);
        Assert.DoesNotContain("127.0.0.1", refused.Message);

        var barkLimited = await Assert.ThrowsAsync<BusinessException>(() => lab.Settings.SendBarkTestAsync(lab.OwnerId, BarkAddress));
        Assert.Equal(429, barkLimited.StatusCode);
    }

    [Fact]
    public async Task ScheduledEmail_IsLimitedPerUser_ThenContinuesNextMinute()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Email));
        for (var i = 0; i < 4; i++)
            await lab.CreateDueTodayAsync("事项" + i);

        await lab.DispatchAsync();
        Assert.Equal(3, lab.Email.Sent.Count);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        Assert.Equal(4, lab.Email.Sent.Count);
        Assert.All(lab.Email.Sent, mail => Assert.Equal("tester@example.com", mail.To));
    }

    [Fact]
    public async Task ScheduledEmail_FailedRowsDoNotConsumeQuota_EveryItemGetsThreeAttempts()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Email));
        var itemIds = new List<int>();
        for (var i = 0; i < 5; i++)
            itemIds.Add((await lab.CreateDueTodayAsync("事项" + i)).Id);

        lab.Email.FailuresRemaining = 100;
        lab.Email.FailMessage = "auth failed " + SmtpPassword;

        await lab.DispatchAsync();
        var first = await lab.Db.HouseholdReminderLogs.AsNoTracking().ToListAsync();
        Assert.Equal(3, first.Count);
        Assert.All(first, log =>
        {
            Assert.Equal(HouseholdReminderDeliveryStatus.Failed, log.Status);
            Assert.Equal(1, log.AttemptCount);
            Assert.DoesNotContain(SmtpPassword, log.LastError);
        });
        Assert.Equal(97, lab.Email.FailuresRemaining);

        var rateLimited = RateLimitCount(lab);
        await lab.DispatchAsync();
        var stillWaiting = await lab.Db.HouseholdReminderLogs.AsNoTracking().OrderBy(r => r.HouseholdItemId).ToListAsync();
        Assert.Equal(3, stillWaiting.Count);
        Assert.All(stillWaiting, log => Assert.Equal(1, log.AttemptCount));
        Assert.Equal(97, lab.Email.FailuresRemaining);
        Assert.Equal(rateLimited + 2, RateLimitCount(lab));

        List<HouseholdReminderLog> logs = stillWaiting;
        for (var step = 0; step < 40; step++)
        {
            if (logs.Count == itemIds.Count && logs.All(log => log.AttemptCount == HouseholdReminderAttempt.MaxAttempts))
                break;
            lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
            await lab.DispatchAsync();
            logs = await lab.Db.HouseholdReminderLogs.AsNoTracking().ToListAsync();
        }

        Assert.Equal(itemIds.OrderBy(id => id), logs.Select(log => log.HouseholdItemId).OrderBy(id => id));
        Assert.All(logs, log =>
        {
            Assert.Equal(HouseholdReminderDeliveryStatus.Failed, log.Status);
            Assert.Equal(HouseholdReminderAttempt.MaxAttempts, log.AttemptCount);
            Assert.DoesNotContain(SmtpPassword, log.LastError ?? "");
        });
        Assert.Equal(100 - itemIds.Count * HouseholdReminderAttempt.MaxAttempts, lab.Email.FailuresRemaining);
        Assert.Empty(lab.Email.Sent);

        rateLimited = RateLimitCount(lab);
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        var capped = await lab.Db.HouseholdReminderLogs.AsNoTracking().ToListAsync();
        Assert.All(capped, log => Assert.Equal(HouseholdReminderAttempt.MaxAttempts, log.AttemptCount));
        Assert.Equal(100 - itemIds.Count * HouseholdReminderAttempt.MaxAttempts, lab.Email.FailuresRemaining);
        Assert.Equal(rateLimited, RateLimitCount(lab));
    }

    [Fact]
    public async Task FailedReminder_RetriesWithBackoff_ThenStopsAtThree()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Email));
        await lab.CreateDueTodayAsync("滤网");
        lab.Email.FailMessage = "auth failed " + SmtpPassword;
        lab.Email.FailuresRemaining = 3;

        await lab.DispatchAsync();
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        var first = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdReminderDeliveryStatus.Failed, first.Status);
        Assert.Equal(1, first.AttemptCount);
        Assert.DoesNotContain(SmtpPassword, first.LastError);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(5);
        await lab.DispatchAsync();
        var stopped = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(3, stopped.AttemptCount);
        Assert.Equal(HouseholdReminderDeliveryStatus.Failed, stopped.Status);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddHours(2);
        lab.Email.FailuresRemaining = 0;
        lab.Email.FailMessage = null;
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
    }

    [Fact]
    public async Task FailedReminder_SucceedsOnTheNextAttempt()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Email));
        await lab.CreateDueTodayAsync("滤网");
        lab.Email.FailuresRemaining = 1;

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Equal(HouseholdReminderDeliveryStatus.Sent, (await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync()).Status);
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
    }

    [Fact]
    public async Task SkippedReminder_CanSendAgainAfterTheItemResumes()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var item = await lab.CreateDueTodayAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Email));
        lab.Dispatcher.BeforeDeliveryAsync = async _ =>
        {
            var entity = await lab.Db.HouseholdItems.FirstAsync(i => i.Id == item.Id);
            entity.IsPaused = true;
            await lab.Db.SaveChangesAsync();
        };

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Equal(HouseholdReminderDeliveryStatus.Skipped, (await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync()).Status);

        await lab.Items.SetPausedAsync(lab.OwnerId, item.Id, false);
        lab.Dispatcher.BeforeDeliveryAsync = null;
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Equal(HouseholdReminderDeliveryStatus.Sent, (await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task ConcurrentDispatch_DoesNotSendTwice()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.CreateDueTodayAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress));
        lab.Bark.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = lab.DispatchAsync();
        await lab.Bark.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var other = lab.CreateContext();
        await lab.ForkDispatcher(other).DispatchAsync();
        lab.Bark.Gate.SetResult(true);
        await first;

        Assert.Single(lab.Bark.Bodies);
        Assert.Equal(1, await lab.Db.HouseholdReminderLogs.CountAsync(r => r.Status == HouseholdReminderDeliveryStatus.Sent));
    }

    [Fact]
    public async Task UnexpectedDatabaseError_IsNotTreatedAsAlreadySent()
    {
        var interceptor = new ThrowOnReminderInsert();
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0), interceptor: interceptor);
        await lab.CreateDueTodayAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Email));
        interceptor.Armed = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => lab.DispatchAsync());
        Assert.Empty(lab.Email.Sent);
        Assert.DoesNotContain(lab.Logs.Messages, message => message.Contains("唯一约束冲突", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RemovingAssignee_ClearsTheItem_AndNotifiesEveryone()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var otherId = await lab.AddUserAsync("linxia");
        var member = await lab.JoinAsync(lab.OwnerId, "linxia");
        var item = await lab.CreateDueTodayAsync("滤网");
        await lab.Items.UpdateAsync(lab.OwnerId, item.Id, new UpdateHouseholdItemRequest
        {
            Name = item.Name,
            ItemType = item.ItemType,
            CycleValue = item.CycleValue,
            CycleUnit = item.CycleUnit,
            LastDoneDate = item.LastDoneDate,
            AssigneeMemberId = member.Id
        });
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Email));
        await lab.Settings.UpdateAsync(otherId, SettingsWith(due: HouseholdNotificationChannel.Email));

        await lab.DispatchAsync();
        Assert.Equal(["linxia@example.com"], lab.Email.Sent.Select(mail => mail.To).ToArray());

        await lab.Household.RemoveMemberAsync(lab.OwnerId, member.Id);
        Assert.Null((await lab.Items.GetAsync(lab.OwnerId, item.Id)).AssigneeMemberId);

        lab.Email.Sent.Clear();
        await lab.DispatchAsync();
        Assert.Equal(["tester@example.com"], lab.Email.Sent.Select(mail => mail.To).ToArray());
    }

    [Fact]
    public async Task StoredPrivateBarkAddress_IsNotRequested()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.CreateDueTodayAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith());
        var protector = new HouseholdSecretProtector(Options.Create(new HouseholdOptions
        {
            Notifications = new HouseholdNotificationOptions { ProtectionKey = ProtectionKey }
        }));
        var setting = await lab.Db.HouseholdNotificationSettings.SingleAsync();
        setting.BarkAddressProtected = protector.Protect("http://127.0.0.1:8088/device-key-test-9f3a");
        setting.DueChannel = HouseholdNotificationChannel.Bark;
        await lab.Db.SaveChangesAsync();

        await lab.DispatchAsync();
        Assert.Empty(lab.Bark.Urls);
        var log = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdReminderDeliveryStatus.Failed, log.Status);
        Assert.DoesNotContain("127.0.0.1", log.LastError);
        Assert.DoesNotContain("device-key", log.LastError);
    }

    [Fact]
    public async Task RestockEmail_DoesNotSpendQuotaOnRemindedMembers_AndRetriesAfterRateLimit()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var otherId = await lab.AddUserAsync("linxia");
        await lab.JoinAsync(lab.OwnerId, "linxia");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(hour: 9, due: HouseholdNotificationChannel.Email));
        await lab.Settings.UpdateAsync(otherId, SettingsWith(hour: 10, due: HouseholdNotificationChannel.Email));
        await lab.Consumables.CreateAsync(lab.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 0,
            RestockThreshold = 1
        });
        for (var i = 0; i < 3; i++)
            await lab.CreateDueTodayAsync("已到期" + i);

        await lab.DispatchAsync();
        Assert.Equal(3, lab.Email.Sent.Count);
        Assert.All(lab.Email.Sent, mail => Assert.Equal("tester@example.com", mail.To));
        Assert.DoesNotContain(lab.Email.Sent, mail => mail.Subject.Contains("补货", StringComparison.Ordinal));
        Assert.Equal(0, await lab.Db.HouseholdConsumableReminders.CountAsync());
        Assert.False(await LowStockFlagAsync(lab));

        lab.Email.Sent.Clear();
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Contains("补货", lab.Email.Sent[0].Subject, StringComparison.Ordinal);
        Assert.Equal("tester@example.com", lab.Email.Sent[0].To);
        Assert.Equal(1, await lab.Db.HouseholdConsumableReminders.CountAsync());
        Assert.False(await LowStockFlagAsync(lab));

        lab.Email.Sent.Clear();
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Equal(1, await lab.Db.HouseholdConsumableReminders.CountAsync());

        for (var i = 0; i < 3; i++)
            await lab.CreateDueTodayAsync("仍要发" + i);
        await lab.DispatchAsync();
        Assert.Equal(3, lab.Email.Sent.Count);
        Assert.All(lab.Email.Sent, mail => Assert.Equal("tester@example.com", mail.To));
        Assert.DoesNotContain(lab.Email.Sent, mail => mail.To == "linxia@example.com");
    }

    [Fact]
    public async Task Settings_ShowsLatestDeliveryFailure_UntilALaterSuccess()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var item = await lab.CreateDueTodayAsync("滤网");
        var memberId = await lab.Db.HouseholdMembers.Where(m => m.UserId == lab.OwnerId).Select(m => m.Id).SingleAsync();
        var failedAt = new DateTime(2026, 10, 8, 1, 5, 0, DateTimeKind.Utc);
        lab.Db.HouseholdReminderLogs.Add(new HouseholdReminderLog
        {
            HouseholdItemId = item.Id,
            MemberId = memberId,
            ReminderDate = new DateOnly(2026, 10, 8),
            Channel = HouseholdNotificationChannel.Email,
            Kind = "Due",
            Status = HouseholdReminderDeliveryStatus.Failed,
            AttemptCount = 1,
            LastAttemptAt = failedAt,
            LastError = "发送失败（InvalidOperationException） " + SmtpPassword + " " + BarkAddress
        });
        lab.Db.HouseholdReminderLogs.Add(new HouseholdReminderLog
        {
            HouseholdItemId = item.Id,
            MemberId = memberId,
            ReminderDate = new DateOnly(2026, 10, 7),
            Channel = HouseholdNotificationChannel.Bark,
            Kind = "Due",
            Status = HouseholdReminderDeliveryStatus.Failed,
            AttemptCount = 1,
            LastAttemptAt = failedAt.AddMinutes(2),
            LastError = "发送失败（HttpRequestException）"
        });
        await lab.Db.SaveChangesAsync();

        var failed = await lab.Settings.GetAsync(lab.OwnerId);
        Assert.NotNull(failed.EmailFailure);
        Assert.Equal(HouseholdDeliveryFailure.SendFailed, failed.EmailFailure!.Reason);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 9, 5, 0, TimeSpan.FromHours(8)), failed.EmailFailure.FailedAt);
        Assert.NotNull(failed.BarkFailure);
        Assert.Equal(HouseholdDeliveryFailure.Unreachable, failed.BarkFailure!.Reason);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 9, 7, 0, TimeSpan.FromHours(8)), failed.BarkFailure.FailedAt);
        var json = JsonSerializer.Serialize(failed);
        Assert.DoesNotContain(SmtpPassword, json);
        Assert.DoesNotContain(BarkAddress, json);
        Assert.DoesNotContain("Exception", json);
        Assert.DoesNotContain("InvalidOperation", json);

        var emailLog = await lab.Db.HouseholdReminderLogs.SingleAsync(r => r.Channel == HouseholdNotificationChannel.Email);
        emailLog.Status = HouseholdReminderDeliveryStatus.Sent;
        emailLog.LastAttemptAt = failedAt.AddMinutes(10);
        emailLog.LastError = null;
        await lab.Db.SaveChangesAsync();

        var recovered = await lab.Settings.GetAsync(lab.OwnerId);
        Assert.Null(recovered.EmailFailure);
        Assert.NotNull(recovered.BarkFailure);

        var consumable = await lab.Consumables.CreateAsync(lab.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 0
        });
        lab.Db.HouseholdConsumableReminders.Add(new HouseholdConsumableReminder
        {
            ConsumableId = consumable.Id,
            MemberId = memberId,
            Channel = HouseholdNotificationChannel.Email,
            Status = HouseholdReminderDeliveryStatus.Failed,
            LastAttemptAt = failedAt.AddHours(2),
            LastError = "发送失败（TaskCanceledException）"
        });
        await lab.Db.SaveChangesAsync();

        var restockFailed = await lab.Settings.GetAsync(lab.OwnerId);
        Assert.NotNull(restockFailed.EmailFailure);
        Assert.Equal(HouseholdDeliveryFailure.TimedOut, restockFailed.EmailFailure!.Reason);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 11, 5, 0, TimeSpan.FromHours(8)), restockFailed.EmailFailure.FailedAt);
        Assert.DoesNotContain("TaskCanceled", JsonSerializer.Serialize(restockFailed));
    }

    [Fact]
    public async Task Settings_UnreadableBarkAddress_IsNotShownAsConfigured()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress));
        var rotated = new HouseholdSecretProtector(Options.Create(new HouseholdOptions
        {
            Notifications = new HouseholdNotificationOptions { ProtectionKey = "rotated-protection-key" }
        }));
        var setting = await lab.Db.HouseholdNotificationSettings.SingleAsync();
        setting.BarkAddressProtected = rotated.Protect(BarkAddress);
        setting.BarkAddressSuffix = "9f3a";
        await lab.Db.SaveChangesAsync();

        var dto = await lab.Settings.GetAsync(lab.OwnerId);
        Assert.False(dto.BarkConfigured);
        Assert.True(dto.BarkAddressUnreadable);
        Assert.Null(dto.BarkAddressSuffix);
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain(BarkAddress, json);
        Assert.DoesNotContain("9f3a", json);
        Assert.DoesNotContain("rotated-protection-key", json);
    }

    [Fact]
    public async Task Settings_StoresRealTimeoutAndConnectionFailures_WithoutSecrets()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.CreateDueTodayAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress, due: HouseholdNotificationChannel.Email));
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);

        lab.Email.Failure = new TimeoutException("smtp timeout " + SmtpPassword + " tester@example.com");
        await lab.DispatchAsync();
        var emailLog = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdDeliveryFailure.TimedOut, emailLog.LastError);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        lab.Email.Failure = new TaskCanceledException("canceled " + BarkAddress);
        await lab.DispatchAsync();
        emailLog = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdDeliveryFailure.TimedOut, emailLog.LastError);
        Assert.Equal(2, emailLog.AttemptCount);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(5);
        lab.Email.Failure = new SocketException((int)SocketError.ConnectionRefused);
        await lab.DispatchAsync();
        emailLog = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdDeliveryFailure.Unreachable, emailLog.LastError);
        Assert.DoesNotContain(SmtpPassword, emailLog.LastError);
        Assert.DoesNotContain("tester@example.com", emailLog.LastError);
        Assert.DoesNotContain(BarkAddress, emailLog.LastError);

        var dto = await lab.Settings.GetAsync(lab.OwnerId);
        Assert.NotNull(dto.EmailFailure);
        Assert.Equal(HouseholdDeliveryFailure.Unreachable, dto.EmailFailure!.Reason);
        AssertNoSecrets(JsonSerializer.Serialize(dto.EmailFailure));

        lab.Email.Failure = null;
        await lab.CreateDueTodayAsync("Bark 超时");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress, due: HouseholdNotificationChannel.Bark));
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        lab.Bark.WaitForTimeout = true;
        await lab.DispatchAsync();

        var barkLog = await lab.Db.HouseholdReminderLogs.AsNoTracking()
            .SingleAsync(r => r.Channel == HouseholdNotificationChannel.Bark);
        Assert.Equal(HouseholdReminderDeliveryStatus.Failed, barkLog.Status);
        Assert.Equal(HouseholdDeliveryFailure.TimedOut, barkLog.LastError);
        Assert.DoesNotContain(BarkAddress, barkLog.LastError);

        var afterBark = await lab.Settings.GetAsync(lab.OwnerId);
        Assert.NotNull(afterBark.BarkFailure);
        Assert.Equal(HouseholdDeliveryFailure.TimedOut, afterBark.BarkFailure!.Reason);
        Assert.Equal(HouseholdDeliveryFailure.Unreachable, afterBark.EmailFailure!.Reason);
        AssertNoSecrets(JsonSerializer.Serialize(afterBark.BarkFailure));
        Assert.All(lab.Logs.Messages, message =>
        {
            Assert.DoesNotContain(BarkAddress, message);
            Assert.DoesNotContain(SmtpPassword, message);
        });
    }

    [Fact]
    public async Task Settings_ClearsChannelFailure_AfterSuccessResaveClearOrDisable()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var otherId = await lab.AddUserAsync("linxia");
        await lab.JoinAsync(lab.OwnerId, "linxia");
        // 另一位成员还没到推送时间，避免新成员默认邮件把这次失败记到他们自己头上。
        await lab.Settings.UpdateAsync(otherId, SettingsWith(hour: 18));
        await lab.CreateDueTodayAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress, due: HouseholdNotificationChannel.Email));
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        lab.Email.Failure = new InvalidOperationException("smtp " + SmtpPassword);
        await lab.DispatchAsync();

        var failed = await lab.Settings.GetAsync(lab.OwnerId);
        Assert.Equal(HouseholdDeliveryFailure.SendFailed, failed.EmailFailure!.Reason);
        Assert.Null(failed.BarkFailure);
        var other = await lab.Settings.GetAsync(otherId);
        Assert.Null(other.EmailFailure);
        Assert.Null(other.BarkFailure);
        Assert.Equal("linxia@example.com", other.Email);
        Assert.DoesNotContain(BarkAddress, JsonSerializer.Serialize(other));

        lab.Email.Failure = null;
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        Assert.Null((await lab.Settings.GetAsync(lab.OwnerId)).EmailFailure);

        await lab.CreateDueTodayAsync("改走 Bark");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress, due: HouseholdNotificationChannel.Bark));
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        lab.Bark.Failure = new HttpRequestException("connection refused");
        await lab.DispatchAsync();
        var barkFailed = await lab.Settings.GetAsync(lab.OwnerId);
        Assert.Equal(HouseholdDeliveryFailure.Unreachable, barkFailed.BarkFailure!.Reason);
        Assert.Null(barkFailed.EmailFailure);

        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress, due: HouseholdNotificationChannel.Bark));
        Assert.Null((await lab.Settings.GetAsync(lab.OwnerId)).BarkFailure);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        Assert.Equal(HouseholdDeliveryFailure.Unreachable, (await lab.Settings.GetAsync(lab.OwnerId)).BarkFailure!.Reason);

        var clear = SettingsWith(due: HouseholdNotificationChannel.Bark);
        clear.ClearBarkAddress = true;
        await lab.Settings.UpdateAsync(lab.OwnerId, clear);
        Assert.Null((await lab.Settings.GetAsync(lab.OwnerId)).BarkFailure);

        await lab.CreateDueTodayAsync("第二次");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress, due: HouseholdNotificationChannel.Email));
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        lab.Email.Failure = new InvalidOperationException("smtp");
        lab.Bark.Failure = null;
        await lab.DispatchAsync();
        Assert.Equal(HouseholdDeliveryFailure.SendFailed, (await lab.Settings.GetAsync(lab.OwnerId)).EmailFailure!.Reason);

        var emailOff = SettingsWith(bark: BarkAddress, due: HouseholdNotificationChannel.Email);
        emailOff.EmailEnabled = false;
        await lab.Settings.UpdateAsync(lab.OwnerId, emailOff);
        Assert.Null((await lab.Settings.GetAsync(lab.OwnerId)).EmailFailure);

        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress, due: HouseholdNotificationChannel.Email));
        Assert.Null((await lab.Settings.GetAsync(lab.OwnerId)).EmailFailure);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        Assert.Equal(HouseholdDeliveryFailure.SendFailed, (await lab.Settings.GetAsync(lab.OwnerId)).EmailFailure!.Reason);

        lab.Bark.Failure = new HttpRequestException("connection refused");
        await lab.CreateDueTodayAsync("Bark 失败");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress, due: HouseholdNotificationChannel.Bark));
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        Assert.NotNull((await lab.Settings.GetAsync(lab.OwnerId)).BarkFailure);

        var barkOff = SettingsWith(due: HouseholdNotificationChannel.Bark);
        barkOff.BarkEnabled = false;
        await lab.Settings.UpdateAsync(lab.OwnerId, barkOff);
        var hidden = await lab.Settings.GetAsync(lab.OwnerId);
        Assert.Null(hidden.BarkFailure);
        Assert.False(hidden.BarkEnabled);
        Assert.NotNull(hidden.EmailFailure);
    }

    [Fact]
    public async Task Settings_IgnoresDeletedItemsAndConsumables()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var keep = await lab.CreateDueTodayAsync("留下");
        var drop = await lab.CreateDueTodayAsync("删掉");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Email));
        lab.Email.Failure = new InvalidOperationException("smtp " + SmtpPassword);
        await lab.DispatchAsync();
        Assert.Equal(HouseholdDeliveryFailure.SendFailed, (await lab.Settings.GetAsync(lab.OwnerId)).EmailFailure!.Reason);

        await lab.Items.DeleteAsync(lab.OwnerId, drop.Id);
        Assert.NotNull((await lab.Settings.GetAsync(lab.OwnerId)).EmailFailure);

        await lab.Items.DeleteAsync(lab.OwnerId, keep.Id);
        Assert.Null((await lab.Settings.GetAsync(lab.OwnerId)).EmailFailure);

        var consumable = await lab.Consumables.CreateAsync(lab.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 0,
            RestockThreshold = 1
        });
        await lab.DispatchAsync();
        var restock = await lab.Db.HouseholdConsumableReminders.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdReminderDeliveryStatus.Failed, restock.Status);
        Assert.Equal(HouseholdDeliveryFailure.SendFailed, restock.LastError);
        Assert.DoesNotContain(SmtpPassword, restock.LastError);
        Assert.NotNull((await lab.Settings.GetAsync(lab.OwnerId)).EmailFailure);

        await lab.Consumables.DeleteAsync(lab.OwnerId, consumable.Id);
        Assert.Null((await lab.Settings.GetAsync(lab.OwnerId)).EmailFailure);
        Assert.Equal(1, await lab.Db.HouseholdConsumableReminders.CountAsync());
    }

    [Fact]
    public async Task Due_BarkUnavailable_FallsBackToEmail_AndSecondScanSendsNothing()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.CreateDueTodayAsync("滤网");
        var saved = await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith());
        Assert.Equal(HouseholdNotificationChannel.Bark, saved.DueChannel);
        Assert.True(saved.HasDeliverableChannel);

        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Equal("tester@example.com", lab.Email.Sent[0].To);
        Assert.Contains("今天到期", lab.Email.Sent[0].Html);
        Assert.Empty(lab.Bark.Bodies);
        var log = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdNotificationChannel.Email, log.Channel);
        Assert.Equal(HouseholdReminderDeliveryStatus.Sent, log.Status);

        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);
        Assert.Equal(1, await lab.Db.HouseholdReminderLogs.CountAsync());
        Assert.Equal(HouseholdNotificationChannel.Bark, (await lab.Db.HouseholdNotificationSettings.SingleAsync()).DueChannel);
    }

    [Fact]
    public async Task LeadAndOverdue_UnavailableChannel_UsesTheOtherOneOnce()
    {
        await using var lead = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0));
        await lead.CreateDueInSevenDaysAsync("滤网");
        var leadSettings = SettingsWith();
        leadSettings.LeadChannel = HouseholdNotificationChannel.Bark;
        await lead.Settings.UpdateAsync(lead.OwnerId, leadSettings);
        await lead.DispatchAsync();
        Assert.Single(lead.Email.Sent);
        Assert.Contains("将于 2026-10-08 到期", lead.Email.Sent[0].Html);
        Assert.Empty(lead.Bark.Bodies);
        var leadLog = await lead.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdNotificationChannel.Email, leadLog.Channel);
        Assert.Equal("Lead", leadLog.Kind);

        await using var overdue = await NotificationLab.CreateAsync(Shanghai(2026, 10, 9, 9, 0));
        await overdue.CreateDueTodayAsync("滤网");
        var barkOff = SettingsWith(bark: BarkAddress);
        barkOff.BarkEnabled = false;
        await overdue.Settings.UpdateAsync(overdue.OwnerId, barkOff);
        await overdue.DispatchAsync();
        Assert.Single(overdue.Email.Sent);
        Assert.Contains("已逾期 1 天", overdue.Email.Sent[0].Html);
        Assert.Empty(overdue.Bark.Bodies);
        await overdue.DispatchAsync();
        Assert.Single(overdue.Email.Sent);
        Assert.Equal(1, await overdue.Db.HouseholdReminderLogs.CountAsync());
    }

    [Fact]
    public async Task Due_EmailOff_FallsBackToBark_WhenConfigured()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.CreateDueTodayAsync("滤网");
        var emailOff = SettingsWith(bark: BarkAddress, due: HouseholdNotificationChannel.Email);
        emailOff.EmailEnabled = false;
        var saved = await lab.Settings.UpdateAsync(lab.OwnerId, emailOff);
        Assert.True(saved.HasDeliverableChannel);

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Single(lab.Bark.Bodies);
        Assert.Contains("今天到期", lab.Bark.Bodies[0]);
        var log = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdNotificationChannel.Bark, log.Channel);
        Assert.Equal(HouseholdReminderDeliveryStatus.Sent, log.Status);

        await lab.DispatchAsync();
        Assert.Single(lab.Bark.Bodies);
        Assert.Empty(lab.Email.Sent);
    }

    [Fact]
    public async Task BarkPreferred_WithoutProtector_FallsBackToEmail()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0), protectionKey: null);
        await lab.CreateDueTodayAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Bark));
        var setting = await lab.Db.HouseholdNotificationSettings.SingleAsync();
        setting.BarkAddressProtected = "not-a-real-payload";
        setting.BarkEnabled = true;
        await lab.Db.SaveChangesAsync();

        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);
        Assert.Equal(HouseholdNotificationChannel.Email, (await lab.Db.HouseholdReminderLogs.SingleAsync()).Channel);
    }

    [Fact]
    public async Task BothChannelsUnavailable_SendsNothing_AndSettingsFlagIsFalse()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.CreateDueTodayAsync("滤网");
        var request = SettingsWith();
        request.EmailEnabled = false;
        request.BarkEnabled = false;
        var saved = await lab.Settings.UpdateAsync(lab.OwnerId, request);
        Assert.False(saved.HasDeliverableChannel);

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);
        Assert.Equal(0, await lab.Db.HouseholdReminderLogs.CountAsync());

        request.BarkEnabled = true;
        saved = await lab.Settings.UpdateAsync(lab.OwnerId, request);
        Assert.False(saved.HasDeliverableChannel);
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);
    }

    [Fact]
    public async Task DeliveryFailure_DoesNotFallBack_AndRetriesOnTheSameChannel()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        await lab.CreateDueTodayAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress));
        lab.Bark.Failure = new HttpRequestException("connection refused");

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        var log = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdNotificationChannel.Bark, log.Channel);
        Assert.Equal(HouseholdReminderDeliveryStatus.Failed, log.Status);
        Assert.Equal(1, log.AttemptCount);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddSeconds(30);
        await lab.DispatchAsync();
        log = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(1, log.AttemptCount);
        Assert.Empty(lab.Email.Sent);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddSeconds(30);
        await lab.DispatchAsync();
        log = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(2, log.AttemptCount);
        Assert.Equal(HouseholdNotificationChannel.Bark, log.Channel);
        Assert.Equal(HouseholdReminderDeliveryStatus.Failed, log.Status);
        Assert.Empty(lab.Email.Sent);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(4);
        await lab.DispatchAsync();
        log = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(2, log.AttemptCount);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        log = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(3, log.AttemptCount);
        Assert.Equal(HouseholdReminderDeliveryStatus.Failed, log.Status);
        Assert.Empty(lab.Email.Sent);
        Assert.Equal(3, lab.Bark.Urls.Count);

        lab.Clock.UtcNow = lab.Clock.UtcNow.AddHours(2);
        await lab.DispatchAsync();
        log = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(3, log.AttemptCount);
        Assert.Empty(lab.Email.Sent);
        Assert.Equal(3, lab.Bark.Urls.Count);
        Assert.Equal(0, await lab.Db.HouseholdReminderLogs.CountAsync(r => r.Channel == HouseholdNotificationChannel.Email));
    }

    [Fact]
    public async Task FallbackEmail_ThenBarkConfigured_SameDaySendsNothing_NextDayUsesBark()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var item = await lab.CreateDueTodayAsync("滤网");
        var initial = SettingsWith();
        initial.OverdueIntervalDays = 1;
        await lab.Settings.UpdateAsync(lab.OwnerId, initial);

        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);
        Assert.Equal(HouseholdNotificationChannel.Email, (await lab.Db.HouseholdReminderLogs.SingleAsync()).Channel);

        lab.Clock.UtcNow = Shanghai(2026, 10, 8, 11, 0);
        var withBark = SettingsWith(bark: BarkAddress);
        withBark.OverdueIntervalDays = 1;
        await lab.Settings.UpdateAsync(lab.OwnerId, withBark);
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);
        Assert.Equal(1, await lab.Db.HouseholdReminderLogs.CountAsync());
        Assert.Equal(0, RateLimitCount(lab));

        var probe = SettingsWith(due: HouseholdNotificationChannel.Email);
        probe.OverdueIntervalDays = 1;
        await lab.Settings.UpdateAsync(lab.OwnerId, probe);
        var probeIds = new List<int>();
        for (var i = 0; i < 3; i++)
            probeIds.Add((await lab.CreateDueTodayAsync("名额" + i)).Id);
        await lab.DispatchAsync();
        Assert.Equal(4, lab.Email.Sent.Count);
        Assert.Empty(lab.Bark.Bodies);
        Assert.Equal(0, RateLimitCount(lab));
        Assert.Equal(1, await lab.Db.HouseholdReminderLogs.CountAsync(r =>
            r.HouseholdItemId == item.Id && r.Channel == HouseholdNotificationChannel.Email));
        foreach (var probeId in probeIds)
            await lab.Items.DeleteAsync(lab.OwnerId, probeId);

        var restore = SettingsWith(bark: BarkAddress);
        restore.OverdueIntervalDays = 1;
        await lab.Settings.UpdateAsync(lab.OwnerId, restore);
        lab.Clock.UtcNow = Shanghai(2026, 10, 9, 9, 0);
        lab.Email.Sent.Clear();
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Single(lab.Bark.Bodies);
        Assert.Contains("已逾期 1 天", lab.Bark.Bodies[0]);
        var next = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync(r => r.ReminderDate == new DateOnly(2026, 10, 9));
        Assert.Equal(item.Id, next.HouseholdItemId);
        Assert.Equal(HouseholdNotificationChannel.Bark, next.Channel);
        Assert.Equal(HouseholdReminderDeliveryStatus.Sent, next.Status);
    }

    [Fact]
    public async Task BarkFinalFailure_ThenEmailResolved_SameDayDoesNotSend_NextDayUsesEmail()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var item = await lab.CreateDueTodayAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(bark: BarkAddress));
        lab.Bark.Failure = new HttpRequestException("connection refused");

        await lab.DispatchAsync();
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(1);
        await lab.DispatchAsync();
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddMinutes(5);
        await lab.DispatchAsync();
        var barkLog = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync();
        Assert.Equal(HouseholdNotificationChannel.Bark, barkLog.Channel);
        Assert.Equal(HouseholdReminderDeliveryStatus.Failed, barkLog.Status);
        Assert.Equal(3, barkLog.AttemptCount);
        Assert.Empty(lab.Email.Sent);

        var emailInstead = SettingsWith(bark: BarkAddress);
        emailInstead.BarkEnabled = false;
        await lab.Settings.UpdateAsync(lab.OwnerId, emailInstead);
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Equal(0, await lab.Db.HouseholdReminderLogs.CountAsync(r => r.Channel == HouseholdNotificationChannel.Email));
        Assert.Equal(0, RateLimitCount(lab));

        for (var i = 0; i < 3; i++)
            await lab.CreateDueTodayAsync("名额" + i);
        await lab.DispatchAsync();
        Assert.Equal(3, lab.Email.Sent.Count);
        Assert.Equal(0, RateLimitCount(lab));
        Assert.Equal(0, await lab.Db.HouseholdReminderLogs.CountAsync(r =>
            r.HouseholdItemId == item.Id && r.Channel == HouseholdNotificationChannel.Email));

        lab.Bark.Failure = null;
        lab.Email.Sent.Clear();
        var barkUrls = lab.Bark.Urls.Count;
        lab.Clock.UtcNow = Shanghai(2026, 10, 9, 9, 0);
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Contains("滤网", lab.Email.Sent[0].Html);
        Assert.Contains("已逾期 1 天", lab.Email.Sent[0].Html);
        Assert.Equal(barkUrls, lab.Bark.Urls.Count);
        var emailLog = await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync(r =>
            r.HouseholdItemId == item.Id && r.Channel == HouseholdNotificationChannel.Email);
        Assert.Equal(new DateOnly(2026, 10, 9), emailLog.ReminderDate);
        Assert.Equal(HouseholdReminderDeliveryStatus.Sent, emailLog.Status);
        Assert.Equal(HouseholdReminderDeliveryStatus.Failed, (await lab.Db.HouseholdReminderLogs.AsNoTracking().SingleAsync(r =>
            r.HouseholdItemId == item.Id && r.ReminderDate == new DateOnly(2026, 10, 8))).Status);
    }

    [Fact]
    public async Task NewMember_WithoutBark_DefaultsDueChannelToEmail_AndDoesNotRewriteSavedBark()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 8, 9, 0));
        var fresh = await lab.Settings.GetAsync(lab.OwnerId);
        Assert.Equal(HouseholdNotificationChannel.Email, fresh.DueChannel);
        Assert.Equal(HouseholdNotificationChannel.Email, fresh.LeadChannel);
        Assert.True(fresh.HasDeliverableChannel);
        Assert.False(await lab.Db.HouseholdNotificationSettings.AnyAsync());

        await lab.CreateDueTodayAsync("滤网");
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);
        Assert.Equal(HouseholdNotificationChannel.Email, (await lab.Db.HouseholdReminderLogs.SingleAsync()).Channel);

        var saved = await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(due: HouseholdNotificationChannel.Bark));
        Assert.Equal(HouseholdNotificationChannel.Bark, saved.DueChannel);
        Assert.Equal(HouseholdNotificationChannel.Bark, (await lab.Settings.GetAsync(lab.OwnerId)).DueChannel);
    }

    private static void AssertNoSecrets(string json)
    {
        Assert.DoesNotContain(SmtpPassword, json);
        Assert.DoesNotContain("tester@example.com", json);
        Assert.DoesNotContain(BarkAddress, json);
        Assert.DoesNotContain("device-key", json);
        Assert.DoesNotContain("api.day.app", json);
        Assert.DoesNotContain("Exception", json);
    }

    private static Task<bool> LowStockFlagAsync(NotificationLab lab) =>
        lab.Db.HouseholdConsumables.AsNoTracking().Select(c => c.LowStockReminderSent).SingleAsync();

    private static int RateLimitCount(NotificationLab lab) =>
        lab.Logs.Messages.Count(message => message.Contains("达到频率上限", StringComparison.Ordinal));

    private static UpdateHouseholdNotificationSettingsRequest SettingsWith(
        string? email = null,
        string? bark = null,
        int hour = 9,
        int minute = 0,
        HouseholdNotificationChannel? due = null) => new()
    {
        BarkEnabled = true,
        BarkAddress = bark,
        EmailEnabled = true,
        Email = email,
        PushHour = hour,
        PushMinute = minute,
        LeadChannel = HouseholdNotificationChannel.Email,
        DueChannel = due ?? HouseholdNotificationChannel.Bark,
        OverdueIntervalDays = 3
    };

    private static DateTimeOffset Shanghai(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));

    private sealed class NotificationLab : IAsyncDisposable
    {
        private readonly MiraiTestFixture _fx;
        public MiraiNoteDbContext Db { get; }
        public MutableTimeProvider Clock { get; }
        public HouseholdItemService Items { get; }
        public HouseholdConsumableService Consumables { get; }
        public HouseholdService Household { get; }
        public HouseholdNotificationSettingsService Settings { get; }
        public HouseholdNotificationDispatcher Dispatcher { get; }
        public RecordingEmail Email { get; }
        public RecordingBark Bark { get; }
        public ListLogger Logs { get; }
        public int OwnerId { get; }
        public Func<MiraiNoteDbContext, HouseholdNotificationDispatcher> ForkDispatcher { get; set; } = _ => throw new InvalidOperationException();

        private NotificationLab(
            MiraiTestFixture fx,
            MiraiNoteDbContext db,
            MutableTimeProvider clock,
            HouseholdItemService items,
            HouseholdConsumableService consumables,
            HouseholdService household,
            HouseholdNotificationSettingsService settings,
            HouseholdNotificationDispatcher dispatcher,
            RecordingEmail email,
            RecordingBark bark,
            ListLogger logs,
            int ownerId)
        {
            _fx = fx;
            Db = db;
            Clock = clock;
            Items = items;
            Consumables = consumables;
            Household = household;
            Settings = settings;
            Dispatcher = dispatcher;
            Email = email;
            Bark = bark;
            Logs = logs;
            OwnerId = ownerId;
        }

        public static Task<NotificationLab> CreateAsync(
            DateTimeOffset utcNow,
            bool enabled = true,
            string? protectionKey = ProtectionKey,
            string[]? barkAllowedHosts = null,
            IInterceptor? interceptor = null)
        {
            var fx = new MiraiTestFixture();
            var db = interceptor == null ? fx.CreateContext() : fx.CreateContextWithInterceptor(interceptor);
            var clock = new MutableTimeProvider(utcNow);
            var rules = new HouseholdCycleRules(new DelegatingHouseholdClock(clock));
            var access = new HouseholdAccessService(db, clock);
            var policy = HouseholdAccessPolicy.Default;
            var items = new HouseholdItemService(db, access, rules, policy);
            var consumables = new HouseholdConsumableService(db, access, policy);
            var household = new HouseholdService(db, access);
            var options = Options.Create(new HouseholdOptions
            {
                PublicBaseUrl = "https://notes.example.test",
                Notifications = new HouseholdNotificationOptions
                {
                    Enabled = enabled,
                    ProtectionKey = protectionKey,
                    BarkAllowedHosts = barkAllowedHosts ?? []
                }
            });
            var email = new RecordingEmail();
            var bark = new RecordingBark();
            var logs = new ListLogger();
            var rates = new HouseholdNotificationRateLimiter(clock);
            var barkChannel = new BarkNotificationChannel(bark, options, new ListLogger<BarkNotificationChannel>(logs));
            var emailChannel = new EmailNotificationChannel(
                email,
                Options.Create(new EmailOptions
                {
                    SmtpHost = "smtp.example.invalid",
                    SmtpUser = "notifier@example.invalid",
                    SmtpPassword = SmtpPassword
                }),
                new ListLogger<EmailNotificationChannel>(logs));
            var protector = new HouseholdSecretProtector(options);
            var settings = new HouseholdNotificationSettingsService(db, access, protector, options, barkChannel, emailChannel, rates, clock);
            HouseholdNotificationDispatcher Build(MiraiNoteDbContext context) => new(
                context,
                clock,
                options,
                new HouseholdLinkBuilder(options),
                barkChannel,
                emailChannel,
                protector,
                rates,
                new ListLogger<HouseholdNotificationDispatcher>(logs));
            var lab = new NotificationLab(
                fx, db, clock, items, consumables, household, settings, Build(db), email, bark, logs, db.Users.Single().Id)
            {
                ForkDispatcher = Build
            };
            return Task.FromResult(lab);
        }

        public MiraiNoteDbContext CreateContext() => _fx.CreateContext();

        public Task DispatchAsync() => Dispatcher.DispatchAsync();

        public async Task<HouseholdItemDto> CreateDueInSevenDaysAsync(string name, string? purchaseLink = null) =>
            await Items.CreateAsync(OwnerId, new CreateHouseholdItemRequest
            {
                Name = name,
                ItemType = HouseholdItemType.Recurring,
                CycleValue = 1,
                CycleUnit = HouseholdCycleUnit.Month,
                LastDoneDate = new DateOnly(2026, 9, 8),
                PurchaseLink = purchaseLink
            });

        public async Task<HouseholdItemDto> CreateDueTodayAsync(string name) =>
            await Items.CreateAsync(OwnerId, new CreateHouseholdItemRequest
            {
                Name = name,
                ItemType = HouseholdItemType.Recurring,
                CycleValue = 1,
                CycleUnit = HouseholdCycleUnit.Month,
                LastDoneDate = new DateOnly(2026, 9, 8)
            });

        public async Task<int> AddUserAsync(string username)
        {
            var user = new User
            {
                Username = username,
                Email = username + "@example.com",
                PasswordHash = "hash",
                IsEmailVerified = true
            };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            return user.Id;
        }

        public Task<HouseholdMemberDto> JoinAsync(int householdUserId, string username, HouseholdRole role = HouseholdRole.Member) =>
            JoinAtAsync(householdUserId, username, null, role);

        public async Task<HouseholdMemberDto> JoinAtAsync(
            int householdUserId,
            string username,
            DateTimeOffset? notifyFrom,
            HouseholdRole role = HouseholdRole.Member)
        {
            var home = await Household.GetMineAsync(householdUserId);
            var user = await Db.Users.SingleAsync(u => u.Username == username);
            var member = new HouseholdMember
            {
                HouseholdId = home.Id,
                UserId = user.Id,
                Role = role,
                NotifyFromUtc = notifyFrom.HasValue
                    ? DateTime.SpecifyKind(notifyFrom.Value.UtcDateTime, DateTimeKind.Utc)
                    : default
            };
            Db.HouseholdMembers.Add(member);
            await Db.SaveChangesAsync();
            return new HouseholdMemberDto
            {
                Id = member.Id,
                UserId = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = role
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _fx.Dispose();
        }
    }

    private sealed class MutableTimeProvider : TimeProvider, IHouseholdClock
    {
        public MutableTimeProvider(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingEmail : IEmailService
    {
        public List<(string To, string Subject, string Html)> Sent { get; } = new();
        public string? FailMessage { get; set; }
        public int FailuresRemaining { get; set; }
        public Exception? Failure { get; set; }

        public Task SendCustomEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (Failure != null)
                throw Failure;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException(FailMessage ?? "smtp down");
            }
            if (FailMessage != null)
                throw new InvalidOperationException(FailMessage);
            Sent.Add((toEmail, subject, htmlBody));
            return Task.CompletedTask;
        }

        public Task SendVerifyEmailAsync(string toEmail, string username, string verifyLink, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAccountCreatedAsync(string toEmail, string username, string initialPassword, string loginLink, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendResetPasswordAsync(string toEmail, string username, string resetLink, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendPasswordChangedAsync(string toEmail, string username, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendMemoReminderAsync(string toEmail, string username, string content, DateTime remindAtLocal, string section, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendScheduledTaskResultAsync(string toEmail, string username, string description, string result, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingBark : IHttpClientFactory
    {
        public List<string> Urls { get; } = new();
        public List<string> Bodies { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Exception? Failure { get; set; }
        public bool WaitForTimeout
        {
            get => _waitForTimeout;
            set
            {
                _waitForTimeout = value;
                if (value)
                    _client.Timeout = TimeSpan.FromMilliseconds(300);
            }
        }
        public TaskCompletionSource<bool>? Gate { get; set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HttpClient _client;
        private readonly object _gate = new();
        private bool _waitForTimeout;

        public RecordingBark()
        {
            _client = new HttpClient(new Handler(this));
        }

        public HttpClient CreateClient(string name) => _client;

        private sealed class Handler : HttpMessageHandler
        {
            private readonly RecordingBark _owner;
            public Handler(RecordingBark owner) => _owner = owner;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = request.RequestUri?.ToString() ?? "";
                var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
                lock (_owner._gate)
                {
                    _owner.Urls.Add(url);
                    _owner.Bodies.Add(body);
                }
                _owner.Started.TrySetResult(true);
                if (_owner.WaitForTimeout)
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                if (_owner.Gate != null)
                    await _owner.Gate.Task.WaitAsync(cancellationToken);
                if (_owner.Failure != null)
                    throw _owner.Failure;
                return new HttpResponseMessage(_owner.Status)
                {
                    Content = new StringContent("{\"code\":200}")
                };
            }
        }
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception != null)
                Messages.Add(exception.ToString());
        }
    }

    private sealed class ThrowOnReminderInsert : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<HouseholdReminderLog>().Any(entry => entry.State == EntityState.Added))
                throw new DbUpdateException("not a unique conflict", new InvalidOperationException("fk"));
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<InterceptionResult<int>>(SavingChanges(eventData, result));
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly ListLogger _inner;
        public ListLogger(ListLogger inner) => _inner = inner;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
