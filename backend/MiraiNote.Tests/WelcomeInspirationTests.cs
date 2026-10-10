using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Data.Entities;
using Xunit;

namespace MiraiNote.Tests;

public class WelcomeInspirationTests : IDisposable
{
    private const string Line = "今天不必赶完所有事，把眼前这一小步走稳。";

    private readonly MiraiTestFixture _fx = new();

    [Fact]
    public void CacheKey_SharesCityWeatherAndHour_AndIgnoresWeatherWithoutCity()
    {
        var thursday = new DateTime(2026, 10, 8, 15, 10, 0);
        var shanghai = Key("中国-上海", "多云", "22", thursday);
        var sameBand = Key("中国-上海", "多云", "24", thursday);
        var nextBand = Key("中国-上海", "多云", "25", thursday);
        var otherWeather = Key("中国-上海", "小雨", "22", thursday);
        var nextHour = Key("中国-上海", "多云", "22", thursday.AddHours(1));
        var tokyo = Key("日本-东京", "多云", "22", thursday);
        var noCity = Key(null, "暴雨", "30", thursday);
        var noCityOtherWeather = Key("  ", "晴", "1", thursday);

        Assert.Equal(shanghai, sameBand);
        Assert.NotEqual(shanghai, nextBand);
        Assert.NotEqual(shanghai, otherWeather);
        Assert.NotEqual(shanghai, nextHour);
        Assert.NotEqual(shanghai, tokyo);
        Assert.Equal(noCity, noCityOtherWeather);
        Assert.Contains("_none", noCity, StringComparison.Ordinal);
        Assert.DoesNotContain("暴雨", noCity, StringComparison.Ordinal);
        Assert.Equal(-5, DeepSeekWelcomeInspiration.TempBand("-3"));
        Assert.Equal(TimeSpan.FromSeconds(2), DeepSeekWelcomeInspiration.DefaultTimeout);
    }

