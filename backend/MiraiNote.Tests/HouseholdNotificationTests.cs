using System.Net;
using System.Text.Json;
using Xunit;
using Microsoft.EntityFrameworkCore;
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
    private const string BarkAddress = "https://bark.example.test/device-key-test-9f3a";
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
        var saved = await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(
            email: "owner@example.com",
            bark: BarkAddress));
        Assert.True(saved.BarkConfigured);
        Assert.Equal("9f3a", saved.BarkAddressSuffix);
        Assert.Equal("owner@example.com", saved.Email);
        var json = JsonSerializer.Serialize(saved);
        Assert.DoesNotContain(BarkAddress, json);
        Assert.DoesNotContain("device-key-test", json);
        Assert.DoesNotContain(ProtectionKey, json);

        var stored = await lab.Db.HouseholdNotificationSettings.SingleAsync();
        Assert.NotEqual(BarkAddress, stored.BarkAddressProtected);
        Assert.DoesNotContain("device-key-test", stored.BarkAddressProtected);

        var otherId = await lab.AddUserAsync("linxia");
        await lab.Household.AddMemberAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "linxia" });
        var other = await lab.Settings.GetAsync(otherId);
        Assert.Null(other.Email);
        Assert.False(other.BarkConfigured);
        Assert.Null(other.BarkAddressSuffix);
        Assert.DoesNotContain("owner@example.com", JsonSerializer.Serialize(other));
    }

    [Fact]
    public async Task Dispatch_UsesShanghaiClock_AndDoesNotRepeatTheSameChannel()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 8, 59));
        await lab.CreateDueInSevenDaysAsync("滤网", " https://example.com/filter ");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com"));

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);

        lab.Clock.UtcNow = Shanghai(2026, 10, 1, 9, 0);
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
        Assert.Contains("将于 2026-10-08 到期", lab.Email.Sent[0].Html);
        Assert.Contains("https://example.com/filter", lab.Email.Sent[0].Html);

        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com", hour: 18));
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
    }

    [Fact]
    public async Task PushTimeChange_SendsAtTheNewTimeWhenNothingWentOutYet()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 10, 0));
        await lab.CreateDueInSevenDaysAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com", hour: 18));
        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);

        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com", hour: 9));
        await lab.DispatchAsync();
        Assert.Single(lab.Email.Sent);
    }

    [Fact]
    public async Task CatchUp_SendsOneLatestNotice_NotTheMissedOnes()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 9, 9, 0));
        await lab.CreateDueInSevenDaysAsync("护照");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com", bark: BarkAddress));

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
    public async Task DisabledModule_DoesNotDispatch_ButTestSendStillWorks()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0), enabled: false);
        await lab.CreateDueInSevenDaysAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com"));
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
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com", bark: BarkAddress));

        await lab.DispatchAsync();
        Assert.Empty(lab.Email.Sent);
        Assert.Empty(lab.Bark.Bodies);
    }

    [Fact]
    public async Task SendRechecksStatus_AndSkipsWhenTheItemWasPaused()
    {
        await using var lab = await NotificationLab.CreateAsync(Shanghai(2026, 10, 1, 9, 0));
        var item = await lab.CreateDueInSevenDaysAsync("滤网");
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com"));
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
        var member = await lab.Household.AddMemberAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "linxia" });
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
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com"));
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
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com"));

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
        await lab.Settings.UpdateAsync(lab.OwnerId, SettingsWith(email: "owner@example.com", bark: BarkAddress));

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

    private static UpdateHouseholdNotificationSettingsRequest SettingsWith(
        string? email = null,
        string? bark = null,
        int hour = 9,
        int minute = 0) => new()
    {
        BarkEnabled = true,
        BarkAddress = bark,
        EmailEnabled = true,
        Email = email,
        PushHour = hour,
        PushMinute = minute,
        LeadChannel = HouseholdNotificationChannel.Email,
        DueChannel = HouseholdNotificationChannel.Bark,
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
            string? protectionKey = ProtectionKey)
        {
            var fx = new MiraiTestFixture();
            var db = fx.CreateContext();
            var clock = new MutableTimeProvider(utcNow);
            var rules = new HouseholdCycleRules(new DelegatingHouseholdClock(clock));
            var access = new HouseholdAccessService(db);
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
                    ProtectionKey = protectionKey
                }
            });
            var email = new RecordingEmail();
            var bark = new RecordingBark();
            var logs = new ListLogger();
            var barkChannel = new BarkNotificationChannel(bark, new ListLogger<BarkNotificationChannel>(logs));
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
            var settings = new HouseholdNotificationSettingsService(db, access, protector, options, barkChannel, emailChannel);
            var dispatcher = new HouseholdNotificationDispatcher(
                db,
                clock,
                options,
                new HouseholdLinkBuilder(options),
                barkChannel,
                emailChannel,
                protector,
                new ListLogger<HouseholdNotificationDispatcher>(logs));
            return Task.FromResult(new NotificationLab(
                fx, db, clock, items, consumables, household, settings, dispatcher, email, bark, logs, db.Users.Single().Id));
        }

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

        public Task SendCustomEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
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
        private readonly HttpClient _client;

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
                _owner.Urls.Add(request.RequestUri?.ToString() ?? "");
                _owner.Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(HttpStatusCode.OK)
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
