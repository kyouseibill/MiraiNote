using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiraiNote.API.Controllers;
using MiraiNote.Core.Services;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared;
using MiraiNote.Shared.Common;
using Xunit;

namespace MiraiNote.Tests;

public class WelcomeGreetingServiceTests : IDisposable
{
    private readonly MiraiTestFixture _fx = new();

    [Fact]
    public async Task GetGreeting_WhenMemosAreDueToday_SaysHowMany()
    {
        var utcNow = new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero);
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var otherId = await AddUserAsync(db, "other", "other@example.com");

        db.Memos.AddRange(
            Memo(userId, "今天要处理", ShanghaiClock.ParseToUtc("2026-10-09 15:00")),
            Memo(userId, "已经做完", ShanghaiClock.ParseToUtc("2026-10-09 11:00"), isDone: true),
            Memo(userId, "已归档", ShanghaiClock.ParseToUtc("2026-10-09 12:00"), isArchived: true),
            Memo(otherId, "别人的", ShanghaiClock.ParseToUtc("2026-10-09 15:00")));
        await db.SaveChangesAsync();

        var greeting = await Greeting(db).GetGreetingAsync(userId, utcNow);

        Assert.Equal("tester，10月9日。今天有 1 条备忘到期。", greeting.Content);
        Assert.Null(greeting.FeatureNote);
    }

    [Fact]
    public async Task GetGreeting_WhenNothingIsDueOrUnfinished_KeepsNameAndDate()
    {
        var utcNow = new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero);
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        db.Memos.Add(Memo(userId, "已经做完", ShanghaiClock.ParseToUtc("2026-10-09 09:00"), isDone: true));
        await db.SaveChangesAsync();

        var greeting = await Greeting(db).GetGreetingAsync(userId, utcNow);

        Assert.Equal("tester，10月9日", greeting.Content);
        Assert.DoesNotContain("到期", greeting.Content);
        Assert.DoesNotContain("没做完", greeting.Content);
        Assert.Null(greeting.FeatureNote);
        Assert.Null(greeting.WeatherWarning);
        Assert.Empty(greeting.News);
    }

    [Fact]
    public async Task GetGreeting_OpenMemoWithoutTodayReminder_MentionsUnfinished()
    {
        var utcNow = new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero);
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        db.Memos.Add(Memo(userId, "还没安排时间", remindAt: null));
        await db.SaveChangesAsync();

        var greeting = await Greeting(db).GetGreetingAsync(userId, utcNow);

        Assert.Equal("tester，10月9日。还有 1 条备忘没做完。", greeting.Content);
        Assert.DoesNotContain("到期", greeting.Content);
    }

    [Fact]
    public async Task GetGreeting_ShanghaiMorningWhileUtcIsPreviousDay_CountsShanghaiDay()
    {
        // 上海 2026-10-09 00:30，UTC 仍是 2026-10-08 16:30。
        var utcNow = new DateTimeOffset(2026, 10, 8, 16, 30, 0, TimeSpan.Zero);
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var otherId = await AddUserAsync(db, "other", "other@example.com");

        db.Memos.AddRange(
            Memo(userId, "上海今天上午", ShanghaiClock.ParseToUtc("2026-10-09 09:00")),
            Memo(userId, "UTC 仍是 8 日，上海已是 9 日", new DateTime(2026, 10, 8, 18, 0, 0, DateTimeKind.Utc)),
            Memo(userId, "上海昨天晚上", ShanghaiClock.ParseToUtc("2026-10-08 23:00")),
            Memo(userId, "已完成的今天", ShanghaiClock.ParseToUtc("2026-10-09 08:00"), isDone: true),
            Memo(userId, "已归档的今天", ShanghaiClock.ParseToUtc("2026-10-09 10:00"), isArchived: true),
            Memo(otherId, "别人今天的", ShanghaiClock.ParseToUtc("2026-10-09 09:00")));
        await db.SaveChangesAsync();

        var greeting = await Greeting(db).GetGreetingAsync(userId, utcNow);

        Assert.Equal("tester，10月9日。今天有 2 条备忘到期，还有 1 条备忘没做完。", greeting.Content);
        Assert.DoesNotContain("10月8日", greeting.Content);
        Assert.Null(greeting.FeatureNote);
    }

    [Fact]
    public async Task GetGreeting_FeatureNote_DisappearsAfterSevenDays()
    {
        var notes = new[]
        {
            new FeatureLaunchNote(new DateOnly(2026, 10, 2), "MiraiAI 可以在对话里接着上次的文件继续做。"),
        };
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var service = new WelcomeGreetingService(db, new FeatureLaunchCatalog(notes));

        var before = await service.GetGreetingAsync(
            userId, new DateTimeOffset(2026, 10, 1, 15, 59, 0, TimeSpan.Zero));
        var firstDay = await service.GetGreetingAsync(
            userId, new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero));
        var lastDay = await service.GetGreetingAsync(
            userId, new DateTimeOffset(2026, 10, 8, 15, 59, 0, TimeSpan.Zero));
        var expired = await service.GetGreetingAsync(
            userId, new DateTimeOffset(2026, 10, 8, 16, 0, 0, TimeSpan.Zero));

        Assert.Null(before.FeatureNote);
        Assert.Equal("tester，10月1日", before.Content);

        Assert.Equal("MiraiAI 可以在对话里接着上次的文件继续做。", firstDay.FeatureNote);
        Assert.Equal("tester，10月2日", firstDay.Content);
        Assert.DoesNotContain("MiraiAI", firstDay.Content);

        Assert.Equal(notes[0].Text, lastDay.FeatureNote);
        Assert.Equal("tester，10月8日", lastDay.Content);

        Assert.Null(expired.FeatureNote);
        Assert.Equal("tester，10月9日", expired.Content);
        Assert.DoesNotContain("MiraiAI", expired.Content);
    }

    [Fact]
    public void FeatureNote_UsesNewestStartDateInsideSevenDayWindow()
    {
        var notes = new[]
        {
            new FeatureLaunchNote(new DateOnly(2026, 10, 1), "旧功能"),
            new FeatureLaunchNote(new DateOnly(2026, 10, 3), "新功能"),
        };

        Assert.Equal("新功能", FeatureLaunchNotes.Select(notes, new DateOnly(2026, 10, 4)));
        Assert.Equal("新功能", FeatureLaunchNotes.Select(notes, new DateOnly(2026, 10, 8)));
        Assert.Null(FeatureLaunchNotes.Select(notes, new DateOnly(2026, 10, 10)));
        Assert.Null(FeatureLaunchNotes.Select([], new DateOnly(2026, 10, 4)));
    }

    [Fact]
    public async Task GetGreeting_IgnoresClientDate_AndUsesShanghaiClock()
    {
        var utcNow = new DateTimeOffset(2026, 10, 8, 16, 30, 0, TimeSpan.Zero);
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        db.Memos.Add(Memo(userId, "上海今天", ShanghaiClock.ParseToUtc("2026-10-09 09:00")));
        await db.SaveChangesAsync();

        var controller = new WelcomeController(
            Greeting(db),
            new FixedUser(userId),
            new FixedClock(utcNow));

        var first = await controller.GetGreeting("2001-01-01", CancellationToken.None);
        var second = await controller.GetGreeting("2099-06-06", CancellationToken.None);

        var body1 = Assert.IsType<ApiResponse<WelcomeGreetingResponse>>(Assert.IsType<OkObjectResult>(first.Result).Value);
        var body2 = Assert.IsType<ApiResponse<WelcomeGreetingResponse>>(Assert.IsType<OkObjectResult>(second.Result).Value);
        Assert.Equal("tester，10月9日。今天有 1 条备忘到期。", body1.Data!.Content);
        Assert.Equal(body1.Data.Content, body2.Data!.Content);
        Assert.DoesNotContain("2001", body1.Data!.Content);
        Assert.DoesNotContain("2099", body2.Data!.Content);
        Assert.Null(body1.Data.FeatureNote);
        Assert.Null(body1.Data.WeatherWarning);
        Assert.Empty(body1.Data.News);
        Assert.Equal(
            "10月9日",
            ShanghaiClock.Today(utcNow).ToString("M月d日", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task WelcomeGreetingService_DiUsesTheReleaseCatalog()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MiraiNoteDbContext>(options => options.UseSqlite(_fx.ConnectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISevereWeatherWarningSource>(new OfflineWeather());
        services.AddSingleton<IWelcomeWeatherSource>(new OfflineBrief());
        services.AddSingleton<IWelcomeNewsSource>(new OfflineNews());
        services.AddScoped<IWelcomeGreetingService, WelcomeGreetingService>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IWelcomeGreetingService>();
        Assert.IsType<WelcomeGreetingService>(resolved);

        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var greeting = await resolved.GetGreetingAsync(
            userId, new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero));

        Assert.Equal("tester，10月9日", greeting.Content);
        Assert.Equal("备忘到点可以发 Bark 手机提醒了，工作台也会显示当天备忘摘要。", greeting.FeatureNote);
        Assert.Null(greeting.WeatherWarning);
        Assert.Empty(greeting.News);
    }

    [Fact]
    public void Compose_AddsShanghaiWeekdayAndNowText_OnlyWhenWeatherIsPresent()
    {
        var friday = new DateOnly(2026, 10, 9);
        Assert.Equal(
            "Bill.Gong，10月9日 · 周五 · 晴",
            WelcomeGreetingService.Compose("Bill.Gong", friday, 0, 0, "晴"));
        Assert.Equal(
            "Bill.Gong，10月9日 · 周五 · 晴。今天有 1 条备忘到期，还有 2 条备忘没做完。",
            WelcomeGreetingService.Compose("Bill.Gong", friday, 1, 2, " 晴 "));
        Assert.Equal("Bill.Gong，10月9日", WelcomeGreetingService.Compose("Bill.Gong", friday, 0, 0));
        Assert.Equal("Bill.Gong，10月9日", WelcomeGreetingService.Compose("Bill.Gong", friday, 0, 0, "   "));
        Assert.Equal(
            "Bill.Gong，10月11日 · 周日 · 阴",
            WelcomeGreetingService.Compose("Bill.Gong", new DateOnly(2026, 10, 11), 0, 0, "阴"));
    }

    private sealed class OfflineWeather : ISevereWeatherWarningSource
    {
        public Task<string?> GetWarningAsync(string? place, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class OfflineBrief : IWelcomeWeatherSource
    {
        public Task<WelcomeWeather> GetAsync(string? place, CancellationToken ct = default) =>
            Task.FromResult(default(WelcomeWeather));
    }

    private sealed class OfflineNews : IWelcomeNewsSource
    {
        public Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WelcomeNewsItem>>([]);
    }

    private static WelcomeGreetingService Greeting(MiraiNote.Data.Context.MiraiNoteDbContext db) =>
        new(db, new FeatureLaunchCatalog([]));

    private static Memo Memo(
        int userId,
        string content,
        DateTime? remindAt,
        bool isDone = false,
        bool isArchived = false) =>
        new()
        {
            UserId = userId,
            Section = "work",
            Content = content,
            RemindAt = remindAt,
            IsDone = isDone,
            IsArchived = isArchived,
        };

    private static async Task<int> AddUserAsync(MiraiNote.Data.Context.MiraiNoteDbContext db, string username, string email)
    {
        var user = new User
        {
            Username = username,
            NormalizedUserName = username,
            Email = email,
            PasswordHash = "hash",
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private sealed class FixedUser(int userId) : ICurrentUserService
    {
        public int UserId => userId;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    public void Dispose() => _fx.Dispose();
}