    [Fact]
    public void Prompt_WithCityMentionsWeatherLightly_WithoutCityOmitsPlaceAndWeather()
    {
        var wall = new DateTime(2026, 10, 8, 15, 0, 0);
        var withCity = DeepSeekWelcomeInspiration.UserPrompt(new WelcomeInspirationContext("中国-上海", "多云", "24", wall));
        var withoutCity = DeepSeekWelcomeInspiration.UserPrompt(new WelcomeInspirationContext(null, "暴雨", "30", wall));

        Assert.Contains("上海", withCity, StringComparison.Ordinal);
        Assert.Contains("多云", withCity, StringComparison.Ordinal);
        Assert.Contains("24°C", withCity, StringComparison.Ordinal);
        Assert.Contains("秋", withCity, StringComparison.Ordinal);
        Assert.Contains("15点", withCity, StringComparison.Ordinal);
        Assert.DoesNotContain("上海", withoutCity, StringComparison.Ordinal);
        Assert.DoesNotContain("暴雨", withoutCity, StringComparison.Ordinal);
        Assert.DoesNotContain("30", withoutCity, StringComparison.Ordinal);
        Assert.Contains("鸡汤", withoutCity, StringComparison.Ordinal);
        Assert.Contains("既写钟点又写具体气温", withoutCity, StringComparison.Ordinal);
        Assert.DoesNotContain("多云", withoutCity, StringComparison.Ordinal);
        Assert.Contains("不要假托", DeepSeekWelcomeInspiration.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("既写钟点又写具体气温", DeepSeekWelcomeInspiration.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("不要用城市名当主语开头", DeepSeekWelcomeInspiration.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("上海", DeepSeekWelcomeInspiration.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("「上海的」", withCity, StringComparison.Ordinal);
        Assert.Contains("不要写成天气预报", withCity, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("今天不必赶完所有事，把眼前这一小步走稳。", true)]
    public void Accept_RejectsEmptyAndKeepsAShortSentence(string raw, bool kept)
    {
        var line = DeepSeekWelcomeInspiration.Accept(raw, "secret");
        if (kept)
            Assert.Equal(raw, line);
        else
            Assert.Null(line);
    }

    [Fact]
    public void Accept_RejectsTooLong_Citations_Links_AndStackedSentences()
    {
        Assert.Equal(new string('安', 40), DeepSeekWelcomeInspiration.Accept(new string('安', 40), "secret"));
        Assert.Null(DeepSeekWelcomeInspiration.Accept(new string('安', 41), "secret"));
        Assert.Null(DeepSeekWelcomeInspiration.Accept("空山新雨后，天气晚来秋。《山居秋暝》", "secret"));
        Assert.Null(DeepSeekWelcomeInspiration.Accept("今天慢慢来——王维", "secret"));
        Assert.Null(DeepSeekWelcomeInspiration.Accept("今天慢慢来 https://example.com 就好", "secret"));
        Assert.Null(DeepSeekWelcomeInspiration.Accept("把日子过慢一点。心也就宽一点。", "secret"));
        Assert.Null(DeepSeekWelcomeInspiration.Accept("今天很好secret呀", "secret"));
        Assert.Equal("今天不必赶完所有事，把眼前这一小步走稳。", DeepSeekWelcomeInspiration.Accept("「今天不必赶完所有事，把眼前这一小步走稳。」", "secret"));
    }

    [Fact]
    public void Accept_RejectsForecastThatStatesClockAndTemperature()
    {
        const string bad = "上海的秋天多云，早晨九点气温二十四度。";
        Assert.InRange(bad.Length, 1, DeepSeekWelcomeInspiration.MaxChars);
        Assert.Null(DeepSeekWelcomeInspiration.Accept(bad, "secret", "中国-上海"));
        Assert.Null(DeepSeekWelcomeInspiration.Accept(bad, "secret"));
        Assert.Null(DeepSeekWelcomeInspiration.Accept("「" + bad + "」", "secret", "中国-上海"));
    }

    [Theory]
    [InlineData("早晨的光很软，适合把一件小事安静做完。", "中国-上海", true)]
    [InlineData("今天二十四度，心里也可以很轻很安静呀。", "中国-上海", true)]
    [InlineData("秋日的云很薄，适合把一件小事慢慢做完。", "中国-上海", true)]
    [InlineData("把步子放慢一点，二十四度的风也刚好。", "中国-上海", true)]
    [InlineData("早晨想起一年一度的光。", null, true)]
    [InlineData("上海的秋天云很薄，适合把窗开一条缝。", null, true)]
    [InlineData("上海的秋天云很薄，适合把窗开一条缝。", "中国-上海", false)]
    [InlineData("东京的傍晚适合把灯调暗一点。", "日本-东京", false)]
    [InlineData("东京的傍晚适合把灯调暗一点。", "中国-上海", true)]
    [InlineData("早晨九点气温二十四度，步子可以放慢些。", null, false)]
    [InlineData("下午二十四度，窗边可以坐一会儿。", null, false)]
    [InlineData("9点气温24°C，把步子放慢一点就好。", null, false)]
    public void Accept_ForecastAndCitySubject(string raw, string? place, bool kept)
    {
        var line = DeepSeekWelcomeInspiration.Accept(raw, "secret", place);
        if (kept)
            Assert.Equal(raw, line);
        else
            Assert.Null(line);
    }

    [Fact]
    public async Task SameCityAndHour_SharesOneSentence_WeatherOrHourChangeRegenerates()
    {
        var calls = 0;
        var handler = new ScriptHandler(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Ok(Line));
        });
        var inspiration = Create(handler, DeepSeekWelcomeInspiration.DefaultTimeout);
        await using var db = _fx.CreateContext();
        var first = await db.Users.SingleAsync();
        first.Nickname = "雅美";
        first.WeatherPlace = "中国-上海";
        var second = new User
        {
            Username = "other",
            Email = "other@example.com",
            PasswordHash = "hash",
            Nickname = "北北",
            WeatherPlace = "中国-上海",
        };
        db.Users.Add(second);
        await db.SaveChangesAsync();

        var weather = new MutableWeather("多云", "22");
        var afternoon = Shanghai(2026, 10, 8, 15, 0);
        var one = await Greet(db, inspiration, weather, first.Id, afternoon);
        var two = await Greet(db, inspiration, weather, second.Id, afternoon);
        Assert.Equal(Line, one.InspirationLine);
        Assert.Equal(Line, two.InspirationLine);
        Assert.Equal(1, calls);

        await Greet(db, inspiration, weather, first.Id, afternoon.AddHours(1));
        Assert.Equal(2, calls);

        weather.Text = "小雨";
        await Greet(db, inspiration, weather, first.Id, afternoon);
        Assert.Equal(3, calls);
        Assert.Equal("雅美", one.DisplayName);
        Assert.Equal("北北", two.DisplayName);
    }

    [Fact]
    public async Task NoCity_OmitsWeatherFromThePrompt_AndStillReturnsALine()
    {
        var bodies = new List<string>();
        var handler = new ScriptHandler(async request =>
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
            lock (bodies) bodies.Add(body);
            return Ok(Line);
        });
        var inspiration = Create(handler, DeepSeekWelcomeInspiration.DefaultTimeout);
        var wall = new DateTime(2026, 10, 8, 15, 0, 0);
        var line = await inspiration.GetLineAsync(new WelcomeInspirationContext(null, "暴雨", "30", wall));

        Assert.Equal(Line, line);
        var prompt = MiraiTestFixture.DecodeMessageText(Assert.Single(bodies));
        Assert.DoesNotContain("暴雨", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("上海", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("30", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutOrFailureOrEmptyOrTooLong_ReturnsNull_AndDoesNotCacheTheMiss()
    {
        var hangs = 0;
        var hanging = new ScriptHandler(async request =>
        {
            Interlocked.Increment(ref hangs);
            await Task.Delay(Timeout.Infinite, request.RequestAborted);
            return Ok(Line);
        });
        var slow = Create(hanging, TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew();
        var timedOut = await slow.GetLineAsync(Sample());
        watch.Stop();
        Assert.Null(timedOut);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.5), $"超时返回花了 {watch.Elapsed}");

        var again = await slow.GetLineAsync(Sample());
        Assert.Null(again);
        Assert.Equal(2, hangs);

        var failures = 0;
        var failing = Create(new ScriptHandler(_ =>
        {
            Interlocked.Increment(ref failures);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("nope"),
            });
        }), TimeSpan.FromSeconds(2));
        Assert.Null(await failing.GetLineAsync(Sample()));
        Assert.Null(await failing.GetLineAsync(Sample()));
        Assert.Equal(2, failures);

        var empties = 0;
        var empty = Create(new ScriptHandler(_ =>
        {
            Interlocked.Increment(ref empties);
            return Task.FromResult(Ok("  "));
        }), TimeSpan.FromSeconds(2));
        Assert.Null(await empty.GetLineAsync(Sample()));
        Assert.Null(await empty.GetLineAsync(Sample()));
        Assert.Equal(2, empties);

        var longs = 0;
        var tooLong = Create(new ScriptHandler(_ =>
        {
            Interlocked.Increment(ref longs);
            return Task.FromResult(Ok(new string('安', 41)));
        }), TimeSpan.FromSeconds(2));
        Assert.Null(await tooLong.GetLineAsync(Sample()));
        Assert.Null(await tooLong.GetLineAsync(Sample()));
        Assert.Equal(2, longs);
    }

    [Fact]
    public async Task ConcurrentCalls_ShareOneDeepSeekRequest()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var handler = new ScriptHandler(async request =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                entered.TrySetResult();
            await release.Task.WaitAsync(request.RequestAborted);
            return Ok(Line);
        });
        var inspiration = Create(handler, TimeSpan.FromSeconds(2));
        var first = inspiration.GetLineAsync(Sample());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = inspiration.GetLineAsync(Sample());
        await Task.Delay(80);
        Assert.Equal(1, Volatile.Read(ref calls));
        release.TrySetResult();
        var lines = await Task.WhenAll(first, second);
        Assert.Equal(Line, lines[0]);
        Assert.Equal(Line, lines[1]);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ForecastLine_IsRejected_RetriedOnce_ThenNullAndNotCached()
    {
        const string bad = "上海的秋天多云，早晨九点气温二十四度。";
        var calls = 0;
        var handler = new ScriptHandler(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Ok(bad));
        });
        var inspiration = Create(handler, DeepSeekWelcomeInspiration.DefaultTimeout);

        Assert.Null(await inspiration.GetLineAsync(Sample()));
        Assert.Equal(2, calls);
        Assert.Null(await inspiration.GetLineAsync(Sample()));
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task ForecastLine_RetriesOnce_AndKeepsTheNextGoodLine()
    {
        const string bad = "上海的秋天多云，早晨九点气温二十四度。";
        var calls = 0;
        var handler = new ScriptHandler(_ =>
        {
            var n = Interlocked.Increment(ref calls);
            return Task.FromResult(Ok(n == 1 ? bad : Line));
        });
        var inspiration = Create(handler, DeepSeekWelcomeInspiration.DefaultTimeout);

        Assert.Equal(Line, await inspiration.GetLineAsync(Sample()));
        Assert.Equal(2, calls);
        Assert.Equal(Line, await inspiration.GetLineAsync(Sample()));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MissingCredentials_ReturnsNullWithoutCalling()
    {
        var calls = 0;
        var handler = new ScriptHandler(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Ok(Line));
        });
        var inspiration = new DeepSeekWelcomeInspiration(
            Factory(handler),
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new DeepSeekOptions { ApiKey = "  ", BaseUrl = "https://ai.example.test", Model = "deepseek-v4-flash" }),
            NullLogger<DeepSeekWelcomeInspiration>.Instance,
            TimeSpan.FromSeconds(2));

