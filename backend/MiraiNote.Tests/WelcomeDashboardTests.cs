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
        Assert.Equal("tester，10月9日", greeting.Content);
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

        Assert.Equal("tester，10月9日。今天有 1 条备忘到期。", greeting.Content);
        Assert.Equal("功能句还在。", greeting.FeatureNote);
        Assert.Equal("上海中心气象台发布暴雨红色预警", greeting.WeatherWarning);
        Assert.Empty(greeting.News);
    }

    [Fact]
    public async Task HappyPath_ReturnsSevereWarningAndTwoNewestNews()
    {
        var handler = new RecordingHandler(RouteHappy);
        await SetPlace(" 中国 - 上海 ");
        var logs = new List<string>();
        var greeting = await Greet(handler, WithKey(ApiKey), logs);

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
        Assert.DoesNotContain(handler.Calls, call =>
            call.Uri.AbsolutePath.Contains("/v7/weather", StringComparison.Ordinal)
            || call.Uri.AbsolutePath.Contains("/weather/3d", StringComparison.Ordinal)
            || call.Uri.AbsolutePath.Contains("/weather/now", StringComparison.Ordinal));
        Assert.Equal(
            new[] { WelcomeNewsClient.OpenAiFeed.AbsoluteUri, WelcomeNewsClient.TechCrunchFeed.AbsoluteUri }.OrderBy(item => item),
            handler.Calls
                .Select(call => call.Uri.AbsoluteUri)
                .Where(uri => uri.Contains("openai.com", StringComparison.Ordinal) || uri.Contains("techcrunch.com", StringComparison.Ordinal))
                .OrderBy(item => item));

        var again = await Greet(handler, WithKey(ApiKey), logs);
        Assert.Equal(greeting.WeatherWarning, again.WeatherWarning);
        Assert.Equal(1, handler.Calls.Count(call => call.Uri.AbsolutePath == "/geo/v2/city/lookup"));
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
        Assert.Equal("tester，10月9日", greeting.Content);
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

        var saved = await service.UpdateAsync(userId, "  中国 - 上海  ");
        Assert.Equal("中国 - 上海", saved.Place);
        Assert.Equal("中国 - 上海", (await service.GetAsync(userId)).Place);

        var cleared = await service.UpdateAsync(userId, "   ");
        Assert.Null(cleared.Place);
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

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(userId, raw));

        Assert.Equal("国家-城市请控制在 80 个字以内", ex.Message);
        Assert.DoesNotContain(ApiKey, ex.Message, StringComparison.Ordinal);
        await using var db = _fx.CreateContext();
        Assert.Null(await db.Users.Select(u => u.WeatherPlace).SingleAsync());
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

        Assert.Equal("tester，10月9日", body.Data!.Content);
        Assert.Null(body.Data.FeatureNote);
        Assert.Null(body.Data.WeatherWarning);
        Assert.Empty(body.Data.News);
        Assert.DoesNotContain(ApiKey, JsonSerializer.Serialize(body), StringComparison.Ordinal);
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
        TimeSpan? timeout = null)
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var service = new WelcomeGreetingService(
            db,
            new FeatureLaunchCatalog([]),
            Weather(handler, options, logs, _cache, timeout),
            News(handler, _cache, timeout));
        return await service.GetGreetingAsync(userId, When);
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

    private static WelcomeNewsClient News(RecordingHandler handler, IMemoryCache? cache = null, TimeSpan? timeout = null) =>
        new(Factory(handler, timeout), cache ?? new MemoryCache(new MemoryCacheOptions()), NullLogger<WelcomeNewsClient>.Instance);

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
