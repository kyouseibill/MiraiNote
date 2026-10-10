using System.Collections.Concurrent;
using System.Globalization;
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
        var cached = await TryReadAsync(key, ct);
        if (cached != null)
            return cached;

        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            cached = await TryReadAsync(key, ct);
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
            return $"季节：{season}\n本地钟点：{hour}\n请写一句鸡汤、短诗或格言。不要出现地点，也不要写天气。";
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
        lines.Add("可以轻轻带一点季节或天气，不要写成天气预报，也不要反复点地名。");
        return string.Join('\n', lines);
    }

    public static string SeasonLabel(DateOnly shanghaiDate) => WelcomeGreetingService.SeasonOf(shanghaiDate) switch
    {
        WelcomeGreetingCopy.Spring => "春",
        WelcomeGreetingCopy.Summer => "夏",
        WelcomeGreetingCopy.Autumn => "秋",
        _ => "冬",
    };

    /// <summary>空、超过 40 字、叠句、书名号、链接都丢掉。40 字整仍然留下。</summary>
    public static string? Accept(string? raw, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = StripFence(raw.Trim());
        if (text.Contains('\n') || text.Contains('\r'))
            return null;

        text = text.Trim().Trim('"', '\'', '“', '”', '「', '」', '『', '』').Trim();
        if (text.Length is 0 or > MaxChars)
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
        if (!string.IsNullOrEmpty(apiKey) && text.Contains(apiKey, StringComparison.Ordinal))
            return null;
        return text;
    }

    private async Task<string?> TryReadAsync(string key, CancellationToken ct)
    {
        var cached = await _cache.GetStringAsync(key, ct);
        return Accept(cached, _options.Value.ApiKey);
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
            var content = await DeepSeekJsonClient.CompleteAsync(
                client,
                model,
                messages,
                temperature: 0.7,
                maxTokens: 80,
                jsonObject: false,
                timeout: _timeout,
                timeoutCts.Token,
                disableThinking: true);
            return Accept(content, apiKey);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("欢迎语小句超时，改由页面本地句顶上");
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