        Assert.Null(await inspiration.GetLineAsync(Sample()));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Greeting_KeepsTheRest_WhenInspirationFails()
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var source = new ThrowingInspiration();
        var weather = new MutableWeather(null, null);
        var greeting = await Greet(db, source, weather, userId, Shanghai(2026, 10, 9, 10, 0));

        Assert.Equal("tester", greeting.DisplayName);
        Assert.Equal("10月9日 · 周五", greeting.DateLine);
        Assert.Equal("早上好，tester", greeting.GreetingLine);
        Assert.Null(greeting.InspirationLine);
        Assert.Equal(1, source.Calls);
    }

    public void Dispose() => _fx.Dispose();

    private static WelcomeInspirationContext Sample() =>
        new("中国-上海", "多云", "22", new DateTime(2026, 10, 8, 15, 0, 0));

    private static string Key(string? place, string? weather, string? temp, DateTime wall) =>
        DeepSeekWelcomeInspiration.CacheKey(new WelcomeInspirationContext(place, weather, temp, wall));

    private static DateTimeOffset Shanghai(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));

    private static HttpResponseMessage Ok(string content) => MiraiTestFixture.DeepSeekContentResponse(content);

    private static DeepSeekWelcomeInspiration Create(HttpMessageHandler handler, TimeSpan timeout) =>
        new(
            Factory(handler),
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new DeepSeekOptions
            {
                ApiKey = "test-key",
                BaseUrl = "https://ai.example.test",
                Model = "deepseek-v4-flash",
            }),
            NullLogger<DeepSeekWelcomeInspiration>.Instance,
            timeout);

    private static IHttpClientFactory Factory(HttpMessageHandler handler)
    {
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(Moq.It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            });
        return factory.Object;
    }

    private static async Task<WelcomeGreeting> Greet(
        MiraiNote.Data.Context.MiraiNoteDbContext db,
        IWelcomeInspirationSource inspiration,
        IWelcomeWeatherSource weather,
        int userId,
        DateTimeOffset moment)
    {
        var service = new WelcomeGreetingService(
            db,
            new FeatureLaunchCatalog([]),
            weather as ISevereWeatherWarningSource ?? new MutableWeather(null, null),
            new SilentNews(),
            weather,
            inspiration);
        return await service.GetGreetingAsync(userId, moment);
    }

    private sealed class MutableWeather(string? text, string? temp) : IWelcomeWeatherSource, ISevereWeatherWarningSource
    {
        public string? Text { get; set; } = text;
        public string? Temp { get; set; } = temp;

        public Task<WelcomeWeather> GetAsync(string? place, CancellationToken ct = default) =>
            Task.FromResult(new WelcomeWeather(Text, Temp, null));

        public Task<string?> GetWarningAsync(string? place, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class SilentNews : IWelcomeNewsSource
    {
        public Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WelcomeNewsItem>>([]);
    }

    private sealed class ThrowingInspiration : IWelcomeInspirationSource
    {
        public int Calls { get; private set; }

        public Task<string?> GetLineAsync(WelcomeInspirationContext context, CancellationToken ct = default)
        {
            Calls++;
            throw new InvalidOperationException("inspiration down");
        }
    }

    private sealed class ScriptHandler : HttpMessageHandler
    {
        private readonly Func<ScriptRequest, Task<HttpResponseMessage>> _respond;

        public ScriptHandler(Func<ScriptRequest, Task<HttpResponseMessage>> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return await _respond(new ScriptRequest(request, linked.Token));
        }
    }

    private sealed class ScriptRequest(HttpRequestMessage request, CancellationToken requestAborted)
    {
        public HttpRequestMessage Request { get; } = request;
        public CancellationToken RequestAborted { get; } = requestAborted;
        public HttpContent? Content => Request.Content;
    }
}
