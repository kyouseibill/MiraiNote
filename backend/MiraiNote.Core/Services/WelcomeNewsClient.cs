using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MiraiNote.Core.Services;

public sealed record WelcomeNewsItem(string Title, string Url);

public interface IWelcomeNewsSource
{
    /// <summary>外部失败时返回空列表，不抛给欢迎语。</summary>
    Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(CancellationToken ct = default);

    /// <summary>
    /// 按用户跳过近 7 天已展示的链接。未实现时退回不区分用户的结果。
    /// </summary>
    Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(int userId, DateTimeOffset utcNow, CancellationToken ct = default)
        => GetLatestAsync(ct);
}

/// <summary>一条公开 RSS，以及挑选时用来区分来源的短名字。</summary>
public readonly record struct WelcomeNewsSourceFeed(string Source, Uri Url);

/// <summary>
/// 合并五条公开 RSS，按链接去重后最多留 2 条，并尽量来自不同来源。只要标题和链接。
/// 候选池短时缓存；已展示链接按用户另记，不放进这条缓存。
/// </summary>
public sealed class WelcomeNewsClient : IWelcomeNewsSource
{
    public const string HttpClientName = "WelcomeNews";
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(8);
    public const int MaxItems = 2;

    public static readonly Uri OpenAiFeed = new("https://openai.com/news/rss.xml");
    public static readonly Uri DeepMindFeed = new("https://deepmind.google/blog/rss.xml");
    public static readonly Uri HuggingFaceFeed = new("https://huggingface.co/blog/feed.xml");
    public static readonly Uri TechCrunchFeed = new("https://techcrunch.com/category/artificial-intelligence/feed/");
    public static readonly Uri TheVergeFeed = new("https://www.theverge.com/rss/ai-artificial-intelligence/index.xml");

    public static IReadOnlyList<WelcomeNewsSourceFeed> Feeds { get; } =
    [
        new("openai", OpenAiFeed),
        new("deepmind", DeepMindFeed),
        new("huggingface", HuggingFaceFeed),
        new("techcrunch", TechCrunchFeed),
        new("theverge", TheVergeFeed),
    ];

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WelcomeNewsClient> _logger;
    private readonly IWelcomeTitleTranslator? _translator;
    private readonly IWelcomeNewsSeenStore? _seen;

