using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using Xunit;

namespace MiraiNote.Tests;

public class WelcomeGreetingServiceTests : IDisposable
{
    private readonly MiraiTestFixture _fx = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private WelcomeGreetingService CreateService(
        string? apiKey,
        IHttpClientFactory factory,
        MiraiNoteDbContext? db = null) =>
        new(
            db ?? _fx.CreateContext(),
            _cache,
            Options.Create(new DeepSeekOptions
            {
                ApiKey = apiKey ?? "",
                BaseUrl = "https://example.test/v1/",
                Model = "deepseek-test"
            }),
            factory,
            NullLogger<WelcomeGreetingService>.Instance);

    [Fact]
    public async Task GetGreeting_ReturnsOriginalGreetingFromAiResponse()
    {
        var (factory, captured) = MiraiTestFixture.MockDeepSeek(_ =>
            Task.FromResult("今天，把重要的一件事做好。"));
        var service = CreateService("test-key", factory);
        var day = new DateOnly(2026, 9, 3);

        var greeting = await service.GetGreetingAsync(userId: 1013, day);

        Assert.Equal("今天，把重要的一件事做好。", greeting);
        var prompt = MiraiTestFixture.DecodeMessageText(captured.Single());
        Assert.Contains("AI 前沿观察者与未来预言者", prompt);
        Assert.Contains("真实进展", prompt);
        Assert.Contains("OpenAI、Anthropic、Google、SpaceX", prompt);
        Assert.Contains("不要捏造未经确认的新闻", prompt);
        Assert.Contains("不超过 60 个汉字", prompt);

        using var request = System.Text.Json.JsonDocument.Parse(captured.Single());
        Assert.Equal(256, request.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("disabled", request.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task GetGreeting_ReturnsPoolPickWhenAiResponseIsTooLong()
    {
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ =>
            Task.FromResult(new string('好', 61)));
        var service = CreateService("test-key", factory);
        var day = new DateOnly(2026, 9, 3);

        var greeting = await service.GetGreetingAsync(userId: 1013, day, exclude: "今天，安静地推进");

        Assert.Contains(greeting, WelcomeGreetingService.GreetingPool);
        Assert.NotEqual("今天，安静地推进", greeting);
    }

    [Fact]
    public async Task GetGreeting_ReturnsPoolPickWhenApiKeyMissing()
    {
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ => Task.FromResult("不应调用"));
        var service = CreateService(apiKey: null, factory);
        var day = new DateOnly(2026, 9, 3);

        var greeting = await service.GetGreetingAsync(userId: 42, day);

        Assert.Contains(greeting, WelcomeGreetingService.GreetingPool);
    }

    [Fact]
    public async Task GetGreeting_PassesExcludeToPoolFallback()
    {
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ => Task.FromResult("不应调用"));
        var service = CreateService(apiKey: null, factory);
        var day = new DateOnly(2026, 9, 3);
        var exclude = WelcomeGreetingService.GreetingPool[0];

        var greeting = await service.GetGreetingAsync(userId: 42, day, exclude);

        Assert.Contains(greeting, WelcomeGreetingService.GreetingPool);
        Assert.NotEqual(exclude, greeting);
    }

    [Fact]
    public void PickRandomFromPool_ExcludesLastWhenPoolHasMultiple()
    {
        var pool = new[] { "甲", "乙", "丙" };
        var rng = new Random(12345);
        for (var i = 0; i < 40; i++)
        {
            var pick = WelcomeGreetingService.PickRandomFromPool(pool, exclude: "乙", random: rng);
            Assert.Contains(pick, pool);
            Assert.NotEqual("乙", pick);
        }
    }

    [Fact]
    public void PickRandomFromPool_AllowsRepeatWhenPoolSizeIsOne()
    {
        var pool = new[] { "唯一一句" };
        var pick = WelcomeGreetingService.PickRandomFromPool(pool, exclude: "唯一一句");
        Assert.Equal("唯一一句", pick);
    }

    [Fact]
    public void PickRandomFromPool_VariesAcrossCalls()
    {
        var pool = WelcomeGreetingService.GreetingPool;
        var distinct = new HashSet<string>();
        var rng = new Random(7);
        for (var i = 0; i < 80; i++)
            distinct.Add(WelcomeGreetingService.PickRandomFromPool(pool, random: rng));

        Assert.True(distinct.Count >= 5, $"随机 80 次 distinct 应为 ≥5，实际 {distinct.Count}");
    }

    [Fact]
    public void GreetingPool_HasExpectedSizeAndFallbackAsFirst()
    {
        Assert.Equal(40, WelcomeGreetingService.GreetingPool.Length);
        Assert.Equal(WelcomeGreetingService.FallbackGreeting, WelcomeGreetingService.GreetingPool[0]);
        Assert.All(WelcomeGreetingService.GreetingPool, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g));
            Assert.True(g.Length <= 60);
            Assert.DoesNotContain('\n', g);
        });
    }

    [Fact]
    public void PickRandomFromPool_UsesExplicitListWhenProvided()
    {
        var pool = new[] { "甲", "乙", "丙" };
        var pick = WelcomeGreetingService.PickRandomFromPool(pool, random: new Random(1));

        Assert.Contains(pick, pool);
        Assert.DoesNotContain(pick, WelcomeGreetingService.GreetingPool);
    }

    [Fact]
    public void PickRandomFromPool_FallsBackToHardcodedWhenListEmpty()
    {
        var pick = WelcomeGreetingService.PickRandomFromPool(Array.Empty<string>(), random: new Random(2));

        Assert.Contains(pick, WelcomeGreetingService.GreetingPool);
    }

    [Fact]
    public async Task PickFromPoolAsync_UsesDbRowsAndHonorsExclude()
    {
        await using (var seed = _fx.CreateContext())
        {
            seed.WelcomeGreetings.AddRange(
                new WelcomeGreeting { Content = "库内第三条", IsActive = true, SortOrder = 30 },
                new WelcomeGreeting { Content = "库内第一条", IsActive = true, SortOrder = 10 },
                new WelcomeGreeting { Content = "库内第二条", IsActive = true, SortOrder = 20 },
                new WelcomeGreeting { Content = "已禁用", IsActive = false, SortOrder = 1 });
            await seed.SaveChangesAsync();
        }

        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ => Task.FromResult("不应调用"));
        var service = CreateService(apiKey: null, factory);
        var expectedPool = new[] { "库内第一条", "库内第二条", "库内第三条" };

        for (var i = 0; i < 20; i++)
        {
            var greeting = await service.PickFromPoolAsync(exclude: "库内第二条");
            Assert.Contains(greeting, expectedPool);
            Assert.NotEqual("库内第二条", greeting);
        }
    }

    [Fact]
    public async Task PickFromPoolAsync_FallsBackWhenDbEmpty()
    {
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ => Task.FromResult("不应调用"));
        var service = CreateService(apiKey: null, factory);

        var greeting = await service.PickFromPoolAsync();

        Assert.Contains(greeting, WelcomeGreetingService.GreetingPool);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _fx.Dispose();
    }
}
