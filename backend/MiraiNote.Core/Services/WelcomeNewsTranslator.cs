using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services.Mirai;

namespace MiraiNote.Core.Services;

public interface IWelcomeTitleTranslator
{
    /// <summary>
    /// 把新闻标题译成中文。失败、超时或未配置 DeepSeek 时原样返回，不丢条目。
    /// </summary>
    Task<IReadOnlyList<WelcomeNewsItem>> TranslateAsync(
        IReadOnlyList<WelcomeNewsItem> items,
        CancellationToken ct = default);
}

/// <summary>
/// 用聊天同一条 DeepSeek 通道翻译欢迎语新闻标题。
/// 密钥和地址来自 <see cref="DeepSeekOptions"/>（启动时已从 AI Providers 的 deepseek 配置回填）。
/// 成功译文按标题和链接的哈希缓存，避免每次刷新欢迎语都重新请求。
/// </summary>
public sealed class DeepSeekWelcomeTitleTranslator : IWelcomeTitleTranslator
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);

    private const string SystemPrompt =
        "你只把新闻标题译成简体中文。只输出 JSON：{\"titles\":[\"...\"]}。" +
        "titles 的数量和顺序必须与输入一致。不要解释，不要编号，不要输出链接。专有名词可以保留英文。";

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly IOptions<DeepSeekOptions> _options;
    private readonly ILogger<DeepSeekWelcomeTitleTranslator> _logger;
    private readonly TimeSpan _timeout;

    public DeepSeekWelcomeTitleTranslator(
        IHttpClientFactory http,
        IMemoryCache cache,
        IOptions<DeepSeekOptions> options,
        ILogger<DeepSeekWelcomeTitleTranslator> logger)
        : this(http, cache, options, logger, DefaultTimeout)
    {
    }

    internal DeepSeekWelcomeTitleTranslator(
        IHttpClientFactory http,
        IMemoryCache cache,
        IOptions<DeepSeekOptions> options,
        ILogger<DeepSeekWelcomeTitleTranslator> logger,
        TimeSpan timeout)
    {
        _http = http;
        _cache = cache;
        _options = options;
        _logger = logger;
        _timeout = timeout > TimeSpan.Zero ? timeout : DefaultTimeout;
    }

    public async Task<IReadOnlyList<WelcomeNewsItem>> TranslateAsync(
        IReadOnlyList<WelcomeNewsItem> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return items;

        var output = new WelcomeNewsItem[items.Count];
        var pending = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            if (TryReadCache(items[i], out var cached))
                output[i] = items[i] with { Title = cached };
            else
                pending.Add(i);
        }

        if (pending.Count == 0)
            return output;

        if (!TryCredentials(out var baseUrl, out var apiKey, out var model))
        {
            foreach (var index in pending)
                output[index] = items[index];
            return output;
        }

        try
        {
            var source = pending.Select(index => items[index].Title).ToArray();
            var translated = await RequestAsync(source, baseUrl, apiKey, model, ct);
            for (var n = 0; n < pending.Count; n++)
            {
                var index = pending[n];
                var original = items[index];
                var title = n < translated.Length ? Clean(translated[n], apiKey) : null;
                if (title == null)
                {
                    output[index] = original;
                    continue;
                }

                _cache.Set(CacheKey(original.Title, original.Url), title, CacheTtl);
                output[index] = original with { Title = title };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            _logger.LogInformation("欢迎语新闻标题暂未译成中文");
            foreach (var index in pending)
                output[index] = items[index];
        }

        return output;
    }

    internal static string CacheKey(string title, string url)
    {
        var raw = url.Trim() + "\n" + title.Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "welcome:news-zh:" + Convert.ToHexString(hash);
    }

    internal static string? Clean(string? title, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var collapsed = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        collapsed = collapsed.Trim('"', '\'', '“', '”', '「', '」');
        if (collapsed.Length == 0)
            return null;
        if (!string.IsNullOrEmpty(apiKey) && collapsed.Contains(apiKey, StringComparison.Ordinal))
            return null;

        const int max = 160;
        return collapsed.Length <= max ? collapsed : collapsed[..max];
    }

    internal static string[]? ReadTitles(string content, int expected)
    {
        if (expected <= 0 || string.IsNullOrWhiteSpace(content))
            return null;

        var text = StripFence(content.Trim());
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
                array = root;
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("titles", out var titles))
                array = titles;
            else
                return null;

            if (array.ValueKind != JsonValueKind.Array)
                return null;

            var result = new string[expected];
            var index = 0;
            foreach (var item in array.EnumerateArray())
            {
                if (index >= expected)
                    break;
                result[index] = item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
                index++;
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool TryReadCache(WelcomeNewsItem item, out string title)
    {
        title = "";
        if (!_cache.TryGetValue(CacheKey(item.Title, item.Url), out string? cached) || string.IsNullOrWhiteSpace(cached))
            return false;
        title = cached;
        return true;
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

    private async Task<string[]> RequestAsync(
        IReadOnlyList<string> titles,
        string baseUrl,
        string apiKey,
        string model,
        CancellationToken ct)
    {
        using var client = DeepSeekJsonClient.CreateAuthorizedClient(_http, baseUrl, apiKey);
        var messages = new object[]
        {
            new { role = "system", content = SystemPrompt },
            new { role = "user", content = JsonSerializer.Serialize(new { titles }) }
        };
        var content = await DeepSeekJsonClient.CompleteAsync(
            client,
            model,
            messages,
            temperature: 0,
            maxTokens: 400,
            jsonObject: true,
            timeout: _timeout,
            ct,
            disableThinking: true);

        var parsed = ReadTitles(content, titles.Count);
        if (parsed == null)
            throw new JsonException("新闻标题译文无法解析");
        return parsed;
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
