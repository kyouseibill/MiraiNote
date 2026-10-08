using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class MemoReminderTests : IDisposable
{
    private const string MemoText = "下午三点给妈妈打电话";
    private const string BarkKey = "unitTestBarkKey1";

    private readonly MiraiTestFixture _fx = new();

    [Fact]
    public void MemoReminderMail_BodyNamesTheMemo()
    {
        var mail = SmtpEmailService.ComposeMemoReminder(
            "tester", MemoText, new DateTime(2026, 10, 8, 15, 0, 0), "life");

        Assert.Contains(MemoText, mail.Subject, StringComparison.Ordinal);
        Assert.Contains("生活备忘提醒", mail.Html, StringComparison.Ordinal);
        Assert.Contains(MemoText, mail.Html, StringComparison.Ordinal);
        Assert.Contains("2026-10-08 15:00", mail.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DueUncheckedMemo_SendsEmailOnce_EvenWithoutHttpContext()
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddMinutes(-5);
            m.RemindMethods = 2;
        });

        var (svc, email, bark) = CreateScanner();
        await svc.ScanOnceAsync(CancellationToken.None);
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Single(email.Sent);
        Assert.Equal(MemoText, email.Sent[0].Content);
        Assert.Equal("work", email.Sent[0].Section);
        Assert.Empty(bark.Calls);
        await AssertFlags(emailSent: true, barkSent: false);
    }

    [Fact]
    public async Task EmailNotChecked_DoesNotSendEmail()
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddMinutes(-5);
            m.RemindMethods = 1;
        });

        var (svc, email, _) = CreateScanner();
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Empty(email.Sent);
        await AssertFlags(emailSent: false, barkSent: false);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DoneOrArchived_DoesNotSendEmail(bool done, bool archived)
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddMinutes(-5);
            m.RemindMethods = 2;
            m.IsDone = done;
            m.IsArchived = archived;
        });

        var (svc, email, bark) = CreateScanner();
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Empty(email.Sent);
        Assert.Empty(bark.Calls);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Bark_SkipsDoneArchivedOrFuture(bool done, bool archived, bool future)
    {
        await SeedMemo(m =>
        {
            m.RemindAt = future ? DateTime.UtcNow.AddHours(2) : DateTime.UtcNow.AddMinutes(-5);
            m.RemindMethods = 0;
            m.IsDone = done;
            m.IsArchived = archived;
        }, BarkKey);

        var (svc, email, bark) = CreateScanner();
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Empty(email.Sent);
        Assert.Equal(0, bark.Attempts);
    }

    [Fact]
    public async Task NotYetDue_DoesNotSendEmail()
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddHours(2);
            m.RemindMethods = 2;
        });

        var (svc, email, _) = CreateScanner();
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task OlderThanTwoHours_GivesUpEmailWithoutSending()
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddHours(-3);
            m.RemindMethods = 2;
        });

        var (svc, email, _) = CreateScanner();
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Empty(email.Sent);
        await AssertFlags(emailSent: true, barkSent: false);
    }

    [Fact]
    public async Task BarkKey_PushesOnce_ToSectionList()
    {
        await SeedMemo(m =>
        {
            m.Section = "life";
            m.RemindAt = DateTime.UtcNow.AddMinutes(-5);
            m.RemindMethods = 1;
        }, BarkKey);

        var (svc, email, bark) = CreateScanner();
        await svc.ScanOnceAsync(CancellationToken.None);
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Empty(email.Sent);
        var push = Assert.Single(bark.Calls);
        Assert.Equal(BarkKey, push.DeviceKey);
        Assert.Contains(MemoText, push.Body, StringComparison.Ordinal);
        Assert.Equal("https://notes.example.com/life/memos", push.OpenUrl);
        await AssertFlags(emailSent: false, barkSent: true);
    }

    [Fact]
    public async Task WorkMemo_BarkOpensWorkList()
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddMinutes(-5);
            m.RemindMethods = 2;
        }, BarkKey);

        var (svc, email, bark) = CreateScanner();
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Single(email.Sent);
        Assert.Equal("https://notes.example.com/work/memos", Assert.Single(bark.Calls).OpenUrl);
    }

    [Fact]
    public async Task EmptyBarkKey_DoesNotPush_EmailStillSends()
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddMinutes(-5);
            m.RemindMethods = 2;
        }, barkKey: "   ");

        var (svc, email, bark) = CreateScanner();
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Single(email.Sent);
        Assert.Equal(0, bark.Attempts);
    }

    [Fact]
    public async Task BarkFailure_StillSendsEmail_AndDoesNotRetryBark()
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddMinutes(-5);
            m.RemindMethods = 2;
        }, BarkKey);

        var logs = new List<string>();
        var (svc, email, bark) = CreateScanner(logs: logs);
        bark.Fail = true;
        await svc.ScanOnceAsync(CancellationToken.None);
        bark.Fail = false;
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Single(email.Sent);
        Assert.Equal(1, bark.Attempts);
        Assert.Empty(bark.Calls);
        await AssertFlags(emailSent: true, barkSent: true);
        Assert.DoesNotContain(logs, line => line.Contains(BarkKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmailFailure_DoesNotRetryBark()
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddMinutes(-5);
            m.RemindMethods = 2;
        }, BarkKey);

        var (svc, email, bark) = CreateScanner();
        email.Fail = true;
        await svc.ScanOnceAsync(CancellationToken.None);
        email.Fail = false;
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Equal(2, email.Attempts);
        Assert.Single(email.Sent);
        Assert.Equal(1, bark.Attempts);
        await AssertFlags(emailSent: true, barkSent: true);
    }

    [Fact]
    public async Task BarkOlderThanTwoHours_DoesNotPush()
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddHours(-3);
            m.RemindMethods = 0;
        }, BarkKey);

        var (svc, _, bark) = CreateScanner();
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Equal(0, bark.Attempts);
        await AssertFlags(emailSent: false, barkSent: true);
    }

    [Fact]
    public async Task PublicBaseUrlMissing_FallsBackToFrontendBaseUrl()
    {
        await SeedMemo(m =>
        {
            m.RemindAt = DateTime.UtcNow.AddMinutes(-5);
            m.RemindMethods = 0;
        }, BarkKey);

        var (svc, _, bark) = CreateScanner(new AppOptions
        {
            PublicBaseUrl = "  ",
            FrontendBaseUrl = "http://localhost:5173"
        });
        await svc.ScanOnceAsync(CancellationToken.None);

        Assert.Equal("http://localhost:5173/work/memos", Assert.Single(bark.Calls).OpenUrl);
    }

    [Fact]
    public async Task BarkNotifier_EmptyKey_DoesNotCallHttp()
    {
        var handler = new RecordingHandler();
        var notifier = CreateNotifier(handler, out var logs);
        await notifier.PushAsync(new BarkPushRequest
        {
            DeviceKey = "  ",
            Title = "t",
            Body = MemoText,
            OpenUrl = "https://notes.example.com/work/memos"
        });

        Assert.Empty(handler.Bodies);
        Assert.DoesNotContain(logs, line => line.Contains(BarkKey, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://evil.example/push")]
    [InlineData("http://api.day.app/push")]
    [InlineData("https://api.day.app.evil.example/push")]
    public async Task BarkNotifier_RejectsNonOfficialHost(string url)
    {
        var handler = new RecordingHandler();
        var notifier = CreateNotifier(handler, out _);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            notifier.PushAsync(new BarkPushRequest
            {
                DeviceKey = BarkKey,
                Title = "t",
                Body = MemoText,
                OpenUrl = "https://notes.example.com/work/memos"
            }, new Uri(url), CancellationToken.None));

        Assert.DoesNotContain(BarkKey, ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task BarkNotifier_PostsOnlyToApiDayApp()
    {
        var handler = new RecordingHandler();
        var notifier = CreateNotifier(handler, out var logs);
        await notifier.PushAsync(new BarkPushRequest
        {
            DeviceKey = BarkKey,
            Title = "标题",
            Body = MemoText,
            OpenUrl = "https://notes.example.com/work/memos"
        });

        Assert.Equal("https://api.day.app/push", handler.Uris.Single());
        using var doc = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal(BarkKey, doc.RootElement.GetProperty("device_key").GetString());
        Assert.Equal(MemoText, doc.RootElement.GetProperty("body").GetString());
        Assert.Equal("https://notes.example.com/work/memos", doc.RootElement.GetProperty("url").GetString());
        Assert.DoesNotContain(logs, line => line.Contains(BarkKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task BarkNotifier_HttpFailure_ThrowsWithoutKey()
    {
        var handler = new RecordingHandler
        {
            StatusCode = HttpStatusCode.InternalServerError
        };
        var notifier = CreateNotifier(handler, out var logs);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            notifier.PushAsync(new BarkPushRequest
            {
                DeviceKey = BarkKey,
                Title = "t",
                Body = MemoText,
                OpenUrl = "https://notes.example.com/work/memos"
            }));

        Assert.Equal("Bark 推送失败", ex.Message);
        Assert.DoesNotContain(BarkKey, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(logs, line => line.Contains(BarkKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task BarkKey_StoredInDatabase_NotInResponse()
    {
        var service = new MemoReminderSettingsService(_fx.CreateContext());
        var userId = await UserId();

        var saved = await service.UpdateBarkKeyAsync(userId, BarkKey);
        Assert.True(saved.BarkConfigured);
        var json = JsonSerializer.Serialize(saved);
        Assert.DoesNotContain(BarkKey, json, StringComparison.Ordinal);

        await using var db = _fx.CreateContext();
        var stored = await db.Users.Select(u => u.BarkDeviceKey).SingleAsync();
        Assert.Equal(BarkKey, stored);

        var loaded = await service.GetAsync(userId);
        Assert.True(loaded.BarkConfigured);
        Assert.DoesNotContain(BarkKey, JsonSerializer.Serialize(loaded), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlankBarkKey_ClearsStoredKey()
    {
        var service = new MemoReminderSettingsService(_fx.CreateContext());
        var userId = await UserId();
        await service.UpdateBarkKeyAsync(userId, BarkKey);

        var cleared = await service.UpdateBarkKeyAsync(userId, "  ");
        Assert.False(cleared.BarkConfigured);

        await using var db = _fx.CreateContext();
        Assert.Null(await db.Users.Select(u => u.BarkDeviceKey).SingleAsync());
    }

    [Theory]
    [InlineData("https://api.day.app/unitTestBarkKey1")]
    [InlineData("https://evil.example/unitTestBarkKey1")]
    [InlineData("api.day.app/unitTestBarkKey1")]
    public async Task BarkKey_RejectsServerAddress(string raw)
    {
        var service = new MemoReminderSettingsService(_fx.CreateContext());
        var userId = await UserId();

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateBarkKeyAsync(userId, raw));
        Assert.DoesNotContain(raw, ex.Message, StringComparison.Ordinal);

        await using var db = _fx.CreateContext();
        Assert.Null(await db.Users.Select(u => u.BarkDeviceKey).SingleAsync());
    }

    public void Dispose() => _fx.Dispose();

    private async Task<int> UserId()
    {
        await using var db = _fx.CreateContext();
        return await db.Users.Select(u => u.Id).SingleAsync();
    }

    private async Task SeedMemo(Action<Memo> configure, string? barkKey = null)
    {
        await using var db = _fx.CreateContext();
        var user = await db.Users.SingleAsync();
        user.BarkDeviceKey = string.IsNullOrWhiteSpace(barkKey) ? null : barkKey;
        var memo = new Memo
        {
            UserId = user.Id,
            Section = "work",
            Content = MemoText,
            Priority = 2
        };
        configure(memo);
        db.Memos.Add(memo);
        await db.SaveChangesAsync();
    }

    private async Task AssertFlags(bool emailSent, bool barkSent)
    {
        await using var db = _fx.CreateContext();
        var memo = await db.Memos.SingleAsync();
        Assert.Equal(emailSent, memo.EmailReminderSent);
        Assert.Equal(barkSent, memo.BarkReminderSent);
    }

    private (MemoReminderBackgroundService Service, RecordingEmail Email, RecordingBark Bark) CreateScanner(
        AppOptions? options = null,
        List<string>? logs = null)
    {
        var email = new RecordingEmail();
        var bark = new RecordingBark();
        var services = new ServiceCollection();
        services.AddScoped(_ => _fx.CreateContext());
        services.AddSingleton<IEmailService>(email);
        services.AddSingleton<IBarkNotifier>(bark);
        services.AddSingleton(Options.Create(options ?? new AppOptions
        {
            PublicBaseUrl = "https://notes.example.com",
            FrontendBaseUrl = "http://localhost:5173"
        }));
        ILogger<MemoReminderBackgroundService> logger = logs == null
            ? NullLogger<MemoReminderBackgroundService>.Instance
            : new ListLogger<MemoReminderBackgroundService>(logs);
        var service = new MemoReminderBackgroundService(services.BuildServiceProvider(), logger);
        return (service, email, bark);
    }

    private static BarkNotifier CreateNotifier(RecordingHandler handler, out List<string> logs)
    {
        logs = new List<string>();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(BarkNotifier.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return new BarkNotifier(factory.Object, new ListLogger<BarkNotifier>(logs));
    }

    private sealed class RecordingEmail : IEmailService
    {
        public List<(string Content, string Section)> Sent { get; } = new();
        public int Attempts { get; private set; }
        public bool Fail { get; set; }

        public Task SendMemoReminderAsync(string toEmail, string username, string content, DateTime remindAtLocal, string section, CancellationToken ct = default)
        {
            Attempts++;
            if (Fail) throw new InvalidOperationException("smtp down");
            Sent.Add((content, section));
            return Task.CompletedTask;
        }

        public Task SendVerifyEmailAsync(string toEmail, string username, string verifyLink, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAccountCreatedAsync(string toEmail, string username, string initialPassword, string loginLink, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendResetPasswordAsync(string toEmail, string username, string resetLink, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendPasswordChangedAsync(string toEmail, string username, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendCustomEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendScheduledTaskResultAsync(string toEmail, string username, string description, string result, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingBark : IBarkNotifier
    {
        public List<BarkPushRequest> Calls { get; } = new();
        public int Attempts { get; private set; }
        public bool Fail { get; set; }

        public Task PushAsync(BarkPushRequest request, CancellationToken ct = default)
        {
            Attempts++;
            if (Fail) throw new InvalidOperationException("bark down");
            Calls.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        public List<string> Uris { get; } = new();
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!.ToString());
            if (request.Content != null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent("{\"code\":200}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            sink.Add(formatter(state, exception) + exception);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
