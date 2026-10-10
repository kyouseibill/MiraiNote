using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services.Mirai;

namespace MiraiNote.Core.Services;

/// <summary>
/// 写一句小字时用到的上下文。没有用户 Id：同一城市、天气和钟点的人共用一句。
/// </summary>
public sealed record WelcomeInspirationContext(
    string? Place,
    string? WeatherText,
    string? WeatherTemp,
    DateTime ShanghaiWall);

public interface IWelcomeInspirationSource
{
    /// <summary>
    /// 一句 1–40 字的中文。超时、失败、空或超长时返回 null，调用方不要再等。
    /// </summary>
    Task<string?> GetLineAsync(WelcomeInspirationContext context, CancellationToken ct = default);
}

/// <summary>
/// 用欢迎语同一套 DeepSeek 凭据写一句小字。
/// 缓存走 <see cref="IDistributedCache"/>（当前是内存实现，不接 Redis）。
/// 键是城市、实况、气温档和上海钟点。进程重启后缓存可以丢。
/// </summary>
public sealed class DeepSeekWelcomeInspiration : IWelcomeInspirationSource
{
    public const string None = "_none";
    public const int MaxChars = 40;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);

    public const string SystemPrompt =
        "你只写一句简体中文，20到40个字。" +
        "不要作者，不要出处，不要书名号，不要把两句叠在一起，不要恐吓，不要政治，不要广告，不要链接。" +
        "不要假托任何具体的古典诗词、诗人或篇名。" +
        "可以轻轻带一点季节或天气的心情，但不要在同一句里既写钟点又写具体气温。" +
        "不要用城市名当主语开头。" +
        "只输出这一句话本身，不要解释，不要引号。";

    private readonly IHttpClientFactory _http;
    private readonly IDistributedCache _cache;
    private readonly IOptions<DeepSeekOptions> _options;
    private readonly ILogger<DeepSeekWelcomeInspiration> _logger;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    public DeepSeekWelcomeInspiration(
        IHttpClientFactory http,
        IDistributedCache cache,
        IOptions<DeepSeekOptions> options,
        ILogger<DeepSeekWelcomeInspiration> logger)
        : this(http, cache, options, logger, DefaultTimeout)
    {
    }

    internal DeepSeekWelcomeInspiration(
        IHttpClientFactory http,
        IDistributedCache cache,
        IOptions<DeepSeekOptions> options,
        ILogger<DeepSeekWelcomeInspiration> logger,
        TimeSpan timeout)
    {
        _http = http;
        _cache = cache;
        _options = options;
        _logger = logger;
        _timeout = timeout > TimeSpan.Zero ? timeout : DefaultTimeout;
    }

    public async Task<string?> GetLineAsync(WelcomeInspirationContext context, CancellationToken ct = default)
    {
        var key = CacheKey(context);
        var cached = await TryReadAsync(key, context.Place, ct);
        if (cached != null)
            return cached;

        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            cached = await TryReadAsync(key, context.Place, ct);
            if (cached != null)
                return cached;

            var line = await GenerateAsync(context, ct);
            if (line != null)
            {
                await _cache.SetStringAsync(
                    key,
                    line,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                    ct);
            }

            return line;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 城市（拆不出就是 <c>_none</c>）+ 实况 + 气温档（没有则 <c>_notemp</c>）+ 上海钟点。
    /// 没有城市时不把天气写进键，避免同一小时被实况拆开。
    /// </summary>
    public static string CacheKey(WelcomeInspirationContext context)
    {
        var city = CityToken(context.Place);
        var brief = city == None ? None : BriefToken(context.WeatherText);
        var band = city == None ? "_notemp" : TempBandToken(context.WeatherTemp);
        return $"welcome:inspiration:{city}\u001f{brief}\u001f{band}\u001f{context.ShanghaiWall.Hour:00}";
    }

    public static string CityToken(string? place)
    {
        if (!WelcomePlace.TrySplit(place, out _, out var city))
            return None;
        var token = Collapse(city).ToLowerInvariant();
        return token.Length == 0 ? None : token;
    }

    public static string BriefToken(string? weatherText)
    {
        var token = Collapse(weatherText);
        if (token.Length == 0)
            return None;
        if (token.Length > 40)
            token = token[..40];
        return token;
    }

    /// <summary>5°C 一档，向下取整。24 和 20 同档，25 进下一档。解析不了返回 null。</summary>
    public static int? TempBand(string? temp)
    {
        var text = QWeatherNow.NormalizeTemp(temp);
        if (text == null || !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return null;
        return (int)(Math.Floor(value / 5m) * 5m);
    }

    public static string UserPrompt(WelcomeInspirationContext context)
    {
        var season = SeasonLabel(DateOnly.FromDateTime(context.ShanghaiWall));
        var hour = $"{context.ShanghaiWall.Hour}点";
        if (!WelcomePlace.TrySplit(context.Place, out var country, out var city))
        {
            return $"季节：{season}\n本地钟点：{hour}\n请写一句鸡汤、短诗或格言。不要出现地点，也不要写天气。不要在同一句里既写钟点又写具体气温。";
        }

        var lines = new List<string>
        {
            $"地点：{country}-{city}",
            $"季节：{season}",
            $"本地钟点：{hour}",
        };
        var brief = BriefToken(context.WeatherText);
        if (brief != None)
            lines.Add("天气：" + brief);
        var temp = QWeatherNow.NormalizeTemp(context.WeatherTemp);
        if (temp != null)
            lines.Add($"气温：{temp}°C");
        lines.Add(
            "可以轻轻带一点季节或天气的心情，不要写成天气预报。" +
            "不要在同一句里既写钟点又写具体气温。" +
            $"不要用城市名当主语开头，不要写成「{city}的」。" +
            "不要反复点地名。");
        return string.Join('\n', lines);
    }

    public static string SeasonLabel(DateOnly shanghaiDate) => WelcomeGreetingService.SeasonOf(shanghaiDate) switch
    {
        WelcomeGreetingCopy.Spring => "春",
        WelcomeGreetingCopy.Summer => "夏",
        WelcomeGreetingCopy.Autumn => "秋",
        _ => "冬",
    };

    /// <summary>
    /// 空、超过 40 字、叠句、书名号、链接都丢掉。40 字整仍然留下。
    /// 同一句里既有钟点又有具体气温，或用「{城市}的」开头，也丢掉。
    /// </summary>
    public static string? Accept(string? raw, string? apiKey, string? place = null)
    {
        var text = NormalizeLine(raw);
        if (text == null || text.Length > MaxChars)
            return null;
        if (!ContainsCjk(text))
            return null;
        if (text.Contains('《') || text.Contains('》') || text.Contains('—'))
            return null;
        if (text.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || text.Contains("https://", StringComparison.OrdinalIgnoreCase)
            || text.Contains("www.", StringComparison.OrdinalIgnoreCase))
            return null;
        if (text.Count(ch => ch is '。' or '！' or '？' or '!' or '?') > 1)
            return null;
        if (LooksLikeForecast(text) || StartsWithCitySubject(text, place))
            return null;
        if (!string.IsNullOrEmpty(apiKey) && text.Contains(apiKey, StringComparison.Ordinal))
            return null;
        return text;
    }

    private async Task<string?> TryReadAsync(string key, string? place, CancellationToken ct)
    {
        var cached = await _cache.GetStringAsync(key, ct);
        return Accept(cached, _options.Value.ApiKey, place);
    }

    private async Task<string?> GenerateAsync(WelcomeInspirationContext context, CancellationToken ct)
    {
        if (!TryCredentials(out var baseUrl, out var apiKey, out var model))
            return null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);
        try
        {
            using var client = DeepSeekJsonClient.CreateAuthorizedClient(_http, baseUrl, apiKey);
            var messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = UserPrompt(context) },
            };
            var content = await CompleteAsync(client, model, messages, timeoutCts.Token);
            var line = Accept(content, apiKey, context.Place);
            if (line != null || !ShouldRetry(content, context.Place) || timeoutCts.IsCancellationRequested)
                return line;

            _logger.LogInformation("欢迎语小句像天气预报或用城市起句，再要一次");
            content = await CompleteAsync(client, model, messages, timeoutCts.Token);
            return Accept(content, apiKey, context.Place);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("欢迎语小句超时，页面只留问候");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogInformation("欢迎语小句暂未生成（{ExceptionType}）", ex.GetType().Name);
            return null;
        }
    }

    private bool TryCredentials(out string baseUrl, out string apiKey, out string model)
    {
        var options = _options.Value;
        apiKey = options.ApiKey?.Trim() ?? "";
        baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://api.deepseek.com" : options.BaseUrl.Trim();
        model = string.IsNullOrWhiteSpace(options.Model) ? "deepseek-v4-flash" : options.Model.Trim();
        if (apiKey.Length == 0)
            return false;
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http";
    }

    private Task<string> CompleteAsync(HttpClient client, string model, object[] messages, CancellationToken ct) =>
        DeepSeekJsonClient.CompleteAsync(
            client,
            model,
            messages,
            temperature: 0.7,
            maxTokens: 80,
            jsonObject: false,
            timeout: _timeout,
            ct,
            disableThinking: true);

    /// <summary>像预报或用城市当主语时再要一次。其余不合格直接作废，避免空句和超长句多打一趟。</summary>
    private static bool ShouldRetry(string? raw, string? place)
    {
        var text = NormalizeLine(raw);
        return text != null && (LooksLikeForecast(text) || StartsWithCitySubject(text, place));
    }

    private static string? NormalizeLine(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = StripFence(raw.Trim());
        if (text.Contains('\n') || text.Contains('\r'))
            return null;

        text = text.Trim().Trim('"', '\'', '“', '”', '「', '」', '『', '』').Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>同一句里既有时段或钟点，又有具体气温。「慢一点」里的「一点」不算钟点。</summary>
    private static bool LooksLikeForecast(string text) =>
        HasTimeOfDay(text) && HasNumericTemperature(text);

    private static bool StartsWithCitySubject(string text, string? place)
    {
        if (!WelcomePlace.TrySplit(place, out _, out var city))
            return false;
        return city.Length > 0 && text.StartsWith(city + "的", StringComparison.Ordinal);
    }

    private static bool HasTimeOfDay(string text) => TimeOfDay.IsMatch(text);

    private static bool HasNumericTemperature(string text)
    {
        if (ArabicTemperature.IsMatch(text) || TemperatureReading.IsMatch(text))
            return true;

        foreach (Match match in ChineseTemperature.Matches(text))
        {
            if (match.Value is "一度" or "两度" && match.Index > 0 && text[match.Index - 1] == '年')
                continue;
            return true;
        }

        return false;
    }

    private static readonly Regex TimeOfDay = new(
        @"凌晨|清晨|早晨|早上|上午|中午|午后|下午|傍晚|黄昏|晚上|夜晚|夜里|夜间|深夜|半夜|(?:[01]?\d|2[0-3])\s*[:：]\s*[0-5]\d|(?:[01]?\d|2[0-3]|二十[一二三四]?|十[一二]|[二三四五六七八九十两])\s*[点时]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ArabicTemperature = new(
        @"-?\d+(?:\.\d+)?\s*(?:摄氏)?\s*(?:度|℃|°\s*[CcＣ])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ChineseTemperature = new(
        @"[零〇一二三四五六七八九十百两]{1,6}\s*(?:摄氏)?\s*度",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TemperatureReading = new(
        @"(?:气温|温度)\s*(?:零下|负)?\s*(?:-?\d+(?:\.\d+)?|[零〇一二三四五六七八九十百两]{2,6})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string TempBandToken(string? temp)
    {
        var band = TempBand(temp);
        return band == null ? "_notemp" : "t" + band.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Replace('\u001f', ' ');
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            if (ch is >= '\u4E00' and <= '\u9FFF')
                return true;
        }

        return false;
    }

    private static string StripFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        var firstLine = text.IndexOf('\n');
        if (firstLine < 0)
            return text;
        var body = text[(firstLine + 1)..];
        var end = body.LastIndexOf("```", StringComparison.Ordinal);
        return end >= 0 ? body[..end].Trim() : body.Trim();
    }
}
