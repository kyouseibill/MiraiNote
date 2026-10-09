using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MiraiNote.API.Controllers;
using MiraiNote.Core.Services;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Data.Migrations;
using MiraiNote.Shared;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Welcome;
using Xunit;

namespace MiraiNote.Tests;

public class WelcomePhraseTests : IDisposable
{
    private readonly MiraiTestFixture _fx = new();

    [Theory]
    [InlineData(4, 59, "latenight")]
    [InlineData(5, 0, "morning")]
    [InlineData(10, 59, "morning")]
    [InlineData(11, 0, "noon")]
    [InlineData(12, 59, "noon")]
    [InlineData(13, 0, "afternoon")]
    [InlineData(17, 59, "afternoon")]
    [InlineData(18, 0, "evening")]
    [InlineData(22, 59, "evening")]
    [InlineData(23, 0, "latenight")]
    [InlineData(0, 0, "latenight")]
    public void Period_Boundaries_AreStartInclusiveAndEndExclusive(int hour, int minute, string period)
    {
        var wall = new DateTime(2026, 10, 8, hour, minute, 0);
        Assert.Equal(period, WelcomeGreetingService.PeriodOf(wall));
    }

    [Fact]
    public void Slot_LateNightBeatsRainAndFriday()
    {
        var fridayNight = new DateTime(2026, 10, 9, 23, 30, 0);
        var fridayDawn = new DateTime(2026, 10, 9, 0, 30, 0);
        Assert.Equal("latenight", WelcomeGreetingService.ResolveSlot(fridayNight, "中雨", new DateOnly(2026, 10, 9)));
        Assert.Equal("latenight", WelcomeGreetingService.ResolveSlot(fridayDawn, "雷阵雨", new DateOnly(2026, 10, 9)));
    }

    [Fact]
    public void Slot_RainBeatsFriday_AndMissingWeatherIsNotRain()
    {
        var morning = new DateTime(2026, 10, 9, 10, 0, 0);
        var friday = new DateOnly(2026, 10, 9);
        Assert.Equal("rain", WelcomeGreetingService.ResolveSlot(morning, "小雨", friday));
        Assert.Equal("rain", WelcomeGreetingService.ResolveSlot(morning, " 雷阵雨 ", friday));
        Assert.Equal("friday", WelcomeGreetingService.ResolveSlot(morning, null, friday));
        Assert.Equal("friday", WelcomeGreetingService.ResolveSlot(morning, "   ", friday));
        Assert.Equal("friday", WelcomeGreetingService.ResolveSlot(morning, "多云", friday));
        Assert.Equal("morning", WelcomeGreetingService.ResolveSlot(new DateTime(2026, 10, 8, 10, 0, 0), "晴", new DateOnly(2026, 10, 8)));
    }

    [Theory]
    [InlineData(3, 1, "spring")]
    [InlineData(5, 31, "spring")]
    [InlineData(6, 1, "summer")]
    [InlineData(8, 31, "summer")]
    [InlineData(9, 1, "autumn")]
    [InlineData(11, 30, "autumn")]
    [InlineData(12, 1, "winter")]
    [InlineData(1, 15, "winter")]
    [InlineData(2, 28, "winter")]
    public void Season_FollowsShanghaiMonth(int month, int day, string season)
    {
        Assert.Equal(season, WelcomeGreetingService.SeasonOf(new DateOnly(2026, month, day)));
    }

