using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
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
using MiraiNote.Shared.Common;
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

        var morning = await Greet(db, Shanghai(2026, 10, 8, 10, 0), "多云");
        var noon = await Greet(db, Shanghai(2026, 10, 8, 12, 0), "多云");
        var afternoon = await Greet(db, Shanghai(2026, 10, 8, 15, 0), "多云");
        var evening = await Greet(db, Shanghai(2026, 10, 8, 20, 0), "多云");
        var late = await Greet(db, Shanghai(2026, 10, 9, 23, 10), "中雨");
        var rain = await Greet(db, Shanghai(2026, 10, 8, 10, 0), "小到中雨");
        var friday = await Greet(db, Shanghai(2026, 10, 9, 10, 0), "晴");
        var noPlace = await GreetWithoutPlace(Shanghai(2026, 10, 8, 10, 0));

        Assert.Equal(Line("morning", new DateOnly(2026, 10, 8), "雅美"), morning.GreetingLine);
        Assert.Equal(Line("noon", new DateOnly(2026, 10, 8), "雅美"), noon.GreetingLine);
        Assert.Equal(Line("afternoon", new DateOnly(2026, 10, 8), "雅美"), afternoon.GreetingLine);
        Assert.Equal(Line("evening", new DateOnly(2026, 10, 8), "雅美"), evening.GreetingLine);
        Assert.Equal(Line("latenight", new DateOnly(2026, 10, 9), "雅美"), late.GreetingLine);
        Assert.Equal("下雨了，雅美，记得带伞", rain.GreetingLine);
        Assert.Equal("周五了，雅美", friday.GreetingLine);
        Assert.Equal(Line("morning", new DateOnly(2026, 10, 8), "雅美"), noPlace.GreetingLine);
        Assert.Equal("雅美", morning.Content);
        Assert.Equal("雅美", morning.DisplayName);
        Assert.Null(morning.InspirationLine);
    }

    [Fact]
    public async Task Greeting_IsStableAcrossRefresh_AndChangesTheNextDay()
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();

        var first = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);
        var again = await Greet(db, Shanghai(2026, 10, 8, 9, 30), null);
        var next = await Greet(db, Shanghai(2026, 10, 9, 9, 0), null);

        Assert.Equal(Line("morning", new DateOnly(2026, 10, 8), "tester"), first.GreetingLine);
        Assert.Equal(first.GreetingLine, again.GreetingLine);
        Assert.NotEqual(first.GreetingLine, next.GreetingLine);
        Assert.Equal(userId, await db.Users.Select(u => u.Id).SingleAsync());
    }

    [Fact]
    public void EmptyPool_ReturnsNull_ButBuiltinPoolsAreFilled()
    {
        Assert.Null(WelcomeGreetingService.Pick(Array.Empty<string>(), new DateOnly(2026, 10, 8), "morning"));
        Assert.NotEmpty(WelcomeGreetingCopy.MorningLines);
        Assert.NotEmpty(WelcomeGreetingCopy.RainLines);
        Assert.NotEmpty(WelcomeGreetingCopy.FridayLines);
    }

    [Fact]
    public async Task NowOverride_WorksInDevelopmentAndTest_NeverInProduction()
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
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
        var morning = Line("morning", new DateOnly(2026, 10, 8), "tester");

        Assert.Equal("10月9日 · 周五", ignoredBody.DateLine);
        Assert.Equal("周五了，tester", ignoredBody.GreetingLine);
        Assert.Equal("10月8日 · 周四", shiftedBody.DateLine);
        Assert.Equal(morning, shiftedBody.GreetingLine);
        Assert.Equal("10月8日 · 周四", spaceBody.DateLine);
        Assert.Equal(morning, spaceBody.GreetingLine);
        Assert.Equal("10月9日 · 周五", brokenBody.DateLine);
        Assert.Equal("周五了，tester", brokenBody.GreetingLine);
        Assert.Null(ignoredBody.InspirationLine);
        Assert.False(WelcomeGreetingService.AllowsWelcomeNowOverride(new TestHostEnvironment("Production")));
        Assert.True(WelcomeGreetingService.AllowsWelcomeNowOverride(new TestHostEnvironment("Development")));
        Assert.True(WelcomeGreetingService.AllowsWelcomeNowOverride(new TestHostEnvironment("Test")));
    }

    [Fact]
    public void GreetingResponse_HasInspirationLine_AndNoAuthorOrSource()
    {
        var type = typeof(WelcomeGreetingResponse);
        Assert.NotNull(type.GetProperty(nameof(WelcomeGreetingResponse.InspirationLine)));
        Assert.NotNull(type.GetProperty(nameof(WelcomeGreetingResponse.GreetingLine)));
        Assert.Null(type.GetProperty("Poem"));
        Assert.Null(type.GetProperty("Author"));
        Assert.Null(type.GetProperty("Source"));

        var sample = new WelcomeGreetingResponse(
            "雅美",
            null,
            null,
            [],
            "雅美",
            "10月9日 · 周五",
            null,
            null,
            "周五了，雅美",
            "今天不必赶完所有事，把眼前这一小步走稳。");
        var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"inspirationLine\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("poem", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("author", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GET", "/api/v1/admin/welcome-phrases")]
    [InlineData("POST", "/api/v1/admin/welcome-phrases")]
    [InlineData("PUT", "/api/v1/admin/welcome-phrases/1")]
    [InlineData("PATCH", "/api/v1/admin/welcome-phrases/1/enabled")]
    [InlineData("DELETE", "/api/v1/admin/welcome-phrases/1")]
    public async Task AdminEndpoints_AreNotFound(string method, string path)
    {
        await using var factory = new WelcomePhraseApiFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiraiNoteDbContext>();
        await db.Database.EnsureCreatedAsync();
        var admin = new User
        {
            Username = "root",
            Email = "root@example.com",
            PasswordHash = "hash",
            IsAdmin = true,
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var tokens = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var client = factory.CreateClient();
        var anonymous = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateAccessToken(admin).token);
        var signedIn = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, signedIn.StatusCode);
    }

    public void Dispose() => _fx.Dispose();

    private static string Line(string slot, DateOnly day, string name)
    {
        var picked = WelcomeGreetingService.Pick(WelcomeGreetingCopy.Lines(slot), day, slot);
        return WelcomeGreetingService.FillName(picked!, name);
    }

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
        var rainy = new ScriptedWeather("暴雨");
        var service = new WelcomeGreetingService(db, new FeatureLaunchCatalog([]), rainy, new ScriptedNews(), rainy);
        return await service.GetGreetingAsync(user.Id, moment);
    }

    private static WelcomeGreetingResponse Body(Microsoft.AspNetCore.Mvc.ActionResult<ApiResponse<WelcomeGreetingResponse>> result)
    {
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
        var body = Assert.IsType<ApiResponse<WelcomeGreetingResponse>>(ok.Value);
        return body.Data!;
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
