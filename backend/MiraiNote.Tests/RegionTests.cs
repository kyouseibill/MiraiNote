using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using Xunit;

namespace MiraiNote.Tests;

public class RegionTests : IDisposable
{
    private const string ApiKey = "unit-test-qweather-key-SHOULD-NOT-LEAK";
    private readonly MiraiTestFixture _fx = new();

    [Fact]
    public void DefaultCountries_AreTheV1ListInOrder_AndNotAWorldDump()
    {
        var names = RegionCatalog.Default.Select(item => item.Name).ToArray();
        Assert.Equal(["中国", "日本", "美国", "英国", "新加坡", "澳大利亚", "加拿大", "韩国", "德国", "法国"], names);
        Assert.Equal(10, names.Length);
        Assert.DoesNotContain(names, name => name is "印度" or "巴西" or "俄罗斯");
    }

    [Fact]
    public void ConfiguredCountries_ReplaceTheDefaultList_AndKeepTheGivenOrder()
    {
        var options = new RegionOptions
        {
            Countries =
            [
                new RegionCountryOption { Name = "泰国", Code = "TH" },
                new RegionCountryOption { Name = "中国", Code = "cn" },
                new RegionCountryOption { Name = "", Code = "jp" },
                new RegionCountryOption { Name = "法国", Code = "france" },
            ]
        };

        var resolved = RegionCatalog.Resolve(options);
        Assert.Equal(["泰国", "中国"], resolved.Select(item => item.Name).ToArray());
        Assert.Equal("th", resolved[0].Code);

        Assert.Equal("泰国 · 曼谷", Canonical("泰国-曼谷", resolved));
        var rejected = WelcomePlace.TryCanonicalize("日本-东京", resolved, out _, out var error);
        Assert.False(rejected);
        Assert.Equal("请选择所在地区", error);
    }

    [Fact]
    public void CanonicalPlace_AcceptsHyphenAndMiddleDot_AndRejectsAThirdLevel()
    {
        Assert.True(WelcomePlace.TrySplit("中国 · 上海", out var country, out var city));
        Assert.Equal(("中国", "上海"), (country, city));
        Assert.True(WelcomePlace.TrySplit("中国-上海", out _, out var hyphenCity));
        Assert.Equal("上海", hyphenCity);
        Assert.Equal(WelcomePlace.CacheKey("中国-上海"), WelcomePlace.CacheKey("中国 · 上海"));

        var countries = RegionCatalog.Default;
        Assert.Equal("中国 · 上海", Canonical("  中国 - 上海  ", countries));
        Assert.False(WelcomePlace.TryCanonicalize("中国-上海-浦东", countries, out _, out _));
        Assert.False(WelcomePlace.TryCanonicalize("中国 · 徐汇区", countries, out _, out var districtError));
        Assert.Equal("请选择城市", districtError);
        Assert.True(WelcomePlace.TryCanonicalize("   ", countries, out var blank, out _));
        Assert.Null(blank);
    }

    [Fact]
    public async Task EmptyCityQuery_DoesNotCallQWeather()
    {
        var source = new CountingCitySource();
        var service = new RegionService(Options.Create(new RegionOptions()), source);

        var empty = await service.SearchCitiesAsync("中国", "   ");
        var missing = await service.SearchCitiesAsync("中国", null);

        Assert.Empty(empty);
        Assert.Empty(missing);
        Assert.Equal(0, source.Calls);

        var countries = service.ListCountries();
        Assert.Equal("中国", countries[0].Name);
        Assert.Equal("法国", countries[^1].Name);
    }

    [Fact]
    public async Task CitySearch_FiltersByCountry_AndDropsDistricts()
    {
        const string json = """
        {
          "code": "200",
          "location": [
            {"name": "上海", "country": "中国", "adm1": "上海市", "adm2": "上海", "type": "city"},
            {"name": "徐汇区", "country": "中国", "adm1": "上海市", "adm2": "上海", "type": "city"},
            {"name": "徐汇", "country": "中国", "adm1": "上海市", "adm2": "上海", "type": "city"},
            {"name": "大阪", "country": "日本", "adm1": "大阪府", "adm2": "大阪", "type": "city"}
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        Assert.Equal(["上海"], QWeatherCityList.Read(document.RootElement, "中国"));

        var handler = new RecordingHandler(_ => Json(json));
        var client = new QWeatherWarningClient(
            Factory(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new QWeatherOptions { ApiKey = ApiKey, ApiHost = "https://weather.example.test" }),
            TimeProvider.System,
            NullLogger<QWeatherWarningClient>.Instance);

        var names = await client.SearchAsync("中国", "cn", "上");
        Assert.Equal(["上海"], names);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("/geo/v2/city/lookup", call.AbsolutePath);
        Assert.Equal("cn", call.Query["range"]);
        Assert.Equal("上", call.Query["location"]);
        Assert.Equal("20", call.Query["number"]);
        Assert.DoesNotContain(ApiKey, call.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(ApiKey, call.ApiKeyHeader);
    }

    [Fact]
    public async Task PlaceSettings_RejectsUnknownCountry_WithoutEchoingTheText()
    {
        var service = new WelcomePlaceSettingsService(_fx.CreateContext());
        await using var lookup = _fx.CreateContext();
        var userId = await lookup.Users.Select(u => u.Id).SingleAsync();
        var secret = "unit-test-secret";

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(userId, "不是地区" + secret, null));

        Assert.Equal("请选择所在地区", ex.Message);
        Assert.DoesNotContain(secret, ex.Message, StringComparison.Ordinal);
        await using var db = _fx.CreateContext();
        Assert.Null(await db.Users.Select(u => u.WeatherPlace).SingleAsync());
    }

    public void Dispose() => _fx.Dispose();

    private static string Canonical(string raw, IReadOnlyList<RegionCountry> countries)
    {
        Assert.True(WelcomePlace.TryCanonicalize(raw, countries, out var canonical, out var error), error);
        return canonical!;
    }

    private static IHttpClientFactory Factory(HttpMessageHandler handler)
    {
        var factory = new Moq.Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(Moq.It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return factory.Object;
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class CountingCitySource : IRegionCitySource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> SearchAsync(string countryName, string countryCode, string query, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>([query]);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;
        public List<RecordedCall> Calls { get; } = [];

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> send) => _send = send;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var header = request.Headers.TryGetValues("X-QW-Api-Key", out var values)
                ? string.Join(',', values)
                : null;
            Calls.Add(new RecordedCall(request.RequestUri!, header));
            return Task.FromResult(_send(request));
        }
    }

    private sealed record RecordedCall(Uri Uri, string? ApiKeyHeader)
    {
        public string AbsolutePath => Uri.AbsolutePath;
        public Dictionary<string, string> Query { get; } = Parse(Uri.Query);

        private static Dictionary<string, string> Parse(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var trimmed = query.TrimStart('?');
            if (trimmed.Length == 0) return result;
            foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var index = part.IndexOf('=');
                var key = Uri.UnescapeDataString(index < 0 ? part : part[..index]);
                var value = index < 0 ? "" : Uri.UnescapeDataString(part[(index + 1)..]);
                result[key] = value;
            }

            return result;
        }
    }
}
