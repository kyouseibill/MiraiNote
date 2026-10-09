using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MiraiNote.Core.Services;

public interface ISevereWeatherWarningSource
{
    /// <summary>没有城市、没有 Host、没有密钥，或外部失败时返回 null。不抛给欢迎语。</summary>
    Task<string?> GetWarningAsync(string? place, CancellationToken ct = default);
}

/// <summary>
/// 和风实时特别预警。只使用天气预警接口，不请求每日预报。
/// 旧的 /v7/warning/now 已于 2026-10-01 停服，这里走城市搜索拿到经纬度，再查现行预警。
/// </summary>
public sealed class QWeatherWarningClient : ISevereWeatherWarningSource
{
    public const string HttpClientName = "QWeather";
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(8);

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly IOptions<QWeatherOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<QWeatherWarningClient> _logger;

    public QWeatherWarningClient(
        IHttpClientFactory http,
        IMemoryCache cache,
        IOptions<QWeatherOptions> options,
        TimeProvider clock,
        ILogger<QWeatherWarningClient> logger)
    {
        _http = http;
        _cache = cache;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<string?> GetWarningAsync(string? place, CancellationToken ct = default)
    {
        if (!WelcomePlace.TrySplit(place, out _, out _))
            return null;
        if (string.IsNullOrWhiteSpace(_options.Value.ApiKey))
            return null;
        if (!TryHost(_options.Value.ApiHost, out _))
            return null;

        var cacheKey = "welcome:qweather:" + WelcomePlace.CacheKey(place!);
        var pending = _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await FetchAsync(place!, CancellationToken.None);
        });
        var text = await pending.WaitAsync(ct);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private async Task<string> FetchAsync(string place, CancellationToken ct)
    {
        try
        {
            if (!WelcomePlace.TrySplit(place, out var country, out var city))
                return "";

            var options = _options.Value;
            var apiKey = options.ApiKey.Trim();
            if (!TryHost(options.ApiHost, out var host))
                return "";

            var point = await LookupAsync(host, apiKey, country, city, ct);
            if (point is null)
                return "";

            var warning = await ReadWarningAsync(host, apiKey, point.Value, ct);
            if (warning != null && warning.Contains(apiKey, StringComparison.Ordinal))
                return "";
            return warning ?? "";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("和风特别预警暂不可用");
            return "";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            _logger.LogInformation("和风特别预警暂不可用");
            return "";
        }
    }

    private async Task<(double Lat, double Lon)?> LookupAsync(
        Uri host,
        string apiKey,
        string country,
        string city,
        CancellationToken ct)
    {
        var query = "location=" + Uri.EscapeDataString(city) + "&number=5&lang=zh";
        var code = WelcomePlace.CountryCode(country);
        if (code != null)
            query += "&range=" + code;

        using var document = await GetJsonAsync(host, "/geo/v2/city/lookup?" + query, apiKey, ct);
        return document is null ? null : QWeatherCities.Pick(document.RootElement, country, code != null);
    }

    private async Task<string?> ReadWarningAsync(
        Uri host,
        string apiKey,
        (double Lat, double Lon) point,
        CancellationToken ct)
    {
        var lat = point.Lat.ToString("0.00", CultureInfo.InvariantCulture);
        var lon = point.Lon.ToString("0.00", CultureInfo.InvariantCulture);
        var path = "/weatheralert/v1/current/" + lat + "/" + lon + "?lang=zh";
        using var document = await GetJsonAsync(host, path, apiKey, ct);
        if (document is null)
            return null;
        return QWeatherAlerts.SelectHeadline(document.RootElement, _clock.GetUtcNow());
    }

    private async Task<JsonDocument?> GetJsonAsync(Uri host, string pathAndQuery, string apiKey, CancellationToken ct)
    {
        var uri = new Uri(host, pathAndQuery);
        if (uri.AbsoluteUri.Contains(apiKey, StringComparison.Ordinal))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyCredential(request, apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = _http.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation("和风特别预警暂不可用");
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    /// <summary>JWT 走 Bearer，其余走 X-QW-Api-Key。凭证不放进 URL。</summary>
    internal static void ApplyCredential(HttpRequestMessage request, string credential)
    {
        if (IsJwt(credential))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            return;
        }

        request.Headers.TryAddWithoutValidation("X-QW-Api-Key", credential);
    }

    internal static bool IsJwt(string credential)
    {
        var parts = credential.Split('.');
        if (parts.Length != 3)
            return false;
        foreach (var part in parts)
        {
            if (part.Length == 0)
                return false;
            foreach (var ch in part)
            {
                var ok = char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_';
                if (!ok)
                    return false;
            }
        }

        return true;
    }

    internal static bool TryHost(string? apiHost, out Uri host)
    {
        host = null!;
        if (string.IsNullOrWhiteSpace(apiHost))
            return false;

        var text = apiHost.Trim().TrimEnd('/');
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed))
            return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
            return false;
        if (parsed.AbsolutePath is not ("/" or ""))
            return false;

