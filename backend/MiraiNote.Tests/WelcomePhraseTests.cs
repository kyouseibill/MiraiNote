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
    public void Slot_IsTheClockPeriod_RainAndFridayDoNotChangeIt()
    {
        var fridayMorning = new DateTime(2026, 10, 9, 10, 0, 0);
        var fridayNight = new DateTime(2026, 10, 9, 23, 30, 0);
        var fridayDawn = new DateTime(2026, 10, 9, 0, 30, 0);
        Assert.Equal("morning", WelcomeGreetingService.ResolveSlot(fridayMorning));
        Assert.Equal("latenight", WelcomeGreetingService.ResolveSlot(fridayNight));
        Assert.Equal("latenight", WelcomeGreetingService.ResolveSlot(fridayDawn));
        Assert.Equal("morning", WelcomeGreetingService.ResolveSlot(new DateTime(2026, 10, 8, 10, 0, 0)));
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

        Assert.Equal("早上好，雅美", morning.GreetingLine);
        Assert.Equal("中午好，雅美", noon.GreetingLine);
        Assert.Equal("下午好，雅美", afternoon.GreetingLine);
        Assert.Equal("晚上好，雅美", evening.GreetingLine);
        Assert.Equal("夜深了，雅美，早点休息", late.GreetingLine);
        Assert.Equal("早上好，雅美", rain.GreetingLine);
        Assert.Equal("早上好，雅美", friday.GreetingLine);
        Assert.Equal("早上好，雅美", noPlace.GreetingLine);
        Assert.Equal("雅美", morning.Content);
        Assert.Equal("雅美", morning.DisplayName);
        Assert.Null(morning.InspirationLine);
    }

    [Fact]
    public async Task Greeting_StaysTheSameLineAcrossRefreshAndTheNextDay()
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();

        var first = await Greet(db, Shanghai(2026, 10, 8, 9, 0), null);
        var again = await Greet(db, Shanghai(2026, 10, 8, 9, 30), null);
        var next = await Greet(db, Shanghai(2026, 10, 9, 9, 0), "小雨");

        Assert.Equal("早上好，tester", first.GreetingLine);
        Assert.Equal(first.GreetingLine, again.GreetingLine);
        Assert.Equal(first.GreetingLine, next.GreetingLine);
        Assert.Equal(userId, await db.Users.Select(u => u.Id).SingleAsync());
    }

    [Fact]
    public void FixedTemplates_AreOneLinePerPeriod()
    {
        Assert.Equal("早上好，{name}", WelcomeGreetingCopy.Template(WelcomeGreetingCopy.Morning));
        Assert.Equal("中午好，{name}", WelcomeGreetingCopy.Template(WelcomeGreetingCopy.Noon));
        Assert.Equal("下午好，{name}", WelcomeGreetingCopy.Template(WelcomeGreetingCopy.Afternoon));
        Assert.Equal("晚上好，{name}", WelcomeGreetingCopy.Template(WelcomeGreetingCopy.Evening));
        Assert.Equal("夜深了，{name}，早点休息", WelcomeGreetingCopy.Template(WelcomeGreetingCopy.LateNight));
        Assert.Null(WelcomeGreetingService.Pick(Array.Empty<string>(), new DateOnly(2026, 10, 8), "morning"));
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
        Assert.Equal("10月9日 · 周五", ignoredBody.DateLine);
        Assert.Equal("早上好，tester", ignoredBody.GreetingLine);
        Assert.Equal("10月8日 · 周四", shiftedBody.DateLine);
        Assert.Equal("早上好，tester", shiftedBody.GreetingLine);
        Assert.Equal("10月8日 · 周四", spaceBody.DateLine);
        Assert.Equal("早上好，tester", spaceBody.GreetingLine);
        Assert.Equal("10月9日 · 周五", brokenBody.DateLine);
        Assert.Equal("早上好，tester", brokenBody.GreetingLine);
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
            "早上好，雅美",
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
