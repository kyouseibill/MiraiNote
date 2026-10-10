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
    public void DefaultCountries_UseIso3166_PinChinaAndJapan_AndKeepTheOriginalTen()
    {
        var countries = RegionCatalog.Default;
        var names = countries.Select(item => item.Name).ToArray();
        Assert.Equal(["中国", "日本", "美国", "英国", "新加坡", "澳大利亚", "加拿大", "韩国", "德国", "法国"], names.Take(10).ToArray());
        Assert.InRange(countries.Count, 200, 260);
        Assert.Equal(249, countries.Count);
        Assert.Equal(countries.Count, countries.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(countries.Count, countries.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(countries, item => item is { Name: "印度", EnglishName: "India", Code: "in" });
        Assert.Contains(countries, item => item is { Name: "巴西", EnglishName: "Brazil", Code: "br" });
        Assert.Contains(countries, item => item is { Name: "俄罗斯", EnglishName: "Russia", Code: "ru" });
        Assert.Contains(countries, item => item is { Name: "挪威", EnglishName: "Norway", Code: "no" });
        Assert.All(countries, item => Assert.DoesNotContain(item.Name, ch => WelcomePlace.IsSeparatorChar(ch)));

        Assert.Same(countries, RegionCatalog.Resolve(null));
        Assert.Same(countries, RegionCatalog.Resolve(new RegionOptions()));
        Assert.Same(countries, RegionCatalog.Resolve(new RegionOptions
        {
            Countries = [new RegionCountryOption { Name = "法国", Code = "france" }],
        }));
    }

    [Fact]
    public void CountrySearch_MatchesChineseEnglishAndCode_WithoutReorderingPins()
    {
        var countries = RegionCatalog.Default;

        Assert.Equal(["jp"], RegionCatalog.Search(countries, "日本").Select(item => item.Code).ToArray());
        Assert.Equal(["jp"], RegionCatalog.Search(countries, "Japan").Select(item => item.Code).ToArray());
        Assert.Equal(["jp"], RegionCatalog.Search(countries, "JP").Select(item => item.Code).ToArray());
        Assert.Equal(["cn"], RegionCatalog.Search(countries, "cn").Select(item => item.Code).ToArray());

        var china = RegionCatalog.Search(countries, "中国");
        Assert.Equal("cn", china[0].Code);
        Assert.Contains(china, item => item.Code == "hk");
        Assert.Contains(china, item => item.Code == "mo");

        var byCode = RegionCatalog.Search(countries, "us");
        Assert.Equal("美国", byCode[0].Name);
        Assert.Contains(byCode, item => item.Code == "au");

        var india = RegionCatalog.Search(countries, "India");
        Assert.Contains(india, item => item is { Name: "印度", Code: "in" });
        Assert.Equal(countries, RegionCatalog.Search(countries, "   "));

        Assert.Equal("no", WelcomePlace.CountryCode("挪威"));
        Assert.Equal("jp", WelcomePlace.CountryCode("日本"));
        Assert.Equal("挪威 · 奥斯陆", Canonical("挪威-奥斯陆", countries));
    }

    [Fact]
    public void ConfiguredCountries_ReplaceTheDefaultList_AndKeepTheGivenOrder()
    {
        var options = new RegionOptions
        {
            Countries =
            [
                new RegionCountryOption { Name = "泰国", Code = "TH", EnglishName = "Thailand" },
                new RegionCountryOption { Name = "中国", Code = "cn" },
                new RegionCountryOption { Name = "", Code = "jp" },
                new RegionCountryOption { Name = "法国", Code = "france" },
            ]
        };

        var resolved = RegionCatalog.Resolve(options);
        Assert.Equal(["泰国", "中国"], resolved.Select(item => item.Name).ToArray());
        Assert.Equal("th", resolved[0].Code);
        Assert.Equal("Thailand", resolved[0].EnglishName);
        Assert.Equal("", resolved[1].EnglishName);
        Assert.Equal(["th"], RegionCatalog.Search(resolved, "thai").Select(item => item.Code).ToArray());
        Assert.Equal(["cn"], RegionCatalog.Search(resolved, "cn").Select(item => item.Code).ToArray());
        Assert.Empty(RegionCatalog.Search(resolved, "Japan"));

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
        var japan = await service.SearchCitiesAsync("日本", "");
        var unitedStates = await service.SearchCitiesAsync("美国", null);

        Assert.Equal(0, source.Calls);
        Assert.Contains(empty, item => item.Name == "上海");
        Assert.Contains(empty, item => item.Name == "北京");
        Assert.Contains(missing, item => item.Name == "上海");
        Assert.Contains(japan, item => item.Name == "札幌市");
        Assert.Contains(japan, item => item.Name == "大阪");
        Assert.Contains(japan, item => item.Name == "名古屋");
        Assert.Contains(japan, item => item.Name == "东京");
        Assert.Contains(unitedStates, item => item.Name == "纽约");
        Assert.All(empty, item => Assert.Equal("中国 · " + item.Name, item.Label));
        Assert.All(japan, item =>
        {
            Assert.StartsWith("日本 · ", item.Label);
            Assert.False(WelcomePlace.IsDistrictName(item.Name));
        });
        Assert.InRange(japan.Count, 8, CityNames.MaxResults);
        Assert.InRange(empty.Count, 8, CityNames.MaxResults);

        var norwayBlank = await service.SearchCitiesAsync("挪威", "   ");
        Assert.Empty(norwayBlank);
        var unknownBlank = await Assert.ThrowsAsync<BusinessException>(() => service.SearchCitiesAsync("不是国家", "  "));
        Assert.Equal("请选择国家", unknownBlank.Message);
        Assert.Equal(0, source.Calls);

        var countries = service.ListCountries();
        Assert.Equal("中国", countries[0].Name);
        Assert.Equal("cn", countries[0].Code);
        Assert.Equal("China", countries[0].EnglishName);
        Assert.Equal("日本", countries[1].Name);
        Assert.True(countries.Count >= 200);

        var cities = await service.SearchCitiesAsync("挪威", "奥");
        Assert.Equal("挪威", source.LastCountry);
        Assert.Equal("no", source.LastCode);
        Assert.Equal("奥", source.LastQuery);
        Assert.Equal(["奥"], cities.Select(item => item.Name).ToArray());
        Assert.Equal("挪威 · 奥", cities[0].Label);

        var unknown = await Assert.ThrowsAsync<BusinessException>(() => service.SearchCitiesAsync("不是国家", "城"));
        Assert.Equal("请选择国家", unknown.Message);
        Assert.Equal(1, source.Calls);
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

    [Theory]
    [InlineData("札幌")]
    [InlineData("Sapporo")]
    [InlineData("さっぽろ")]
    public async Task CitySearch_PromotesSharedAdm2_WhenGeoReturnsOnlyWards(string query)
    {
        const string json = """
        {
          "code": "200",
          "location": [
            {"name": "札幌", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "scenic"},
            {"name": "中央区", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "city"},
            {"name": "北区", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "city"},
            {"name": "东区", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "city"},
            {"name": "白石区", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "city"},
            {"name": "厚别区", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "city"},
            {"name": "丰平区", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "city"},
            {"name": "南区", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "city"},
            {"name": "西区", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "city"},
            {"name": "手稻区", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "city"},
            {"name": "清田区", "country": "日本", "adm1": "北海道", "adm2": "札幌市", "type": "city"}
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        Assert.Equal(["札幌市"], QWeatherCityList.Read(document.RootElement, "日本"));

        const string scenicOnly = """
        {"code":"200","location":[{"name":"札幌","country":"日本","adm1":"北海道","adm2":"札幌市","type":"scenic"}]}
        """;
        using var scenic = JsonDocument.Parse(scenicOnly);
        Assert.Empty(QWeatherCityList.Read(scenic.RootElement, "日本"));

        var handler = new RecordingHandler(_ => Json(json));
        var names = await Client(handler).SearchAsync("日本", "jp", query);
        Assert.Equal(["札幌市"], names);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(query, call.Query["location"]);
        Assert.Equal("20", call.Query["number"]);
        Assert.Empty(await Client(handler).SearchAsync("日本", "jp", "   "));
        Assert.Single(handler.Calls);
    }

    [Fact]
    public void CityList_MatchesTrimmedAdm2_AndDoesNotDuplicateTheCity()
    {
        const string json = """
        {
          "code": "200",
          "location": [
            {"name": "大阪", "country": "日本", "adm1": "大阪府", "adm2": "大阪市", "type": "city"},
            {"name": "北区", "country": "日本", "adm1": "大阪府", "adm2": "大阪市", "type": "city"},
            {"name": "大阪城", "country": "日本", "adm1": "大阪府", "adm2": "大阪市", "type": "scenic"},
            {"name": "名古屋市", "country": "日本", "adm1": "爱知县", "adm2": "名古屋市", "type": "city"},
            {"name": "中区", "country": "日本", "adm1": "爱知县", "adm2": "名古屋市", "type": "city"}
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        Assert.Equal(["大阪", "名古屋市"], QWeatherCityList.Read(document.RootElement, "日本"));
        Assert.Equal("大阪", CityNames.Key("大阪市"));
        Assert.Equal("札幌", CityNames.Key("札幌市"));
    }

    [Fact]
    public void CityList_KeepsChineseCitiesWhoseNamesUseShi()
    {
        const string json = """
        {
          "code": "200",
          "location": [
            {"name": "北京市", "country": "中国", "adm1": "北京市", "adm2": "北京", "type": "city"},
            {"name": "朝阳区", "country": "中国", "adm1": "北京市", "adm2": "北京", "type": "city"},
            {"name": "上海市", "country": "中国", "adm1": "上海市", "adm2": "上海市", "type": "city"},
            {"name": "无锡", "country": "中国", "adm1": "江苏省", "adm2": "无锡", "type": "city"},
            {"name": "苏州", "country": "中国", "adm1": "江苏省", "adm2": "苏州", "type": "city"}
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        Assert.Equal(["北京市", "上海市", "无锡", "苏州"], QWeatherCityList.Read(document.RootElement, "中国"));
    }

    [Fact]
    public void CityList_ReturnsUpToTwentyAfterFiltering()
    {
        var location = Enumerable.Range(0, 25).Select(i =>
        {
            var name = $"城{i:00}";
            return new Dictionary<string, string>
            {
                ["name"] = name,
                ["country"] = "挪威",
                ["adm1"] = name,
                ["adm2"] = name,
                ["type"] = "city",
            };
        });
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { code = "200", location }));
        var names = QWeatherCityList.Read(document.RootElement, "挪威");
        Assert.Equal(20, names.Count);
        Assert.Equal("城00", names[0]);
        Assert.Equal("城10", names[10]);
        Assert.Equal("城19", names[19]);
        Assert.DoesNotContain("城20", names);
    }

    [Fact]
    public async Task CitySearch_KeepsTwenty_AndFillsGapsFromCommonCities()
    {
        var many = Enumerable.Range(0, 25).Select(i => $"城{i:00}").ToArray();
        var source = new FixedCitySource(many);
        var service = new RegionService(Options.Create(new RegionOptions()), source);

        var capped = await service.SearchCitiesAsync("挪威", "城");
        Assert.Equal(20, capped.Count);
        Assert.Equal("城00", capped[0].Name);
        Assert.Equal("城19", capped[19].Name);
        Assert.Equal(1, source.Calls);

        source.Names = ["大阪市"];
        var osaka = await service.SearchCitiesAsync("日本", "大阪");
        Assert.Equal(["大阪市"], osaka.Select(item => item.Name).ToArray());

        source.Names = [];
        var sapporo = await service.SearchCitiesAsync("日本", "札幌");
        Assert.Equal(["札幌市"], sapporo.Select(item => item.Name).ToArray());
        Assert.Equal(3, source.Calls);

        var browsed = await service.SearchCitiesAsync("日本", "   ");
        Assert.Contains(browsed, item => item.Name == "札幌市");
        Assert.Equal(3, source.Calls);
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

    private static QWeatherWarningClient Client(HttpMessageHandler handler) =>
        new(
            Factory(handler),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new QWeatherOptions { ApiKey = ApiKey, ApiHost = "https://weather.example.test" }),
            TimeProvider.System,
            NullLogger<QWeatherWarningClient>.Instance);

    private sealed class FixedCitySource : IRegionCitySource
    {
        public FixedCitySource(IReadOnlyList<string> names) => Names = names.ToList();
        public List<string> Names { get; set; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> SearchAsync(string countryName, string countryCode, string query, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>(Names);
        }
    }

    private sealed class CountingCitySource : IRegionCitySource
    {
        public int Calls { get; private set; }
        public string? LastCountry { get; private set; }
        public string? LastCode { get; private set; }
        public string? LastQuery { get; private set; }

        public Task<IReadOnlyList<string>> SearchAsync(string countryName, string countryCode, string query, CancellationToken ct = default)
        {
            Calls++;
            LastCountry = countryName;
            LastCode = countryCode;
            LastQuery = query;
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
