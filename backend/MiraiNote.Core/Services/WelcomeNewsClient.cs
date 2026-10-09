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
}

/// <summary>
/// 合并两条公开 RSS，按链接去重后只留最新的 2 条。只要标题和链接。
/// </summary>
public sealed class WelcomeNewsClient : IWelcomeNewsSource
{
    public const string HttpClientName = "WelcomeNews";
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(8);
    public const int MaxItems = 2;

    public static readonly Uri OpenAiFeed = new("https://openai.com/news/rss.xml");
    public static readonly Uri TechCrunchFeed = new("https://techcrunch.com/category/artificial-intelligence/feed/");

    public static IReadOnlyList<Uri> Feeds { get; } = [OpenAiFeed, TechCrunchFeed];

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WelcomeNewsClient> _logger;
    private readonly IWelcomeTitleTranslator? _translator;

    public WelcomeNewsClient(
        IHttpClientFactory http,
        IMemoryCache cache,
        ILogger<WelcomeNewsClient> logger,
        IWelcomeTitleTranslator? translator = null)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _translator = translator;
    }

    public async Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(CancellationToken ct = default)
    {
        var pending = _cache.GetOrCreateAsync("welcome:news", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await FetchAsync(CancellationToken.None);
        });
        var items = await pending.WaitAsync(ct) ?? [];
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

    private async Task<IReadOnlyList<WelcomeNewsItem>> FetchAsync(CancellationToken ct)
    {
        var batches = await Task.WhenAll(Feeds.Select(feed => ReadFeedAsync(feed, ct)));
        return WelcomeNewsMerge.Select(batches.SelectMany(batch => batch), MaxItems);
    }

    private async Task<IReadOnlyList<WelcomeNewsCandidate>> ReadFeedAsync(Uri feed, CancellationToken ct)
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
            return WelcomeNewsFeed.Read(limited, feed);
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

public sealed record WelcomeNewsCandidate(string Title, string Url, DateTimeOffset Published);

public static class WelcomeNewsMerge
{
    public static IReadOnlyList<WelcomeNewsItem> Select(IEnumerable<WelcomeNewsCandidate> items, int maxItems)
    {
        var best = new Dictionary<string, WelcomeNewsCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || !Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
                continue;
            if (uri.Scheme is not ("https" or "http"))
                continue;

            var key = DedupeKey(uri);
            if (!best.TryGetValue(key, out var existing) || item.Published > existing.Published)
                best[key] = item with { Title = item.Title.Trim(), Url = key };
        }

        return best.Values
            .OrderByDescending(item => item.Published)
            .ThenBy(item => item.Title, StringComparer.Ordinal)
            .Take(Math.Max(0, maxItems))
            .Select(item => new WelcomeNewsItem(item.Title, item.Url))
            .ToArray();
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
    public static IReadOnlyList<WelcomeNewsCandidate> Read(Stream xml, Uri feed)
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
            items.Add(new WelcomeNewsCandidate(Collapse(title), link, Published(entry)));
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
