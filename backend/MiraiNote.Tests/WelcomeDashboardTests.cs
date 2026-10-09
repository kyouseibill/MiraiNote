using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.API.Controllers;
using MiraiNote.Core.Services;
using MiraiNote.Shared;
using MiraiNote.Shared.Common;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class WelcomeDashboardTests : IDisposable
{
    private const string ApiKey = "unit-test-qweather-key-SHOULD-NOT-LEAK";
    private const string ApiHost = "https://weather.example.test";

    private readonly MiraiTestFixture _fx = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public async Task EmptyCity_DoesNotFetchWeather_AndOmitsTheLine()
    {
        var handler = new RecordingHandler(RouteHappy);
        await SetPlace(null);
        var greeting = await Greet(handler, WithKey(ApiKey));

        Assert.Null(greeting.WeatherWarning);
        Assert.DoesNotContain(handler.Calls, call => call.Uri.Host == "weather.example.test");
        Assert.Equal("tester", greeting.Content);
        Assert.Equal("tester", greeting.DisplayName);
        Assert.Equal("10月9日 · 周五", greeting.DateLine);
        Assert.Null(greeting.WeatherBrief);
        Assert.DoesNotContain("·", greeting.Content);
        Assert.Equal(2, greeting.News.Count);
    }

    [Fact]
    public async Task MissingApiKey_HidesWeather_AndStillReturnsNews()
    {
        var handler = new RecordingHandler(RouteHappy);
        await SetPlace("中国-上海");
        var greeting = await Greet(handler, WithKey("  "));

        Assert.Null(greeting.WeatherWarning);
        Assert.DoesNotContain(handler.Calls, call => call.Uri.Host == "weather.example.test");
        Assert.Equal("tester", greeting.Content);
        Assert.Equal("10月9日 · 周五", greeting.DateLine);
        Assert.Null(greeting.WeatherBrief);
        Assert.DoesNotContain("·", greeting.Content);
        Assert.Equal(2, greeting.News.Count);
        Assert.Equal("TechCrunch 最新", greeting.News[0].Title);
        Assert.Equal("https://techcrunch.com/2026/10/09/newest", greeting.News[0].Url);
        Assert.All(handler.Calls, call => Assert.DoesNotContain(ApiKey, call.Uri.AbsoluteUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingApiHost_HidesWeather_AndStillReturnsNews()
    {
        var handler = new RecordingHandler(RouteHappy);
        await SetPlace("中国-上海");
        var greeting = await Greet(handler, new QWeatherOptions { ApiKey = ApiKey, ApiHost = "" });

        Assert.Null(greeting.WeatherWarning);
        Assert.DoesNotContain(handler.Calls, call => call.Uri.Host == "weather.example.test");
        Assert.Equal("tester", greeting.Content);
        Assert.Equal("10月9日 · 周五", greeting.DateLine);
        Assert.Null(greeting.WeatherBrief);
        Assert.DoesNotContain("晴", greeting.Content);
        Assert.DoesNotContain("晴", greeting.DateLine);
        Assert.NotEmpty(greeting.News);
    }

    [Fact]
    public async Task RssFailure_OmitsNews_AndKeepsNameDateMemoAndFeatureNote()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "weather.example.test")
                return Task.FromResult(RouteHappy(request, CancellationToken.None));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.GatewayTimeout));
        });
        await SetPlace("中国-上海");
        var notes = new[] { new FeatureLaunchNote(new DateOnly(2026, 10, 9), "功能句还在。") };
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        db.Memos.Add(new MiraiNote.Data.Entities.Memo
        {
            UserId = userId,
            Section = "work",
            Content = "今天要处理",
            RemindAt = ShanghaiClock.ParseToUtc("2026-10-09 15:00")
        });
        await db.SaveChangesAsync();

        var greeting = await new WelcomeGreetingService(
            db,
            new FeatureLaunchCatalog(notes),
            Weather(handler, WithKey(ApiKey)),
            News(handler)).GetGreetingAsync(userId, When);

        Assert.Equal("tester", greeting.Content);
        Assert.Equal("tester", greeting.DisplayName);
        Assert.Equal("10月9日 · 周五 · 晴 22°C", greeting.DateLine);
        Assert.Equal("晴 22°C", greeting.WeatherBrief);
        Assert.Equal("今天有 1 条备忘到期。", greeting.MemoSummary);
        Assert.DoesNotContain("晴", greeting.Content);
        Assert.DoesNotContain("10月", greeting.Content);
        Assert.Equal("功能句还在。", greeting.FeatureNote);
        Assert.Equal("上海中心气象台发布暴雨红色预警", greeting.WeatherWarning);
        Assert.DoesNotContain("暴雨", greeting.Content);
        Assert.DoesNotContain("暴雨", greeting.DateLine);
        Assert.Empty(greeting.News);
    }

    [Fact]
    public async Task HappyPath_ReturnsSevereWarningAndTwoNewestNews()
    {
        var handler = new RecordingHandler(RouteHappy);
        await SetPlace(" 中国 - 上海 ");
        var logs = new List<string>();
        var greeting = await Greet(handler, WithKey(ApiKey), logs, cache: _cache);

        Assert.Equal("tester", greeting.Content);
        Assert.Equal("10月9日 · 周五 · 晴 22°C", greeting.DateLine);
        Assert.Equal("晴 22°C", greeting.WeatherBrief);
        Assert.Null(greeting.MemoSummary);
        Assert.DoesNotContain("暴雨", greeting.Content);
        Assert.DoesNotContain("暴雨", greeting.DateLine);
        Assert.DoesNotContain("大雨", greeting.DateLine);
        Assert.Contains("22°C", greeting.DateLine);
        Assert.DoesNotContain("31", greeting.DateLine);
        Assert.DoesNotContain("22", greeting.Content);
        Assert.DoesNotContain("°", greeting.Content);
        Assert.DoesNotContain("22", greeting.WeatherWarning);
        Assert.Equal("上海中心气象台发布暴雨红色预警", greeting.WeatherWarning);
        Assert.Equal(
            ["TechCrunch 最新", "OpenAI 更新"],
            greeting.News.Select(item => item.Title).ToArray());
        Assert.All(greeting.News, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.StartsWith("https://", item.Url);
        });
        Assert.DoesNotContain(greeting.News, item => item.Title.Contains("更旧", StringComparison.Ordinal));

        var geo = Assert.Single(handler.Calls, call => call.Uri.AbsolutePath == "/geo/v2/city/lookup");
        Assert.Equal("上海", Uri.UnescapeDataString(geo.Query["location"]!));
        Assert.Equal("cn", geo.Query["range"]);
        Assert.Equal(ApiKey, geo.ApiKeyHeader);
        Assert.DoesNotContain(ApiKey, geo.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains(handler.Calls, call => call.Uri.AbsolutePath == "/weatheralert/v1/current/31.23/121.47");
        var now = Assert.Single(handler.Calls, call => call.Uri.AbsolutePath == "/v7/weather/now");
        Assert.Equal("101020100", now.Query["location"]);
        Assert.Equal("zh", now.Query["lang"]);
        Assert.Equal(ApiKey, now.ApiKeyHeader);
        Assert.DoesNotContain(ApiKey, now.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Calls, call =>
            call.Uri.AbsolutePath.Contains("/weather/3d", StringComparison.Ordinal)
            || call.Uri.AbsolutePath.Contains("/v7/weather/7d", StringComparison.Ordinal)
            || call.Uri.AbsolutePath.Contains("/v7/weather/24h", StringComparison.Ordinal));
        Assert.Equal(
            new[] { WelcomeNewsClient.OpenAiFeed.AbsoluteUri, WelcomeNewsClient.TechCrunchFeed.AbsoluteUri }.OrderBy(item => item),
            handler.Calls
                .Select(call => call.Uri.AbsoluteUri)
                .Where(uri => uri.Contains("openai.com", StringComparison.Ordinal) || uri.Contains("techcrunch.com", StringComparison.Ordinal))
                .OrderBy(item => item));

        var again = await Greet(handler, WithKey(ApiKey), logs, cache: _cache);
        Assert.Equal(greeting.WeatherWarning, again.WeatherWarning);
        Assert.Equal(greeting.Content, again.Content);
        Assert.Equal(1, handler.Calls.Count(call => call.Uri.AbsolutePath == "/geo/v2/city/lookup"));
        Assert.Equal(1, handler.Calls.Count(call => call.Uri.AbsolutePath == "/v7/weather/now"));
        Assert.DoesNotContain(logs, line => line.Contains(ApiKey, StringComparison.Ordinal));
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(greeting), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WeatherTimeoutAndMinorAlerts_AreOmitted()
    {
        var handler = new RecordingHandler((request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("weatheralert", StringComparison.Ordinal))
                return Delay(ct);
            if (request.RequestUri.AbsolutePath.Contains("/geo/", StringComparison.Ordinal))
                return Task.FromResult(Json(CityJson));
            return Task.FromResult(Xml(OpenAiRss));
        });
        await SetPlace("中国-上海");
        var greeting = await Greet(handler, WithKey(ApiKey), timeout: TimeSpan.FromMilliseconds(200));

        Assert.Null(greeting.WeatherWarning);
        Assert.Equal("tester", greeting.Content);
        Assert.Equal("10月9日 · 周五", greeting.DateLine);
        Assert.Null(greeting.WeatherBrief);
        Assert.DoesNotContain("晴", greeting.DateLine);
        Assert.NotEmpty(greeting.News);
    }

    [Fact]
    public async Task OnlyMinorOrCancelledWarnings_AreOmitted()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("weatheralert", StringComparison.Ordinal))
                return Task.FromResult(Json(MinorAndCancelJson));
            if (request.RequestUri.AbsolutePath.Contains("/geo/", StringComparison.Ordinal))
                return Task.FromResult(Json(CityJson));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        await SetPlace("中国-上海");
        var greeting = await Greet(handler, WithKey(ApiKey));

        Assert.Null(greeting.WeatherWarning);
        Assert.Empty(greeting.News);
    }

    [Fact]
    public async Task BareHostAndJwt_StayOutOfTheUrl()
    {
        const string jwt = "eyJhbGciOiJFZERTQSJ9.eyJzdWIiOiJ0ZXN0In0.signature";
        var handler = new RecordingHandler(RouteHappy);
        await SetPlace("中国-上海");
        var greeting = await Greet(handler, new QWeatherOptions
        {
            ApiKey = jwt,
            ApiHost = "weather.example.test"
        });

        Assert.Equal("上海中心气象台发布暴雨红色预警", greeting.WeatherWarning);
        var geo = Assert.Single(handler.Calls, call => call.Uri.AbsolutePath == "/geo/v2/city/lookup");
        Assert.Equal("https", geo.Uri.Scheme);
        Assert.Equal("weather.example.test", geo.Uri.Host);
        Assert.Null(geo.ApiKeyHeader);
        Assert.Equal("Bearer " + jwt, geo.Authorization);
        Assert.DoesNotContain(jwt, geo.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain(jwt, JsonSerializer.Serialize(greeting), StringComparison.Ordinal);
    }

    [Fact]
    public void HostOrCredential_MustBothBePresent()
    {
        Assert.False(QWeatherWarningClient.TryHost(null, out _));
        Assert.False(QWeatherWarningClient.TryHost("   ", out _));
        Assert.False(QWeatherWarningClient.TryHost("http://weather.example.test", out _));
        Assert.True(QWeatherWarningClient.TryHost("weather.example.test", out var bare));
        Assert.Equal("https://weather.example.test/", bare.AbsoluteUri);
        Assert.False(QWeatherWarningClient.IsJwt(ApiKey));
        Assert.True(QWeatherWarningClient.IsJwt("eyJhbGciOiJFZERTQSJ9.eyJzdWIiOiJ0ZXN0In0.signature"));
    }

    [Fact]
    public async Task WeatherHttpFailure_IsLoggedWithoutTheKey()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(ApiKey, Encoding.UTF8, "text/plain")
            }));
        var logs = new List<string>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var client = Weather(handler, WithKey(ApiKey), logs, cache);

        var warning = await client.GetWarningAsync("中国-上海");

        Assert.Null(warning);
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, line => line.Contains(ApiKey, StringComparison.Ordinal));
        Assert.All(handler.Calls, call => Assert.DoesNotContain(ApiKey, call.Uri.AbsoluteUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task OneFeedFails_TheOtherStillSuppliesNews()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "openai.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            if (request.RequestUri.Host == "techcrunch.com")
                return Task.FromResult(Xml(TechCrunchRss));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        var greeting = await Greet(handler, WithKey(ApiKey));

        Assert.Null(greeting.WeatherWarning);
        Assert.Equal(
            ["TechCrunch 最新", "TechCrunch 第三"],
            greeting.News.Select(item => item.Title).ToArray());
        Assert.DoesNotContain(greeting.News, item => item.Url.Contains("openai.com", StringComparison.Ordinal));
    }

    [Fact]
    public void NewsMerge_DedupesByUrl_AndKeepsTwoNewest()
    {
        var selected = WelcomeNewsMerge.Select(
        [
            new("更旧", "https://openai.com/news/old/", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            new("重复旧", "https://OpenAI.com/news/b", new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero)),
            new("OpenAI 更新", "https://openai.com/news/b/", new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero)),
            new("TechCrunch 最新", "https://techcrunch.com/2026/10/09/newest", new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero)),
            new("第三新", "https://techcrunch.com/2026/10/07/third", new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero)),
            new("", "https://example.com/blank", DateTimeOffset.UnixEpoch),
            new("坏链接", "javascript:alert(1)", DateTimeOffset.UnixEpoch)
        ], 2);

        Assert.Equal(["TechCrunch 最新", "OpenAI 更新"], selected.Select(item => item.Title).ToArray());
    }

    [Fact]
    public async Task PlaceSettings_StoresCountryCity_AndBlankClearsIt()
    {
        var service = new WelcomePlaceSettingsService(_fx.CreateContext());
        await using var lookup = _fx.CreateContext();
        var userId = await lookup.Users.Select(u => u.Id).SingleAsync();

        var saved = await service.UpdateAsync(userId, "  中国 - 上海  ", "  雅美  ");
        Assert.Equal("中国 - 上海", saved.Place);
        Assert.Equal("雅美", saved.Nickname);
        Assert.Equal("中国 - 上海", (await service.GetAsync(userId)).Place);
        Assert.Equal("雅美", (await service.GetAsync(userId)).Nickname);

        var cleared = await service.UpdateAsync(userId, "   ", "   ");
        Assert.Null(cleared.Place);
        Assert.Null(cleared.Nickname);
        await using var db = _fx.CreateContext();
        Assert.Null(await db.Users.Select(u => u.WeatherPlace).SingleAsync());
    }

    [Fact]
    public async Task PlaceSettings_RejectsOverlongText_WithoutEchoingIt()
    {
        var service = new WelcomePlaceSettingsService(_fx.CreateContext());
        await using var lookup = _fx.CreateContext();
        var userId = await lookup.Users.Select(u => u.Id).SingleAsync();
        var raw = new string('城', 81) + ApiKey;

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(userId, raw, null));

        Assert.Equal("国家-城市请控制在 80 个字以内", ex.Message);
        Assert.DoesNotContain(ApiKey, ex.Message, StringComparison.Ordinal);
        await using var db = _fx.CreateContext();
        Assert.Null(await db.Users.Select(u => u.WeatherPlace).SingleAsync());
    }

    [Fact]
    public void CityLookup_TakesTheFirstValidHit_WhenSeveralPlacesMatch()
    {
        const string json = """
        {
          "code": "200",
          "location": [
            {"name": "坏坐标", "lat": "31.00", "lon": "999", "country": "中国", "rank": "1"},
            {"name": "上海", "id": "101020100", "lat": "31.23170", "lon": "121.47264", "country": "中国", "rank": "15"},
            {"name": "rank更小的同名地", "lat": "30.11000", "lon": "120.11000", "country": "中国", "rank": "2"},
            {"name": "rank更大的同名地", "lat": "29.22000", "lon": "119.22000", "country": "中国", "rank": "90"}
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var point = QWeatherCities.Pick(document.RootElement, "中国", rangeWasApplied: true);

        Assert.Equal(new QWeatherCity("101020100", 31.23, 121.47), point);
    }

    [Fact]
    public void NowText_ReadsOnlyTheCurrentCondition()
    {
        using var ok = JsonDocument.Parse(NowJson);
        Assert.Equal("晴", QWeatherNow.ReadText(ok.RootElement));
        Assert.Equal("22", QWeatherNow.ReadTemp(ok.RootElement));
        Assert.Equal("101020100", QWeatherNow.Location(new QWeatherCity("101020100", 31.23, 121.47), ApiKey));
        Assert.Equal("121.47,31.23", QWeatherNow.Location(new QWeatherCity("", 31.231, 121.472), ApiKey));

        using var denied = JsonDocument.Parse("""{"code":"204","now":{"text":"晴","temp":"22"}}""");
        Assert.Null(QWeatherNow.ReadText(denied.RootElement));
        Assert.Null(QWeatherNow.ReadTemp(denied.RootElement));
        using var dailyOnly = JsonDocument.Parse("""{"code":"200","daily":[{"textDay":"大雨","tempMax":"31"}]}""");
        Assert.Null(QWeatherNow.ReadText(dailyOnly.RootElement));
        Assert.Null(QWeatherNow.ReadTemp(dailyOnly.RootElement));
        using var textOnly = JsonDocument.Parse("""{"code":"200","now":{"text":"多云"}}""");
        Assert.Equal("多云", QWeatherNow.ReadText(textOnly.RootElement));
        Assert.Null(QWeatherNow.ReadTemp(textOnly.RootElement));
        using var tempOnly = JsonDocument.Parse("""{"code":"200","now":{"temp":"-3","feelsLike":"1"}}""");
        Assert.Null(QWeatherNow.ReadText(tempOnly.RootElement));
        Assert.Equal("-3", QWeatherNow.ReadTemp(tempOnly.RootElement));
        using var numeric = JsonDocument.Parse("""{"code":"200","now":{"temp":24}}""");
        Assert.Equal("24", QWeatherNow.ReadTemp(numeric.RootElement));
        using var junk = JsonDocument.Parse("""{"code":"200","now":{"text":"多云","temp":"大雨"}}""");
        Assert.Null(QWeatherNow.ReadTemp(junk.RootElement));
        Assert.Equal("121.47,31.23", QWeatherNow.Location(new QWeatherCity(ApiKey, 31.23, 121.47), ApiKey));
        Assert.Null(QWeatherNow.Location(new QWeatherCity("unitTestKey", 31.23, 121.47), "unitTestKey"));
    }

    [Fact]
    public async Task NowBrief_WeekdayFollowsShanghaiCalendar_NotTheApi()
    {
        var handler = new RecordingHandler(RouteHappy);
        await SetPlace("日本-东京");
        using var cache = new MemoryCache(new MemoryCacheOptions());

        // 上海 2026-10-09 00:30，UTC 仍是 10 月 8 日（周四）。
        var friday = await Greet(
            handler,
            WithKey(ApiKey),
            cache: cache,
            moment: new DateTimeOffset(2026, 10, 8, 16, 30, 0, TimeSpan.Zero));
        var saturday = await Greet(
            handler,
            WithKey(ApiKey),
            cache: cache,
            moment: new DateTimeOffset(2026, 10, 9, 16, 0, 0, TimeSpan.Zero));

        Assert.Equal("tester", friday.Content);
        Assert.Equal("10月9日 · 周五 · 晴 22°C", friday.DateLine);
        Assert.Equal("晴 22°C", friday.WeatherBrief);
        Assert.DoesNotContain("周四", friday.Content);
        Assert.DoesNotContain("周四", friday.DateLine);
        Assert.Equal("tester", saturday.Content);
        Assert.Equal("10月10日 · 周六 · 晴 22°C", saturday.DateLine);
        Assert.Equal(1, handler.Calls.Count(call => call.Uri.AbsolutePath == "/v7/weather/now"));
        Assert.Equal("上海中心气象台发布暴雨红色预警", friday.WeatherWarning);
        Assert.DoesNotContain("暴雨", friday.Content);
        Assert.Equal(friday.WeatherWarning, saturday.WeatherWarning);
    }

    [Fact]
    public async Task NowFailure_OmitsTheSuffix_AndKeepsWarningAndNews()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v7/weather/now")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.GatewayTimeout));
            if (path == "/geo/v2/city/lookup")
                return Task.FromResult(Json(CityJson));
            if (path.StartsWith("/weatheralert/", StringComparison.Ordinal))
                return Task.FromResult(Json(AlertJson));
            if (request.RequestUri.Host == "openai.com")
                return Task.FromResult(Xml(OpenAiRss));
            if (request.RequestUri.Host == "techcrunch.com")
                return Task.FromResult(Xml(TechCrunchRss));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        await SetPlace("中国-上海");

        var greeting = await Greet(handler, WithKey(ApiKey));

        Assert.Equal("tester", greeting.Content);
        Assert.Equal("10月9日 · 周五", greeting.DateLine);
        Assert.Null(greeting.WeatherBrief);
        Assert.DoesNotContain("·", greeting.Content);
        Assert.DoesNotContain("晴", greeting.Content);
        Assert.DoesNotContain("晴", greeting.DateLine);
        Assert.Equal("上海中心气象台发布暴雨红色预警", greeting.WeatherWarning);
        Assert.Equal(2, greeting.News.Count);
        Assert.DoesNotContain(handler.Calls, call => call.Uri.AbsolutePath.Contains("/weather/3d", StringComparison.Ordinal));
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(greeting), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NowTimeout_OmitsTheSuffix_AndKeepsTheWarning()
    {
        var handler = new RecordingHandler((request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v7/weather/now")
                return Delay(ct);
            if (request.RequestUri.AbsolutePath.Contains("/geo/", StringComparison.Ordinal))
                return Task.FromResult(Json(CityJson));
            if (request.RequestUri.AbsolutePath.Contains("weatheralert", StringComparison.Ordinal))
                return Task.FromResult(Json(AlertJson));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        await SetPlace("中国-上海");

        var greeting = await Greet(handler, WithKey(ApiKey), timeout: TimeSpan.FromMilliseconds(200));

        Assert.Equal("tester", greeting.Content);
        Assert.Equal("10月9日 · 周五", greeting.DateLine);
        Assert.Null(greeting.WeatherBrief);
        Assert.Equal("上海中心气象台发布暴雨红色预警", greeting.WeatherWarning);
    }

    [Fact]
    public async Task Nickname_IsUsedOnTheGreeting_AndEmptyFallsBackToUsername()
    {
        await SetNickname("  雅美  ");
        var named = await Greet(new RecordingHandler(RouteHappy), new QWeatherOptions());
        Assert.Equal("雅美", named.Content);
        Assert.Equal("雅美", named.DisplayName);
        Assert.Equal("10月9日 · 周五", named.DateLine);
        Assert.Null(named.WeatherBrief);
        Assert.DoesNotContain("tester", named.Content);
        Assert.DoesNotContain("10月", named.Content);

        await SetNickname("   ");
        var fallback = await Greet(new RecordingHandler(RouteHappy), new QWeatherOptions());
        Assert.Equal("tester", fallback.Content);
        Assert.Equal("tester", fallback.DisplayName);
        Assert.Equal("10月9日 · 周五", fallback.DateLine);
    }

    [Fact]
    public async Task Nickname_WithWeather_StaysOnTheNameLine_NotTheDate()
    {
        await SetNickname("雅美");
        await SetPlace("中国-上海");
        var greeting = await Greet(new RecordingHandler(RouteHappy), WithKey(ApiKey));

        Assert.Equal("雅美", greeting.Content);
        Assert.Equal("雅美", greeting.DisplayName);
        Assert.Equal("10月9日 · 周五 · 晴 22°C", greeting.DateLine);
        Assert.Equal("晴 22°C", greeting.WeatherBrief);
        Assert.Equal("上海中心气象台发布暴雨红色预警", greeting.WeatherWarning);
        Assert.DoesNotContain("tester", greeting.Content);
        Assert.DoesNotContain("tester", greeting.DisplayName);
        Assert.DoesNotContain("晴", greeting.Content);
        Assert.DoesNotContain("10月", greeting.Content);
        Assert.DoesNotContain("暴雨", greeting.Content);
        Assert.DoesNotContain("暴雨", greeting.DateLine);
    }

    [Fact]
    public async Task Nickname_LengthCap_RejectsWithoutEchoingOrSaving()
    {
        var service = new WelcomePlaceSettingsService(_fx.CreateContext());
        await using var lookup = _fx.CreateContext();
        var userId = await lookup.Users.Select(u => u.Id).SingleAsync();
        await using (var seed = _fx.CreateContext())
        {
            var user = await seed.Users.SingleAsync();
            user.BarkDeviceKey = "unitTestBarkKey1";
            user.WeatherPlace = "中国-上海";
            user.Nickname = "雅美";
            await seed.SaveChangesAsync();
        }

        var twenty = new string('名', WelcomeNickname.MaxLength);
        var saved = await service.UpdateAsync(userId, "中国-上海", "  " + twenty + "  ");
        Assert.Equal(twenty, saved.Nickname);

        var raw = new string('名', WelcomeNickname.MaxLength + 1) + "unit-test-secret";
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(userId, "日本-东京", raw));
        Assert.Equal("昵称请控制在 20 个字以内", ex.Message);
        Assert.DoesNotContain("unit-test-secret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(raw, ex.Message, StringComparison.Ordinal);

        var current = await service.GetAsync(userId);
        Assert.Equal(twenty, current.Nickname);
        Assert.Equal("中国-上海", current.Place);
        var json = JsonSerializer.Serialize(current);
        Assert.DoesNotContain("unitTestBarkKey1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", json, StringComparison.Ordinal);
    }

    [Fact]
    public void UnparsedPlace_DoesNotCountAsACity()
    {
        Assert.False(WelcomePlace.TrySplit(null, out _, out _));
        Assert.False(WelcomePlace.TrySplit("   ", out _, out _));
        Assert.False(WelcomePlace.TrySplit("上海", out _, out _));
        Assert.True(WelcomePlace.TrySplit("日本－东京", out var country, out var city));
        Assert.Equal("日本", country);
        Assert.Equal("东京", city);
        Assert.Equal("jp", WelcomePlace.CountryCode(country));
    }

    [Fact]
    public async Task GreetingResponse_OmitsExternalLinesWhenOffline()
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var controller = new WelcomeController(
            new WelcomeGreetingService(db, new FeatureLaunchCatalog([])),
            new FixedUser(userId),
            new FixedClock(When));

        var result = await controller.GetGreeting(null, CancellationToken.None);
        var body = Assert.IsType<ApiResponse<WelcomeGreetingResponse>>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result).Value);

        Assert.Equal("tester", body.Data!.Content);
        Assert.Equal("tester", body.Data.DisplayName);
        Assert.Equal("10月9日 · 周五", body.Data.DateLine);
        Assert.Null(body.Data.WeatherBrief);
        Assert.Null(body.Data.MemoSummary);
        Assert.Null(body.Data.FeatureNote);
        Assert.Null(body.Data.WeatherWarning);
        Assert.Empty(body.Data.News);
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewsTitles_TranslateWithDeepSeek_AndStayCached()
    {
        const string deepSeekKey = "unit-test-deepseek-key-SHOULD-NOT-LEAK";
        string? posted = null;
        var chats = 0;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/chat/completions")
            {
                chats++;
                posted = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
                var inner = """{"titles":["人工智能融资再创新高","OpenAI 发布新模型"]}""";
                return Json(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = inner } } }
                }));
            }

            if (request.RequestUri.Host == "openai.com")
                return Xml(EnglishOpenAiRss);
            if (request.RequestUri.Host == "techcrunch.com")
                return Xml(EnglishTechCrunchRss);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var logs = new List<string>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var translator = Translator(handler, deepSeekKey, logs, cache);
        var news = News(handler, cache, translator: translator);

        var first = await news.GetLatestAsync();
        var second = await news.GetLatestAsync();

        Assert.Equal(
            ["人工智能融资再创新高", "OpenAI 发布新模型"],
            first.Select(item => item.Title).ToArray());
        Assert.Equal("https://techcrunch.com/2026/10/09/funding", first[0].Url);
        Assert.Equal("https://openai.com/news/model", first[1].Url);
        Assert.Equal(first.Select(item => item.Title), second.Select(item => item.Title));
        Assert.Equal(1, chats);
        Assert.NotNull(posted);
        Assert.DoesNotContain(deepSeekKey, posted, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls.Count(call => call.Uri.AbsolutePath == "/v1/chat/completions"));
        var chat = Assert.Single(handler.Calls, call => call.Uri.AbsolutePath == "/v1/chat/completions");
        Assert.DoesNotContain(deepSeekKey, chat.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("Bearer " + deepSeekKey, chat.Authorization);
        Assert.DoesNotContain(logs, line => line.Contains(deepSeekKey, StringComparison.Ordinal));
        Assert.DoesNotContain(deepSeekKey, JsonSerializer.Serialize(first), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewsTitles_KeepEnglishWhenTranslationFails()
    {
        const string deepSeekKey = "unit-test-deepseek-key-SHOULD-NOT-LEAK";
        var chats = 0;
        var handler = new RecordingHandler((request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/chat/completions")
            {
                chats++;
                if (chats == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
                    {
                        Content = new StringContent(deepSeekKey, Encoding.UTF8, "text/plain")
                    });
                }

                var inner = """{"titles":["中文标题"]}""";
                return Task.FromResult(Json(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content = inner } } }
                })));
            }

            if (request.RequestUri.Host == "openai.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (request.RequestUri.Host == "techcrunch.com")
                return Task.FromResult(Xml(EnglishTechCrunchRss));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        var logs = new List<string>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var translator = Translator(handler, deepSeekKey, logs, cache, TimeSpan.FromMilliseconds(200));
        var hanging = new RecordingHandler((request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/chat/completions")
                return Delay(ct);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var failed = await translator.TranslateAsync(
        [
            new WelcomeNewsItem("AI labs raise a new round", "https://techcrunch.com/2026/10/09/funding"),
            new WelcomeNewsItem("OpenAI ships a new model", "https://openai.com/news/model")
        ]);
        var retried = await translator.TranslateAsync(
        [
            new WelcomeNewsItem("AI labs raise a new round", "https://techcrunch.com/2026/10/09/funding")
        ]);
        var timedOut = await Translator(hanging, deepSeekKey, logs, new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromMilliseconds(200))
            .TranslateAsync([new WelcomeNewsItem("OpenAI ships a new model", "https://openai.com/news/model")]);

        Assert.Equal(2, failed.Count);
        Assert.Equal("AI labs raise a new round", failed[0].Title);
        Assert.Equal("https://techcrunch.com/2026/10/09/funding", failed[0].Url);
        Assert.Equal("OpenAI ships a new model", failed[1].Title);
        Assert.Equal("https://openai.com/news/model", failed[1].Url);
        Assert.Equal("中文标题", retried[0].Title);
        Assert.Equal("https://techcrunch.com/2026/10/09/funding", retried[0].Url);
        Assert.Equal("OpenAI ships a new model", timedOut[0].Title);
        Assert.DoesNotContain(logs, line => line.Contains(deepSeekKey, StringComparison.Ordinal));
        Assert.DoesNotContain(deepSeekKey, JsonSerializer.Serialize(failed), StringComparison.Ordinal);
        Assert.DoesNotContain(deepSeekKey, JsonSerializer.Serialize(timedOut), StringComparison.Ordinal);
    }

    [Fact]
    public void EnvExample_DoesNotCommitSecretsOrExtraWeatherKeys()
    {
        var path = FindRepoFile(".env.example");
        var text = File.ReadAllText(path);
        Assert.Contains("QWeather__ApiHost=", text, StringComparison.Ordinal);
        Assert.Contains("QWeather__ApiKey=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("QWeather__Now", text, StringComparison.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            var split = trimmed.IndexOf('=');
            Assert.True(split > 0, trimmed);
            Assert.Equal("", trimmed[(split + 1)..].Trim());
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
        _fx.Dispose();
    }

    private static readonly DateTimeOffset When = new(2026, 10, 9, 2, 0, 0, TimeSpan.Zero);

    private async Task<WelcomeGreeting> Greet(
        RecordingHandler handler,
        QWeatherOptions options,
        List<string>? logs = null,
        IMemoryCache? cache = null,
        TimeSpan? timeout = null,
        DateTimeOffset? moment = null)
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var service = new WelcomeGreetingService(
            db,
            new FeatureLaunchCatalog([]),
            Weather(handler, options, logs, cache, timeout),
            News(handler, cache, timeout));
        return await service.GetGreetingAsync(userId, moment ?? When);
    }

    private static QWeatherWarningClient Weather(
        RecordingHandler handler,
        QWeatherOptions options,
        List<string>? logs = null,
        IMemoryCache? cache = null,
        TimeSpan? timeout = null) =>
        new(
            Factory(handler, timeout),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options),
            new FixedClock(When),
            logs == null ? NullLogger<QWeatherWarningClient>.Instance : new ListLogger<QWeatherWarningClient>(logs));

    private static WelcomeNewsClient News(
        RecordingHandler handler,
        IMemoryCache? cache = null,
        TimeSpan? timeout = null,
        IWelcomeTitleTranslator? translator = null) =>
        new(
            Factory(handler, timeout),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<WelcomeNewsClient>.Instance,
            translator);

    private static DeepSeekWelcomeTitleTranslator Translator(
        RecordingHandler handler,
        string apiKey,
        List<string> logs,
        IMemoryCache cache,
        TimeSpan? timeout = null) =>
        new(
            Factory(handler, TimeSpan.FromSeconds(5)),
            cache,
            Options.Create(new DeepSeekOptions
            {
                ApiKey = apiKey,
                BaseUrl = "https://ai.example.test",
                Model = "deepseek-v4-flash"
            }),
            new ListLogger<DeepSeekWelcomeTitleTranslator>(logs),
            timeout ?? DeepSeekWelcomeTitleTranslator.DefaultTimeout);

    private static string FindRepoFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(name);
    }

    private static QWeatherOptions WithKey(string apiKey) => new()
    {
        ApiKey = apiKey,
        ApiHost = ApiHost
    };

    private async Task SetPlace(string? place)
    {
        await using var db = _fx.CreateContext();
        var user = await db.Users.SingleAsync();
        user.WeatherPlace = place;
        await db.SaveChangesAsync();
    }

    private async Task SetNickname(string? nickname)
    {
        await using var db = _fx.CreateContext();
        var user = await db.Users.SingleAsync();
        user.Nickname = nickname;
        await db.SaveChangesAsync();
    }

    private static IHttpClientFactory Factory(RecordingHandler handler, TimeSpan? timeout)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false)
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(4)
            });
        return factory.Object;
    }

    private static Task<HttpResponseMessage> Delay(CancellationToken ct) =>
        Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => new HttpResponseMessage(HttpStatusCode.OK), ct);

    private static HttpResponseMessage RouteHappy(HttpRequestMessage request, CancellationToken _)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/geo/v2/city/lookup")
            return Json(CityJson);
        if (path == "/v7/weather/now")
            return Json(NowJson);
        if (path.StartsWith("/weatheralert/v1/current/", StringComparison.Ordinal))
            return Json(AlertJson);
        if (request.RequestUri.Host == "openai.com")
            return Xml(OpenAiRss);
        if (request.RequestUri.Host == "techcrunch.com")
            return Xml(TechCrunchRss);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Xml(string xml) =>
        new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };

    private const string NowJson =
        """
        {"code":"200","now":{"text":"晴","temp":"22","icon":"100"},"daily":[{"textDay":"大雨","tempMax":"31"}]}
        """;

    private const string CityJson =
        """
        {"code":"200","location":[{"name":"上海","id":"101020100","lat":"31.23170","lon":"121.47264","country":"中国","rank":"15"}]}
        """;

    private const string AlertJson =
        """
        {
          "metadata": { "zeroResult": false },
          "alerts": [
            {
              "messageType": { "code": "alert" },
              "severity": "minor",
              "issuedTime": "2026-10-09T10:00:00+08:00",
              "expireTime": "2026-10-10T10:00:00+08:00",
              "headline": "大风蓝色预警"
            },
            {
              "messageType": { "code": "alert" },
              "severity": "severe",
              "issuedTime": "2026-10-09T08:00:00+08:00",
              "expireTime": "2026-10-10T08:00:00+08:00",
              "headline": "暴雨橙色预警"
            },
            {
              "messageType": { "code": "update" },
              "severity": "extreme",
              "issuedTime": "2026-10-09T09:00:00+08:00",
              "expireTime": "2026-10-10T09:00:00+08:00",
              "headline": "上海中心气象台发布暴雨红色预警"
            }
          ]
        }
        """;

    private const string MinorAndCancelJson =
        """
        {
          "alerts": [
            {
              "messageType": { "code": "alert" },
              "severity": "moderate",
              "issuedTime": "2026-10-09T08:00:00+08:00",
              "expireTime": "2026-10-10T08:00:00+08:00",
              "headline": "大风黄色预警"
            },
            {
              "messageType": { "code": "cancel" },
              "severity": "extreme",
              "issuedTime": "2026-10-09T09:00:00+08:00",
              "expireTime": "2026-10-10T09:00:00+08:00",
              "headline": "已取消的红色预警"
            },
            {
              "messageType": { "code": "alert" },
              "severity": "extreme",
              "issuedTime": "2026-10-08T09:00:00+08:00",
              "expireTime": "2026-10-08T12:00:00+08:00",
              "headline": "已经过期的红色预警"
            }
          ]
        }
        """;

    private const string EnglishOpenAiRss =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <title>OpenAI ships a new model</title>
            <link rel="alternate" href="https://openai.com/news/model"/>
            <published>2026-10-08T00:00:00Z</published>
          </entry>
        </feed>
        """;

    private const string EnglishTechCrunchRss =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0">
          <channel>
            <item>
              <title>AI labs raise a new round</title>
              <link>https://techcrunch.com/2026/10/09/funding</link>
              <pubDate>Fri, 09 Oct 2026 01:00:00 GMT</pubDate>
            </item>
          </channel>
        </rss>
        """;

    private const string OpenAiRss =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <title>OpenAI 更旧</title>
            <link rel="alternate" href="https://openai.com/news/old"/>
            <published>2026-10-01T00:00:00Z</published>
          </entry>
          <entry>
            <title>OpenAI 更新</title>
            <link rel="alternate" href="https://openai.com/news/b"/>
            <updated>2026-10-08T00:00:00Z</updated>
          </entry>
        </feed>
        """;

    private const string TechCrunchRss =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0">
          <channel>
            <item>
              <title>TechCrunch 第三</title>
              <link>https://techcrunch.com/2026/10/07/third</link>
              <pubDate>Wed, 07 Oct 2026 00:00:00 GMT</pubDate>
            </item>
            <item>
              <title>重复旧稿</title>
              <link>https://openai.com/news/b/</link>
              <pubDate>Fri, 02 Oct 2026 00:00:00 GMT</pubDate>
            </item>
            <item>
              <title>TechCrunch 最新</title>
              <link>https://techcrunch.com/2026/10/09/newest</link>
              <pubDate>Fri, 09 Oct 2026 01:00:00 GMT</pubDate>
            </item>
          </channel>
        </rss>
        """;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public List<RecordedCall> Calls { get; } = [];

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send)
            : this((request, ct) => Task.FromResult(send(request, ct)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var header = request.Headers.TryGetValues("X-QW-Api-Key", out var values)
                ? string.Join(',', values)
                : null;
            lock (Calls)
            {
                Calls.Add(new RecordedCall(
                    request.RequestUri!,
                    request.RequestUri!.Query,
                    header,
                    request.Headers.Authorization?.ToString()));
            }

            return _send(request, cancellationToken);
        }
    }

    private sealed record RecordedCall(Uri Uri, string RawQuery, string? ApiKeyHeader, string? Authorization)
    {
        public Dictionary<string, string> Query { get; } = Parse(RawQuery);

        private static Dictionary<string, string> Parse(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var trimmed = query.TrimStart('?');
            if (trimmed.Length == 0) return result;
            foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var index = part.IndexOf('=');
                if (index < 0)
                {
                    result[Uri.UnescapeDataString(part)] = "";
                    continue;
                }

                result[Uri.UnescapeDataString(part[..index])] = Uri.UnescapeDataString(part[(index + 1)..]);
            }

            return result;
        }
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

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            sink.Add(formatter(state, exception));
            if (exception != null)
                sink.Add(exception.ToString());
        }
    }
}