    [Fact]
    public async Task Greeting_UsesTheSlotPool_AndFillsTheName()
    {
        await using var db = _fx.CreateContext();
        var user = await db.Users.SingleAsync();
        user.Nickname = "雅美";
        user.WeatherPlace = "中国-上海";
        await db.SaveChangesAsync();
        await SeedSlots(db);

        var morning = await Greet(db, Shanghai(2026, 10, 8, 10, 0), "多云");
        var noon = await Greet(db, Shanghai(2026, 10, 8, 12, 0), "多云");
        var afternoon = await Greet(db, Shanghai(2026, 10, 8, 15, 0), "多云");
        var evening = await Greet(db, Shanghai(2026, 10, 8, 20, 0), "多云");
        var late = await Greet(db, Shanghai(2026, 10, 9, 23, 10), "中雨");
        var rain = await Greet(db, Shanghai(2026, 10, 8, 10, 0), "小到中雨");
        var friday = await Greet(db, Shanghai(2026, 10, 9, 10, 0), "晴");
        var noPlace = await GreetWithoutPlace(Shanghai(2026, 10, 8, 10, 0));

        Assert.Equal("早上好，雅美", morning.GreetingLine);
        Assert.Equal("中午好，雅美", noon.GreetingLine);
        Assert.Equal("下午好，雅美", afternoon.GreetingLine);
        Assert.Equal("晚上好，雅美", evening.GreetingLine);
        Assert.Equal("夜深了，雅美", late.GreetingLine);
        Assert.Equal("下雨了，雅美，记得带伞", rain.GreetingLine);
        Assert.Equal("周五了，雅美", friday.GreetingLine);
        Assert.Equal("早上好，雅美", noPlace.GreetingLine);
        Assert.Equal("雅美", morning.Content);
        Assert.Equal("雅美", morning.DisplayName);
    }

    [Fact]
    public async Task Greeting_IsStableAcrossRefresh_AndChangesTheNextDay()
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        db.WelcomePhrases.AddRange(
            Phrase("greeting", "{name}，甲", period: "morning", sort: 50),
            Phrase("greeting", "{name}，乙", period: "morning", sort: 1),
            Phrase("greeting", "{name}，丙", period: "morning", sort: 0),
            Phrase("greeting", "{name}，丁", period: "morning", sort: 9));
        await db.SaveChangesAsync();

