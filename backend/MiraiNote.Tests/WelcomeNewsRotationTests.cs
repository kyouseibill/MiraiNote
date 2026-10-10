using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MiraiNote.Core.Services;
using MiraiNote.Data.Entities;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class WelcomeNewsRotationTests : IDisposable
{
    private static readonly DateTimeOffset When = new(2026, 10, 9, 2, 0, 0, TimeSpan.Zero);

    private readonly MiraiTestFixture _fx = new();

    [Fact]
    public void Feeds_UseTheFivePublicSources()
    {
        Assert.Equal(
            ["openai", "deepmind", "huggingface", "techcrunch", "theverge"],
            WelcomeNewsClient.Feeds.Select(feed => feed.Source).ToArray());
        Assert.Equal("https://openai.com/news/rss.xml", WelcomeNewsClient.OpenAiFeed.AbsoluteUri);
        Assert.Equal("https://deepmind.google/blog/rss.xml", WelcomeNewsClient.DeepMindFeed.AbsoluteUri);
        Assert.Equal("https://huggingface.co/blog/feed.xml", WelcomeNewsClient.HuggingFaceFeed.AbsoluteUri);
        Assert.Equal("https://techcrunch.com/category/artificial-intelligence/feed/", WelcomeNewsClient.TechCrunchFeed.AbsoluteUri);
        Assert.Equal("https://www.theverge.com/rss/ai-artificial-intelligence/index.xml", WelcomeNewsClient.TheVergeFeed.AbsoluteUri);
    }

    [Fact]
    public void Select_PrefersDifferentSources_AndFillsFromTheSameSourceWhenNeeded()
    {
        var newest = At(9, 3);
        var next = At(9, 2);
        var older = At(7, 0);
        var selected = WelcomeNewsMerge.Select(
        [
            new("OpenAI 最新", "https://openai.com/news/newest", newest, "openai"),
            new("OpenAI 次新", "https://openai.com/news/next", next, "openai"),
            new("DeepMind 一条", "https://deepmind.google/blog/one/", older, "deepmind"),
            new("坏链接", "javascript:alert(1)", newest, "theverge"),
            new("", "https://huggingface.co/blog/blank", newest, "huggingface")
        ], 2);

        Assert.Equal(["OpenAI 最新", "DeepMind 一条"], selected.Select(item => item.Title).ToArray());
        Assert.Equal("https://deepmind.google/blog/one", selected[1].Url);

        var sameSource = WelcomeNewsMerge.Select(
        [
            new("OpenAI 最新", "https://openai.com/news/newest", newest, "openai"),
            new("OpenAI 次新", "https://openai.com/news/next", next, "openai")
        ], 2);
        Assert.Equal(["OpenAI 最新", "OpenAI 次新"], sameSource.Select(item => item.Title).ToArray());
    }

    [Fact]
    public void Select_SkipsExcludedLinks_AndCanReturnFewerThanTwo()
    {
        var pool = Pool();
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "https://openai.com/news/newest/",
            "https://deepmind.google/blog/one"
        };

        var selected = WelcomeNewsMerge.Select(pool, 2, hidden);
        Assert.Equal(["OpenAI 次新", "Hugging Face 一条"], selected.Select(item => item.Title).ToArray());

        var onlyOne = WelcomeNewsMerge.Select(pool, 2, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "https://openai.com/news/newest",
            "https://openai.com/news/next",
            "https://deepmind.google/blog/one",
            "https://www.theverge.com/ai/one",
            "https://techcrunch.com/2026/10/07/one"
        });
        var left = Assert.Single(onlyOne);
        Assert.Equal("Hugging Face 一条", left.Title);
    }

    [Fact]
    public void Select_PrefersUnseen_WhenAnyCandidateRemains()
    {
        var pool = Pool();
        var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "https://openai.com/news/newest",
            "https://openai.com/news/next",
            "https://deepmind.google/blog/one/",
            "https://www.theverge.com/ai/one"
        };
        var seenAt = exclude.ToDictionary(
            url => url,
            _ => When.AddDays(-6).UtcDateTime,
            StringComparer.OrdinalIgnoreCase);

        var selected = WelcomeNewsMerge.Select(pool, 2, exclude, seenAt);

        Assert.Equal(["Hugging Face 一条", "TechCrunch 一条"], selected.Select(item => item.Title).ToArray());
    }

    [Fact]
    public void Select_WhenEveryCandidateWasSeen_ReusesTheOldestSeen()
    {
        var pool = Pool();
        var seenAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://openai.com/news/newest/"] = When.AddHours(-1).UtcDateTime,
            ["https://openai.com/news/next"] = When.AddDays(-2).UtcDateTime,
            ["https://deepmind.google/blog/one"] = When.AddDays(-3).UtcDateTime,
            ["https://huggingface.co/blog/one"] = When.AddDays(-4).UtcDateTime,
            ["https://techcrunch.com/2026/10/07/one"] = When.AddDays(-5).UtcDateTime,
            ["https://www.theverge.com/ai/one"] = When.AddDays(-6).UtcDateTime
        };

        var selected = WelcomeNewsMerge.Select(
            pool,
            2,
            seenAt.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            seenAt);

        Assert.Equal(["The Verge 一条", "TechCrunch 一条"], selected.Select(item => item.Title).ToArray());

        var withoutTimes = WelcomeNewsMerge.Select(
            pool,
            2,
            pool.Select(item => item.Url).ToHashSet(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["OpenAI 最新", "DeepMind 一条"], withoutTimes.Select(item => item.Title).ToArray());
    }

    [Fact]
    public async Task Client_PicksTwoItemsFromDifferentSources()
    {
        var handler = new RecordingHandler(FiveFeeds);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var news = Client(handler, cache);

        var first = await news.GetLatestAsync(7, When);
        var second = await news.GetLatestAsync(7, When);

        Assert.Equal(
            ["OpenAI 最新", "DeepMind 一条"],
            first.Select(item => item.Title).ToArray());
        Assert.Equal(first.Select(item => item.Url), second.Select(item => item.Url));
        Assert.Equal(
            WelcomeNewsClient.Feeds.Select(feed => feed.Url.AbsoluteUri).OrderBy(item => item),
            handler.Calls.Select(call => call.AbsoluteUri).OrderBy(item => item));
    }

    [Fact]
    public async Task Client_SkipsLinksShownInTheLastSevenDays_AndRecordsThem()
    {
        var handler = new RecordingHandler(FiveFeeds);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var db = _fx.CreateContext();
        var userId = await ResetSeenAsync(db);
        var otherId = await AddUserAsync(db, "other", "other-news@example.com");
        var news = Client(handler, cache, new WelcomeNewsSeenStore(db));

        var first = await news.GetLatestAsync(userId, When);
        var second = await news.GetLatestAsync(userId, When);
        var other = await news.GetLatestAsync(otherId, When);

        Assert.Equal(["OpenAI 最新", "DeepMind 一条"], first.Select(item => item.Title).ToArray());
        Assert.Equal(["OpenAI 次新", "Hugging Face 一条"], second.Select(item => item.Title).ToArray());
        Assert.Equal(first.Select(item => item.Url), other.Select(item => item.Url));
        Assert.Equal(5, handler.Calls.Count);

        var shown = await db.WelcomeNewsSeens.AsNoTracking()
            .Where(row => row.UserId == userId)
            .OrderBy(row => row.Url)
            .Select(row => row.Url)
            .ToListAsync();
        Assert.Equal(
            new[]
            {
                "https://deepmind.google/blog/one",
                "https://huggingface.co/blog/one",
                "https://openai.com/news/newest",
                "https://openai.com/news/next"
            },
            shown);
        Assert.Equal(2, await db.WelcomeNewsSeens.CountAsync(row => row.UserId == otherId));
    }

    [Fact]
    public async Task SeenStore_RecordThenRead_UsesTheSameDedupeKey()
    {
        await using var write = _fx.CreateContext();
        var userId = await ResetSeenAsync(write);
        await new WelcomeNewsSeenStore(write).RecordAsync(
            userId,
            [
                "https://www.TheVerge.com/ai/one/",
                "https://techcrunch.com/2026/10/07/one#section"
            ],
            When);

        await using var read = _fx.CreateContext();
        var recent = await new WelcomeNewsSeenStore(read).GetRecentUrlsAsync(userId, When);

        Assert.Equal(2, recent.Count);
        Assert.Equal(When.UtcDateTime, recent["https://www.theverge.com/ai/one"]);
        Assert.Equal(When.UtcDateTime, recent["https://techcrunch.com/2026/10/07/one"]);
    }

    [Fact]
    public async Task SeenStoreFailure_StaysFailSoft_AndLogsWarningWithExceptionType()
    {
        var logger = new ListLogger<WelcomeNewsClient>();
        var news = Client(
            new RecordingHandler(FiveFeeds),
            new MemoryCache(new MemoryCacheOptions()),
            new BrokenSeen(),
            logger: logger);

        var items = await news.GetLatestAsync(4, When);

        Assert.Equal(["OpenAI 最新", "DeepMind 一条"], items.Select(item => item.Title).ToArray());
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, entry =>
        {
            Assert.Contains(nameof(InvalidOperationException), entry.Message);
            Assert.IsType<InvalidOperationException>(entry.Exception);
        });
    }

    [Fact]
    public async Task SeenLinks_OlderThanSevenDays_CanShowAgain_AndArePruned()
    {
        await using var db = _fx.CreateContext();
        var userId = await ResetSeenAsync(db);
        db.WelcomeNewsSeens.AddRange(
            Seen(userId, "https://openai.com/news/newest", When.AddDays(-7).AddSeconds(-1)),
            Seen(userId, "https://deepmind.google/blog/one", When.AddDays(-1)),
            Seen(userId, "https://openai.com/news/next", When.AddDays(-7)));
        await db.SaveChangesAsync();

        var handler = new RecordingHandler(FiveFeeds);
        var news = Client(handler, new MemoryCache(new MemoryCacheOptions()), new WelcomeNewsSeenStore(db));
        var selected = await news.GetLatestAsync(userId, When);

        Assert.Equal(["OpenAI 最新", "Hugging Face 一条"], selected.Select(item => item.Title).ToArray());
        var cutoff = WelcomeNewsSeenStore.Cutoff(When);
        var rows = await db.WelcomeNewsSeens.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new { row.Url, row.ShownAt })
            .ToListAsync();
        Assert.Contains(rows, row => row.Url == "https://openai.com/news/newest" && row.ShownAt >= cutoff);
        Assert.Contains(rows, row => row.Url == "https://huggingface.co/blog/one");
        Assert.Contains(rows, row => row.Url == "https://deepmind.google/blog/one");
        Assert.Contains(rows, row => row.Url == "https://openai.com/news/next");
        Assert.DoesNotContain(rows, row => row.ShownAt < cutoff);
    }

    [Fact]
    public async Task Client_WhenEveryPoolLinkWasSeen_ReusesTheOldestAndRecordsIt()
    {
        await using var db = _fx.CreateContext();
        var userId = await ResetSeenAsync(db);
        db.WelcomeNewsSeens.AddRange(
            Seen(userId, "https://www.theverge.com/ai/one", When.AddDays(-6)),
            Seen(userId, "https://techcrunch.com/2026/10/07/one", When.AddDays(-5)),
            Seen(userId, "https://huggingface.co/blog/one", When.AddDays(-4)),
            Seen(userId, "https://deepmind.google/blog/one", When.AddDays(-3)),
            Seen(userId, "https://openai.com/news/next", When.AddDays(-2)),
            Seen(userId, "https://openai.com/news/newest", When.AddDays(-1)));
        await db.SaveChangesAsync();

        var news = Client(new RecordingHandler(FiveFeeds), new MemoryCache(new MemoryCacheOptions()), new WelcomeNewsSeenStore(db));
        var first = await news.GetLatestAsync(userId, When);
        var second = await news.GetLatestAsync(userId, When);

        Assert.Equal(["The Verge 一条", "TechCrunch 一条"], first.Select(item => item.Title).ToArray());
        Assert.Equal(["Hugging Face 一条", "DeepMind 一条"], second.Select(item => item.Title).ToArray());
        var verge = await db.WelcomeNewsSeens.AsNoTracking()
            .SingleAsync(row => row.UserId == userId && row.Url == "https://www.theverge.com/ai/one");
        Assert.Equal(When.UtcDateTime, verge.ShownAt);
    }

    [Fact]
    public void FeedRead_KeepsCompleteItems_WhenXmlIsTruncated()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel>
            {Item("还在", "https://openai.com/news/kept", "Fri, 09 Oct 2026 04:00:00 GMT")}
            <item><title>被截断
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        var items = WelcomeNewsFeed.Read(stream, WelcomeNewsClient.OpenAiFeed, "openai");

        var only = Assert.Single(items);
        Assert.Equal("还在", only.Title);
        Assert.Equal("https://openai.com/news/kept", only.Url);
        Assert.Equal("openai", only.Source);
    }

    [Fact]
    public async Task OversizedFeed_KeepsItemsInsideTheCap_AndDoesNotDropOtherSources()
    {
        var openai = OversizedOpenAi();
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "openai.com")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(openai)
                });
            }

            return Task.FromResult(FiveFeeds(request, _));
        });
        var news = Client(handler, new MemoryCache(new MemoryCacheOptions()));

        var items = await news.GetLatestAsync(4, When);

        Assert.Equal(["OpenAI 仍在", "DeepMind 一条"], items.Select(item => item.Title).ToArray());
        Assert.Equal(WelcomeNewsClient.MaxFeedBytes + 1, openai.ReadBytes);
        Assert.True(openai.ReadBytes < openai.TotalBytes);

        var garbage = new CountingStream(new byte[WelcomeNewsClient.MaxFeedBytes + 64]);
        var others = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "openai.com")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(garbage)
                });
            }

            return Task.FromResult(FiveFeeds(request, _));
        });
        var stillThere = await Client(others, new MemoryCache(new MemoryCacheOptions())).GetLatestAsync(4, When);

        Assert.Equal(["DeepMind 一条", "Hugging Face 一条"], stillThere.Select(item => item.Title).ToArray());
        Assert.Equal(WelcomeNewsClient.MaxFeedBytes + 1, garbage.ReadBytes);
    }

    [Fact]
    public async Task FeedFailureAndTimeout_OmitNewsWithoutThrowing()
    {
        var failed = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.Host == "huggingface.co")
                return Task.FromResult(Xml(Item("Hugging Face 一条", "https://huggingface.co/blog/one", "Wed, 07 Oct 2026 00:00:00 GMT")));
            if (request.RequestUri.Host == "www.theverge.com")
                throw new HttpRequestException("连接被重置");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.GatewayTimeout));
        });
        var one = await Client(failed, new MemoryCache(new MemoryCacheOptions())).GetLatestAsync(4, When);
        var only = Assert.Single(one);
        Assert.Equal("Hugging Face 一条", only.Title);
        Assert.Equal("https://huggingface.co/blog/one", only.Url);

        var hanging = new RecordingHandler((_, ct) =>
            Task.Delay(Timeout.Infinite, ct).ContinueWith(
                _ => new HttpResponseMessage(HttpStatusCode.OK),
                ct,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default));
        var none = await Client(hanging, new MemoryCache(new MemoryCacheOptions()), timeout: TimeSpan.FromMilliseconds(200))
            .GetLatestAsync();
        Assert.Empty(none);

        var brokenStore = Client(new RecordingHandler(FiveFeeds), new MemoryCache(new MemoryCacheOptions()), new BrokenSeen());
        var stillThere = await brokenStore.GetLatestAsync(4, When);
        Assert.Equal(["OpenAI 最新", "DeepMind 一条"], stillThere.Select(item => item.Title).ToArray());
    }

    [Fact]
    public async Task Translation_StillAppliesToThePickedPair_AndKeepsEnglishOnFailure()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var translator = new PrefixTranslator();
        var news = Client(new RecordingHandler(FiveFeeds), cache, translator: translator);

        var translated = await news.GetLatestAsync();
        var again = await news.GetLatestAsync();

        Assert.Equal(["译:OpenAI 最新", "译:DeepMind 一条"], translated.Select(item => item.Title).ToArray());
        Assert.Equal(["https://openai.com/news/newest", "https://deepmind.google/blog/one"], translated.Select(item => item.Url).ToArray());
        Assert.Equal(["OpenAI 最新", "DeepMind 一条"], translator.SeenTitles);
        Assert.Equal(2, translator.Calls);
        Assert.Equal(translated.Select(item => item.Title), again.Select(item => item.Title));

        var failed = Client(
            new RecordingHandler(FiveFeeds),
            new MemoryCache(new MemoryCacheOptions()),
            translator: new ThrowingTranslator());
        var english = await failed.GetLatestAsync();
        Assert.Equal(["OpenAI 最新", "DeepMind 一条"], english.Select(item => item.Title).ToArray());
        Assert.Equal("https://openai.com/news/newest", english[0].Url);
    }

    [Fact]
    public async Task Greeting_PassesTheUserClock_AndOmitsNewsWhenTheSourceFails()
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var recorded = new RecordingNews();
        var service = new WelcomeGreetingService(
            db,
            new FeatureLaunchCatalog([]),
            new IdleWeather(),
            recorded);

        var greeting = await service.GetGreetingAsync(userId, When);

        Assert.Equal(userId, recorded.UserId);
        Assert.Equal(When, recorded.At);
        Assert.Equal("tester", greeting.Content);
        Assert.Equal("10月9日 · 周五", greeting.DateLine);
        Assert.Equal(["记下的标题"], greeting.News.Select(item => item.Title).ToArray());

        var degraded = await new WelcomeGreetingService(
            db,
            new FeatureLaunchCatalog([]),
            new IdleWeather(),
            new ThrowingNews()).GetGreetingAsync(userId, When);

        Assert.Equal("tester", degraded.Content);
        Assert.Equal("10月9日 · 周五", degraded.DateLine);
        Assert.Null(degraded.MemoSummary);
        Assert.Empty(degraded.News);
    }

    public void Dispose() => _fx.Dispose();

    private static DateTimeOffset At(int day, int hour) =>
        new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);

    private static WelcomeNewsCandidate[] Pool() =>
    [
        new("OpenAI 最新", "https://openai.com/news/newest", At(9, 3), "openai"),
        new("OpenAI 次新", "https://openai.com/news/next", At(9, 2), "openai"),
        new("DeepMind 一条", "https://deepmind.google/blog/one", At(9, 1), "deepmind"),
        new("Hugging Face 一条", "https://huggingface.co/blog/one", At(8, 0), "huggingface"),
        new("TechCrunch 一条", "https://techcrunch.com/2026/10/07/one", At(7, 0), "techcrunch"),
        new("The Verge 一条", "https://www.theverge.com/ai/one", At(6, 0), "theverge")
    ];

    private async Task<int> ResetSeenAsync(MiraiNote.Data.Context.MiraiNoteDbContext db)
    {
        db.WelcomeNewsSeens.RemoveRange(db.WelcomeNewsSeens);
        await db.SaveChangesAsync();
        return await db.Users.Select(u => u.Id).SingleAsync();
    }

    private static async Task<int> AddUserAsync(MiraiNote.Data.Context.MiraiNoteDbContext db, string username, string email)
    {
        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = "hash"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static WelcomeNewsSeen Seen(int userId, string url, DateTimeOffset shownAt) =>
        new()
        {
            UserId = userId,
            Url = url,
            ShownAt = shownAt.UtcDateTime
        };

    private static WelcomeNewsClient Client(
        RecordingHandler handler,
        IMemoryCache cache,
        IWelcomeNewsSeenStore? seen = null,
        IWelcomeTitleTranslator? translator = null,
        TimeSpan? timeout = null,
        ILogger<WelcomeNewsClient>? logger = null) =>
        new(Factory(handler, timeout), cache, logger ?? NullLogger<WelcomeNewsClient>.Instance, translator, seen);

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

    private static HttpResponseMessage FiveFeeds(HttpRequestMessage request, CancellationToken _)
    {
        var host = request.RequestUri!.Host;
        if (host == "openai.com")
        {
            return Xml(Item("OpenAI 最新", "https://openai.com/news/newest", "Fri, 09 Oct 2026 03:00:00 GMT")
                + Item("OpenAI 次新", "https://openai.com/news/next", "Fri, 09 Oct 2026 02:30:00 GMT"));
        }

        if (host == "deepmind.google")
            return Xml(Item("DeepMind 一条", "https://deepmind.google/blog/one", "Fri, 09 Oct 2026 02:00:00 GMT"));
        if (host == "huggingface.co")
            return Xml(Item("Hugging Face 一条", "https://huggingface.co/blog/one", "Thu, 08 Oct 2026 00:00:00 GMT"));
        if (host == "techcrunch.com")
            return Xml(Item("TechCrunch 一条", "https://techcrunch.com/2026/10/07/one", "Wed, 07 Oct 2026 00:00:00 GMT"));
        if (host == "www.theverge.com")
            return Xml(Item("The Verge 一条", "https://www.theverge.com/ai/one", "Tue, 06 Oct 2026 00:00:00 GMT"));
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string Item(string title, string link, string published) =>
        $"""
        <item>
          <title>{title}</title>
          <link>{link}</link>
          <pubDate>{published}</pubDate>
        </item>
        """;

    private static CountingStream OversizedOpenAi()
    {
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel>
            {Item("OpenAI 仍在", "https://openai.com/news/kept", "Fri, 09 Oct 2026 04:00:00 GMT")}
            <item><title>太大</title><link>https://openai.com/news/huge</link><description>
            """;
        var prefix = Encoding.UTF8.GetBytes(xml);
        var body = new byte[prefix.Length + WelcomeNewsClient.MaxFeedBytes];
        Buffer.BlockCopy(prefix, 0, body, 0, prefix.Length);
        Array.Fill(body, (byte)'x', prefix.Length, WelcomeNewsClient.MaxFeedBytes);
        return new CountingStream(body);
    }

    private static HttpResponseMessage Xml(string items) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <rss version="2.0"><channel>{items}</channel></rss>
                """,
                Encoding.UTF8,
                "application/xml")
        };

    private sealed class CountingStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public CountingStream(byte[] data) => _data = data;

        public int ReadBytes { get; private set; }
        public int TotalBytes => _data.Length;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Pull(count);
            if (n == 0)
                return 0;
            Buffer.BlockCopy(_data, _position - n, buffer, offset, n);
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = Pull(buffer.Length);
            if (n > 0)
                _data.AsSpan(_position - n, n).CopyTo(buffer.Span);
            return ValueTask.FromResult(n);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Pull(int count)
        {
            if (_position >= _data.Length || count <= 0)
                return 0;
            var n = Math.Min(count, _data.Length - _position);
            _position += n;
            ReadBytes += n;
            return n;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public List<Uri> Calls { get; } = [];

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send)
            : this((request, ct) => Task.FromResult(send(request, ct)))
        {
        }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
            _send = send;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Calls)
                Calls.Add(request.RequestUri!);
            return _send(request, cancellationToken);
        }
    }

    private sealed class PrefixTranslator : IWelcomeTitleTranslator
    {
        public int Calls { get; private set; }
        public IReadOnlyList<string> SeenTitles { get; private set; } = [];

        public Task<IReadOnlyList<WelcomeNewsItem>> TranslateAsync(
            IReadOnlyList<WelcomeNewsItem> items,
            CancellationToken ct = default)
        {
            Calls++;
            SeenTitles = items.Select(item => item.Title).ToArray();
            IReadOnlyList<WelcomeNewsItem> translated = items
                .Select(item => item with { Title = "译:" + item.Title })
                .ToArray();
            return Task.FromResult(translated);
        }
    }

    private sealed class ThrowingTranslator : IWelcomeTitleTranslator
    {
        public Task<IReadOnlyList<WelcomeNewsItem>> TranslateAsync(
            IReadOnlyList<WelcomeNewsItem> items,
            CancellationToken ct = default) =>
            throw new HttpRequestException("翻译超时");
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }

    private sealed class BrokenSeen : IWelcomeNewsSeenStore
    {
        public Task<IReadOnlyDictionary<string, DateTime>> GetRecentUrlsAsync(int userId, DateTimeOffset utcNow, CancellationToken ct = default) =>
            throw new InvalidOperationException("已读表暂时打不开");

        public Task RecordAsync(int userId, IReadOnlyList<string> urls, DateTimeOffset utcNow, CancellationToken ct = default) =>
            throw new InvalidOperationException("已读表暂时写不进");
    }

    private sealed class RecordingNews : IWelcomeNewsSource
    {
        public int UserId { get; private set; }
        public DateTimeOffset At { get; private set; }

        public Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WelcomeNewsItem>>([new("记下的标题", "https://example.com/kept")]);

        public Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(int userId, DateTimeOffset utcNow, CancellationToken ct = default)
        {
            UserId = userId;
            At = utcNow;
            return GetLatestAsync(ct);
        }
    }

    private sealed class ThrowingNews : IWelcomeNewsSource
    {
        public Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WelcomeNewsItem>>([new("不该出现", "https://example.com/nope")]);

        public Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(int userId, DateTimeOffset utcNow, CancellationToken ct = default) =>
            throw new HttpRequestException("新闻源超时");
    }

    private sealed class IdleWeather : ISevereWeatherWarningSource, IWelcomeWeatherSource
    {
        public Task<string?> GetWarningAsync(string? place, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<WelcomeWeather> GetAsync(string? place, CancellationToken ct = default) =>
            Task.FromResult(default(WelcomeWeather));
    }
}