    public WelcomeNewsClient(
        IHttpClientFactory http,
        IMemoryCache cache,
        ILogger<WelcomeNewsClient> logger,
        IWelcomeTitleTranslator? translator = null,
        IWelcomeNewsSeenStore? seen = null)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _translator = translator;
        _seen = seen;
    }

    public Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(CancellationToken ct = default) =>
        GetLatestAsync(0, DateTimeOffset.UtcNow, ct);

    public async Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(
        int userId,
        DateTimeOffset utcNow,
        CancellationToken ct = default)
    {
        var pool = await PoolAsync(ct);
        if (pool.Count == 0)
            return [];

        var seen = await RecentAsync(userId, utcNow, ct);
        var selected = WelcomeNewsMerge.Select(pool, MaxItems, seen);
        await RememberAsync(userId, utcNow, selected, ct);
        return await TranslateAsync(selected, ct);
    }

    private async Task<IReadOnlyList<WelcomeNewsCandidate>> PoolAsync(CancellationToken ct)
    {
        var pending = _cache.GetOrCreateAsync("welcome:news", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await FetchPoolAsync(CancellationToken.None);
        });
        return await pending.WaitAsync(ct) ?? [];
    }

    private async Task<IReadOnlyList<WelcomeNewsCandidate>> FetchPoolAsync(CancellationToken ct)
    {
        var batches = await Task.WhenAll(Feeds.Select(feed => ReadFeedAsync(feed.Url, feed.Source, ct)));
        return batches.SelectMany(batch => batch).ToArray();
    }

    private async Task<IReadOnlySet<string>> RecentAsync(int userId, DateTimeOffset utcNow, CancellationToken ct)
    {
        if (_seen == null || userId <= 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return await _seen.GetRecentUrlsAsync(userId, utcNow, ct)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogInformation("欢迎语新闻已读记录暂不可用");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task RememberAsync(
        int userId,
        DateTimeOffset utcNow,
        IReadOnlyList<WelcomeNewsItem> selected,
        CancellationToken ct)
    {
        if (_seen == null || userId <= 0 || selected.Count == 0)
            return;

        try
        {
            await _seen.RecordAsync(userId, selected.Select(item => item.Url).ToArray(), utcNow, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogInformation("欢迎语新闻已读记录暂不可用");
        }
    }

    private async Task<IReadOnlyList<WelcomeNewsItem>> TranslateAsync(
        IReadOnlyList<WelcomeNewsItem> items,
        CancellationToken ct)
    {
        if (_translator is null || items.Count == 0)
            return items;

        try
        {
            return await _translator.TranslateAsync(items, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogInformation("欢迎语新闻标题暂未译成中文");
            return items;
        }
    }

    private async Task<IReadOnlyList<WelcomeNewsCandidate>> ReadFeedAsync(Uri feed, string source, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient(HttpClientName);
            using var response = await client.GetAsync(feed, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var limited = new MemoryStream();
            var buffer = new byte[8192];
            var total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;
                total += read;
                if (total > 1_000_000)
                    return [];
                limited.Write(buffer, 0, read);
            }

            limited.Position = 0;
            return WelcomeNewsFeed.Read(limited, feed, source);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogInformation("欢迎语新闻暂不可用");
            return [];
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or InvalidOperationException)
        {
            _logger.LogInformation("欢迎语新闻暂不可用");
            return [];
        }
    }
}

public sealed record WelcomeNewsCandidate(string Title, string Url, DateTimeOffset Published, string Source = "");

public static class WelcomeNewsMerge
{
    public static IReadOnlyList<WelcomeNewsItem> Select(
        IEnumerable<WelcomeNewsCandidate> items,
        int maxItems,
        IReadOnlySet<string>? excludeUrls = null)
    {
        var excluded = NormalizeExcluded(excludeUrls);
        var best = new Dictionary<string, WelcomeNewsCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || !Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
                continue;
            if (uri.Scheme is not ("https" or "http"))
                continue;

            var key = DedupeKey(uri);
            if (excluded.Contains(key))
                continue;
            if (!best.TryGetValue(key, out var existing) || item.Published > existing.Published)
                best[key] = item with { Title = item.Title.Trim(), Url = key };
        }

        var ranked = best.Values
            .OrderByDescending(item => item.Published)
            .ThenBy(item => item.Title, StringComparer.Ordinal)
            .ToArray();

        return TakeDiverse(ranked, Math.Max(0, maxItems))
            .Select(item => new WelcomeNewsItem(item.Title, item.Url))
            .ToArray();
    }

    /// <summary>
    /// 先按时间从新到旧各取一个来源；名额还没满，再回头补同一来源的下一条。
    /// </summary>
    private static IReadOnlyList<WelcomeNewsCandidate> TakeDiverse(IReadOnlyList<WelcomeNewsCandidate> ranked, int maxItems)
    {
        if (maxItems <= 0 || ranked.Count == 0)
            return [];

        var picked = new List<WelcomeNewsCandidate>(Math.Min(maxItems, ranked.Count));
        var pickedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ranked)
        {
            if (picked.Count >= maxItems)
                break;
            if (!usedSources.Add(SourceKey(item)))
                continue;
            picked.Add(item);
            pickedUrls.Add(item.Url);
        }

        if (picked.Count < maxItems)
        {
            foreach (var item in ranked)
            {
                if (picked.Count >= maxItems)
                    break;
                if (!pickedUrls.Add(item.Url))
                    continue;
                picked.Add(item);
            }
        }

        return picked;
    }

    private static string SourceKey(WelcomeNewsCandidate item) =>
        string.IsNullOrWhiteSpace(item.Source) ? "" : item.Source.Trim();

    private static HashSet<string> NormalizeExcluded(IReadOnlySet<string>? excludeUrls)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (excludeUrls == null)
            return excluded;

        foreach (var raw in excludeUrls)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                excluded.Add(DedupeKey(uri));
            else
                excluded.Add(raw.Trim());
        }

        return excluded;
    }

    internal static string DedupeKey(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = "",
            Host = uri.Host.ToLowerInvariant(),
            Scheme = uri.Scheme.ToLowerInvariant()
        };
        if (builder.Path.Length > 1)
            builder.Path = builder.Path.TrimEnd('/');
        return builder.Uri.AbsoluteUri;
    }
}

internal static class WelcomeNewsFeed
{
    public static IReadOnlyList<WelcomeNewsCandidate> Read(Stream xml, Uri feed, string source = "")
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        using var reader = XmlReader.Create(xml, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        if (document.Root == null)
            return [];

        var entries = document.Descendants().Where(element =>
            element.Name.LocalName is "item" or "entry");

        var items = new List<WelcomeNewsCandidate>();
        foreach (var entry in entries.Take(30))
        {
            var title = Text(entry, "title");
            var link = Link(entry, feed);
            if (string.IsNullOrWhiteSpace(title) || link == null)
                continue;
            items.Add(new WelcomeNewsCandidate(Collapse(title), link, Published(entry), source));
        }

        return items;
    }

    private static string? Link(XElement entry, Uri feed)
    {
        foreach (var element in entry.Elements().Where(element => element.Name.LocalName == "link"))
        {
            var rel = (string?)element.Attribute("rel");
            if (rel != null && !rel.Equals("alternate", StringComparison.OrdinalIgnoreCase))
                continue;

            var raw = element.Attribute("href")?.Value;
            if (string.IsNullOrWhiteSpace(raw))
                raw = element.Value;
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (!Uri.TryCreate(feed, raw.Trim(), out var absolute))
                continue;
            if (absolute.Scheme is not ("https" or "http"))
                continue;
            return WelcomeNewsMerge.DedupeKey(absolute);
        }

        return null;
    }

    private static DateTimeOffset Published(XElement entry)
    {
        foreach (var name in new[] { "pubDate", "published", "updated", "date" })
        {
            var text = Text(entry, name);
            if (string.IsNullOrWhiteSpace(text))
                continue;
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                return parsed;
        }

        return DateTimeOffset.MinValue;
    }

    private static string? Text(XElement entry, string localName)
    {
        return entry.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;
    }

    private static string Collapse(string value)
    {
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