        var first = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);
        var again = await Greet(db, Shanghai(2026, 10, 8, 9, 30), null);
        var next = await Greet(db, Shanghai(2026, 10, 15, 9, 0), null);
        var ordered = await db.WelcomePhrases.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Text).ToListAsync();
        var expected = WelcomeGreetingService.FillName(
            ordered[WelcomeGreetingService.PickIndex(new DateOnly(2026, 10, 8), "morning", ordered.Count)],
            "tester");

        Assert.Equal(expected, first.GreetingLine);
        Assert.Equal(first.GreetingLine, again.GreetingLine);
        Assert.NotEqual(first.GreetingLine, next.GreetingLine);
        Assert.Equal(userId, await db.Users.Select(u => u.Id).SingleAsync());
    }

    [Fact]
    public async Task EmptyOrDisabledGreetingPool_ReturnsNull()
    {
        await using var db = _fx.CreateContext();
        var empty = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);
        Assert.Null(empty.GreetingLine);

        db.WelcomePhrases.Add(Phrase("greeting", "早安，{name}", period: "morning", enabled: false));
        await db.SaveChangesAsync();
        var disabled = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);
        Assert.Null(disabled.GreetingLine);
    }

    [Fact]
    public async Task DisabledOrDeletedRow_DropsOutImmediately()
    {
        await using var db = _fx.CreateContext();
        var row = Phrase("greeting", "早安，{name}", period: "morning");
        db.WelcomePhrases.Add(row);
        await db.SaveChangesAsync();

        var before = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);
        row.IsEnabled = false;
        await db.SaveChangesAsync();
        var disabled = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);
        row.IsEnabled = true;
        row.IsDeleted = true;
        await db.SaveChangesAsync();
        var deleted = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);

        Assert.Equal("早安，tester", before.GreetingLine);
        Assert.Null(disabled.GreetingLine);
        Assert.Null(deleted.GreetingLine);
    }

    [Fact]
    public async Task Poem_PrefersTheSeason_ThenAnyEnabledPoem()
    {
        await using var db = _fx.CreateContext();
        db.WelcomePhrases.AddRange(
            Phrase("poem", "春句", author: "甲", source: "春题", season: "spring"),
            Phrase("poem", "秋句", author: "乙", source: "秋题", season: "autumn"),
            Phrase("poem", "停用秋句", author: "丙", source: "秋题二", season: "autumn", enabled: false));
        await db.SaveChangesAsync();

        var october = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);
        Assert.Equal("秋句", october.Poem!.Text);
        Assert.Equal("乙", october.Poem.Author);
        Assert.Equal("秋题", october.Poem.Source);

        var autumn = await db.WelcomePhrases.SingleAsync(row => row.Text == "秋句");
        autumn.IsEnabled = false;
        await db.SaveChangesAsync();

        var fallback = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);
        Assert.Equal("春句", fallback.Poem!.Text);
        Assert.Equal("甲", fallback.Poem.Author);
        Assert.Equal("春题", fallback.Poem.Source);

        var spring = await db.WelcomePhrases.SingleAsync(row => row.Text == "春句");
        spring.IsDeleted = true;
        await db.SaveChangesAsync();
        var none = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);
        Assert.Null(none.Poem);
    }

    [Fact]
    public async Task Poem_IsStableForTheDay_AndChangesTheNextDay()
    {
        await using var db = _fx.CreateContext();
        db.WelcomePhrases.AddRange(
            Phrase("poem", "秋一", author: "甲", source: "其一", season: "autumn"),
            Phrase("poem", "秋二", author: "乙", source: "其二", season: "autumn"));
        await db.SaveChangesAsync();

        var morning = await Greet(db, Shanghai(2026, 10, 8, 8, 0), null);
        var evening = await Greet(db, Shanghai(2026, 10, 8, 21, 0), null);
        var next = await Greet(db, Shanghai(2026, 10, 9, 8, 0), null);

        Assert.Equal(morning.Poem!.Text, evening.Poem!.Text);
        Assert.NotEqual(morning.Poem.Text, next.Poem!.Text);
    }

    [Fact]
    public async Task NowOverride_WorksInDevelopmentAndTest_NeverInProduction()
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        db.WelcomePhrases.Add(Phrase("greeting", "早安，{name}", period: "morning"));
        await db.SaveChangesAsync();
        var clock = new FixedClock(Shanghai(2026, 10, 9, 10, 0));
        var service = new WelcomeGreetingService(db, new FeatureLaunchCatalog([]));

        var production = new WelcomeController(service, new FixedUser(userId), clock, new TestHostEnvironment("Production"));
        var development = new WelcomeController(service, new FixedUser(userId), clock, new TestHostEnvironment("Development"));
        var test = new WelcomeController(service, new FixedUser(userId), clock, new TestHostEnvironment("Test"));

        var ignored = await production.GetGreeting(null, "2026-10-08T08:00:00+08:00", CancellationToken.None);
        var shifted = await development.GetGreeting(null, "2026-10-08T08:00:00+08:00", CancellationToken.None);
        var fromSpace = await test.GetGreeting(null, "2026-10-08T08:00:00 08:00", CancellationToken.None);
        var broken = await development.GetGreeting(null, "not-a-time", CancellationToken.None);

        var ignoredBody = Body(ignored);
        var shiftedBody = Body(shifted);
        var spaceBody = Body(fromSpace);
        var brokenBody = Body(broken);

        Assert.Equal("10月9日 · 周五", ignoredBody.DateLine);
        Assert.Null(ignoredBody.GreetingLine);
        Assert.Equal("10月8日 · 周四", shiftedBody.DateLine);
        Assert.Equal("早安，tester", shiftedBody.GreetingLine);
        Assert.Equal("10月8日 · 周四", spaceBody.DateLine);
        Assert.Equal("早安，tester", spaceBody.GreetingLine);
        Assert.Equal("10月9日 · 周五", brokenBody.DateLine);
        Assert.False(WelcomeGreetingService.AllowsWelcomeNowOverride(new TestHostEnvironment("Production")));
        Assert.True(WelcomeGreetingService.AllowsWelcomeNowOverride(new TestHostEnvironment("Development")));
        Assert.True(WelcomeGreetingService.AllowsWelcomeNowOverride(new TestHostEnvironment("Test")));
    }

    [Fact]
    public async Task Admin_RejectsUnknownKind_AndSoftDeletes()
    {
        await using var db = _fx.CreateContext();
        var admin = new WelcomePhraseAdminService(db);
        var created = await admin.CreateAsync(new WelcomePhraseWriteRequest
        {
            Kind = "poem",
            Text = "春眠不觉晓，处处闻啼鸟。",
            Author = "孟浩然",
            Source = "春晓",
            Season = "spring",
            SortOrder = 3,
        });
        Assert.True(created.IsEnabled);

        var disabled = await admin.SetEnabledAsync(created.Id, false);
        Assert.False(disabled.IsEnabled);
        var listed = await admin.ListAsync(null);
        Assert.Contains(listed, row => row.Id == created.Id && !row.IsEnabled);

        await admin.DeleteAsync(created.Id);
        Assert.Empty(await admin.ListAsync("poem"));

        var ex = await Assert.ThrowsAsync<BusinessException>(() => admin.CreateAsync(new WelcomePhraseWriteRequest
        {
            Kind = "haiku",
            Text = "一句",
        }));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_NonAdminIsForbidden_AdminCanList()
    {
        await using var factory = new WelcomePhraseApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiraiNoteDbContext>();
        await db.Database.EnsureCreatedAsync();
        var member = new User
        {
            Username = "member",
            Email = "member@example.com",
            PasswordHash = "hash",
            IsAdmin = false,
        };
        var admin = new User
        {
            Username = "root",
            Email = "root@example.com",
            PasswordHash = "hash",
            IsAdmin = true,
        };
        db.Users.AddRange(member, admin);
        await db.SaveChangesAsync();

        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var memberToken = tokens.GenerateAccessToken(member).token;
        var adminToken = tokens.GenerateAccessToken(admin).token;
        var client = factory.CreateClient();

        var anonymous = await client.GetAsync("/api/v1/admin/welcome-phrases");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        var forbidden = await client.GetAsync("/api/v1/admin/welcome-phrases");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var allowed = await client.GetAsync("/api/v1/admin/welcome-phrases");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var body = await allowed.Content.ReadFromJsonAsync<ApiResponse<List<WelcomePhraseDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.NotNull(body.Data);
    }

    [Fact]
    public void Migration_GrantsAppUser_AndSeedsAboutSixtyPoems()
    {
        Assert.Equal(60, WelcomePhraseMigrationSeed.PoemCount);
        var migration = Directory.GetFiles(
                FindRepoDir("backend/MiraiNote.Data/Migrations"),
                "*AddWelcomePhrase.cs",
                SearchOption.TopDirectoryOnly)
            .Single(path => !path.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var text = File.ReadAllText(migration);
        Assert.Contains("WelcomePhraseMigrationSeed.Insert", text, StringComparison.Ordinal);
        Assert.Contains("IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'appuser')", text, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE \"WelcomePhrase\" TO appuser;", text, StringComparison.Ordinal);
        Assert.Contains("GRANT USAGE, SELECT ON SEQUENCE \"WelcomePhrase_Id_seq\" TO appuser;", text, StringComparison.Ordinal);
        Assert.Contains("pg_get_serial_sequence('\"WelcomePhrase\"', 'Id')", text, StringComparison.Ordinal);
    }

    public void Dispose() => _fx.Dispose();

    private static DateTimeOffset Shanghai(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));

    private async Task<WelcomeGreeting> Greet(MiraiNoteDbContext db, DateTimeOffset moment, string? weather)
    {
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var source = new ScriptedWeather(weather);
        var service = new WelcomeGreetingService(db, new FeatureLaunchCatalog([]), source, new ScriptedNews(), source);
        return await service.GetGreetingAsync(userId, moment);
    }

    private async Task<WelcomeGreeting> GreetWithoutPlace(DateTimeOffset moment)
    {
        await using var db = _fx.CreateContext();
        var user = await db.Users.SingleAsync();
        user.Nickname = "雅美";
        user.WeatherPlace = null;
        await db.SaveChangesAsync();
        await SeedSlots(db);
        var rainy = new ScriptedWeather("暴雨");
        var service = new WelcomeGreetingService(db, new FeatureLaunchCatalog([]), rainy, new ScriptedNews(), rainy);
        return await service.GetGreetingAsync(user.Id, moment);
    }

    private static async Task SeedSlots(MiraiNoteDbContext db)
    {
        db.WelcomePhrases.AddRange(
            Phrase("greeting", "早上好，{name}", period: "morning"),
            Phrase("greeting", "中午好，{name}", period: "noon"),
            Phrase("greeting", "下午好，{name}", period: "afternoon"),
            Phrase("greeting", "晚上好，{name}", period: "evening"),
            Phrase("greeting", "夜深了，{name}", period: "latenight"),
            Phrase("greeting", "下雨了，{name}，记得带伞", special: "rain"),
            Phrase("greeting", "周五了，{name}", special: "friday"));
        await db.SaveChangesAsync();
    }

    private static WelcomePhrase Phrase(
        string kind,
        string text,
        string? period = null,
        string? special = null,
        string? season = null,
        string? author = null,
        string? source = null,
        bool enabled = true,
        int sort = 0) => new()
    {
        Kind = kind,
        Text = text,
        Period = period,
        Special = special,
        Season = season,
        Author = author,
        Source = source,
        IsEnabled = enabled,
        SortOrder = sort,
    };

    private static WelcomeGreetingResponse Body(Microsoft.AspNetCore.Mvc.ActionResult<ApiResponse<WelcomeGreetingResponse>> result)
    {
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<WelcomeGreetingResponse>>(ok.Value);
        return body.Data!;
    }

    private static string FindRepoDir(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(relative);
    }

    private sealed class ScriptedWeather(string? nowText) : IWelcomeWeatherSource, ISevereWeatherWarningSource
    {
        public Task<WelcomeWeather> GetAsync(string? place, CancellationToken ct = default) =>
            Task.FromResult(new WelcomeWeather(nowText, "18", null));

        public Task<string?> GetWarningAsync(string? place, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class ScriptedNews : IWelcomeNewsSource
    {
        public Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WelcomeNewsItem>>([]);
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
}

public sealed class WelcomePhraseApiFactory : WebApplicationFactory<Program>
{
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), "mirai-phrase-" + Guid.NewGuid().ToString("N") + ".db");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_sqlitePath))
        {
            try { File.Delete(_sqlitePath); } catch { /* 测试库占用时留给系统清理 */ }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting(
            "ConnectionStrings:DefaultConnection",
            "Host=127.0.0.1;Port=1;Database=mirainote;Username=mirai;Password=x;Timeout=1");
        builder.UseSetting("Jwt:Secret", "test-jwt-secret-0123456789-abcdef");
        builder.UseSetting("Jwt:Issuer", "MiraiNote");
        builder.UseSetting("Jwt:Audience", "MiraiNote");
        builder.UseSetting("App:PublicBaseUrl", "https://notes.example.com");
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(descriptor);

            var npgsql = services.Where(d =>
            {
                var names = new[]
                {
                    d.ServiceType.FullName,
                    d.ImplementationType?.FullName,
                    d.ImplementationInstance?.GetType().FullName,
                };
                return names.Any(name => name != null && (
                    name.Contains("Npgsql", StringComparison.Ordinal)
                    || name.Contains("PostgreSQL", StringComparison.Ordinal)
                    || name.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal)));
            }).ToList();
            foreach (var descriptor in npgsql)
                services.Remove(descriptor);

            services.RemoveAll<DbContextOptions<MiraiNoteDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<MiraiNoteDbContext>();
            services.AddDbContext<MiraiNoteDbContext>(options => options.UseSqlite($"Data Source={_sqlitePath}"));
        });
    }
}