        host = new Uri(parsed.GetLeftPart(UriPartial.Authority));
        return true;
    }
}

internal static class QWeatherCities
{
    public static (double Lat, double Lon)? Pick(JsonElement root, string country, bool rangeWasApplied)
    {
        if (root.TryGetProperty("code", out var code))
        {
            var value = code.GetString();
            if (!string.Equals(value, "200", StringComparison.Ordinal))
                return null;
        }

        if (!root.TryGetProperty("location", out var locations) || locations.ValueKind != JsonValueKind.Array)
            return null;

        // 和风按相关度排好序，rank 数字越小越重要。这里跟接口顺序走，取第一条坐标有效的结果，不再比 rank 大小。
        foreach (var location in locations.EnumerateArray())
        {
            if (!rangeWasApplied && !CountryMatches(location, country))
                continue;
            if (!TryCoordinate(location, "lat", -90, 90, out var lat))
                continue;
            if (!TryCoordinate(location, "lon", -180, 180, out var lon))
                continue;

            return (
                Math.Round(lat, 2, MidpointRounding.AwayFromZero),
                Math.Round(lon, 2, MidpointRounding.AwayFromZero));
        }

        return null;
    }

    private static bool CountryMatches(JsonElement location, string country)
    {
        if (!location.TryGetProperty("country", out var element))
            return false;
        var actual = element.GetString();
        if (string.IsNullOrWhiteSpace(actual))
            return false;
        return actual.Contains(country, StringComparison.OrdinalIgnoreCase)
            || country.Contains(actual, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCoordinate(JsonElement location, string name, double min, double max, out double value)
    {
        value = 0;
        if (!location.TryGetProperty(name, out var element))
            return false;
        var raw = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;
        return value >= min && value <= max;
    }
}

internal static class QWeatherAlerts
{
    public static string? SelectHeadline(JsonElement root, DateTimeOffset utcNow)
    {
        if (root.TryGetProperty("metadata", out var metadata)
            && metadata.TryGetProperty("zeroResult", out var zero)
            && zero.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        if (!root.TryGetProperty("alerts", out var alerts) || alerts.ValueKind != JsonValueKind.Array)
            return null;

        string? bestText = null;
        var bestRank = -1;
        var bestIssued = DateTimeOffset.MinValue;
        foreach (var alert in alerts.EnumerateArray())
        {
            if (IsCancel(alert) || IsExpired(alert, utcNow) || !IsSevere(alert))
                continue;

            var text = Headline(alert);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var rank = SeverityRank(alert);
            var issued = Issued(alert);
            if (rank > bestRank || (rank == bestRank && issued >= bestIssued))
            {
                bestRank = rank;
                bestIssued = issued;
                bestText = text;
            }
        }

        return bestText;
    }

    private static bool IsCancel(JsonElement alert)
    {
        if (!alert.TryGetProperty("messageType", out var messageType))
            return false;
        if (!messageType.TryGetProperty("code", out var code))
            return false;
        return string.Equals(code.GetString(), "cancel", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpired(JsonElement alert, DateTimeOffset utcNow)
    {
        if (!alert.TryGetProperty("expireTime", out var expire) || expire.ValueKind != JsonValueKind.String)
            return false;
        return DateTimeOffset.TryParse(expire.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            && at <= utcNow;
    }

    /// <summary>只留 severe / extreme。蓝色和黄色预警不算特别预警。</summary>
    private static bool IsSevere(JsonElement alert)
    {
        if (!alert.TryGetProperty("severity", out var severity))
            return false;
        var value = severity.GetString();
        return string.Equals(value, "severe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "extreme", StringComparison.OrdinalIgnoreCase);
    }

    private static int SeverityRank(JsonElement alert)
    {
        var value = alert.TryGetProperty("severity", out var severity) ? severity.GetString() : null;
        if (string.Equals(value, "extreme", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(value, "severe", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 0;
    }

    private static DateTimeOffset Issued(JsonElement alert)
    {
        if (!alert.TryGetProperty("issuedTime", out var issued) || issued.ValueKind != JsonValueKind.String)
            return DateTimeOffset.MinValue;
        return DateTimeOffset.TryParse(issued.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : DateTimeOffset.MinValue;
    }

    private static string? Headline(JsonElement alert)
    {
        var text = ReadString(alert, "headline");
        if (string.IsNullOrWhiteSpace(text))
            text = ReadString(alert, "description");
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int max = 160;
        return collapsed.Length <= max ? collapsed : collapsed[..max];
    }

    private static string? ReadString(JsonElement alert, string name)
    {
        if (!alert.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return null;
        return element.GetString();
    }
}
