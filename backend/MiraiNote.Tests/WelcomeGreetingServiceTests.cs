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
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ =>
            Task.FromResult("今天，把重要的一件事做好。"));
        var service = CreateService("test-key", factory);
        var day = new DateOnly(2026, 9, 3);

        var greeting = await service.GetGreetingAsync(userId: 1013, day);

        Assert.Equal("今天，把重要的一件事做好。", greeting);
    }

    [Fact]
    public async Task GetGreeting_ReturnsPoolPickWhenAiResponseIsTooLong()
    {
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ =>
            Task.FromResult(new string('好', 61)));
        var service = CreateService("test-key", factory);
        var day = new DateOnly(2026, 9, 3);

        var greeting = await service.GetGreetingAsync(userId: 1013, day);

        Assert.Equal(WelcomeGreetingService.PickFromPool(1013, day), greeting);
        Assert.Contains(greeting, WelcomeGreetingService.GreetingPool);
    }

    [Fact]
    public async Task GetGreeting_ReturnsPoolPickWhenApiKeyMissing()
    {
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ => Task.FromResult("不应调用"));
        var service = CreateService(apiKey: null, factory);
        var day = new DateOnly(2026, 9, 3);

        var greeting = await service.GetGreetingAsync(userId: 42, day);

        Assert.Equal(WelcomeGreetingService.PickFromPool(42, day), greeting);
    }

    [Fact]
    public void PickFromPool_IsStableForSameUserAndDate()
    {
        var day = new DateOnly(2026, 9, 3);
        var a = WelcomeGreetingService.PickFromPool(7, day);
        var b = WelcomeGreetingService.PickFromPool(7, day);
        Assert.Equal(a, b);
    }

    [Fact]
    public void PickFromPool_HasAtLeastFiveDistinctOverSevenToFourteenDays()
    {
        var userId = 7;
        var start = new DateOnly(2026, 9, 1);
        var distinct = new HashSet<string>();
        for (var i = 0; i < 14; i++)
            distinct.Add(WelcomeGreetingService.PickFromPool(userId, start.AddDays(i)));

        Assert.True(distinct.Count >= 5, $"7～14 天内 distinct 应为 ≥5，实际 {distinct.Count}");
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
    public void PickFromPool_UsesExplicitListWhenProvided()
    {
        var day = new DateOnly(2026, 9, 3);
        var pool = new[] { "甲", "乙", "丙" };
        var pick = WelcomeGreetingService.PickFromPool(1013, day, pool);

        Assert.Contains(pick, pool);
        Assert.Equal(WelcomeGreetingService.PickFromPool(1013, day, pool), pick);
        Assert.DoesNotContain(pick, WelcomeGreetingService.GreetingPool);
    }

    [Fact]
    public void PickFromPool_FallsBackToHardcodedWhenListEmpty()
    {
        var day = new DateOnly(2026, 9, 3);
        var pick = WelcomeGreetingService.PickFromPool(42, day, Array.Empty<string>());

        Assert.Equal(WelcomeGreetingService.PickFromPool(42, day), pick);
        Assert.Contains(pick, WelcomeGreetingService.GreetingPool);
    }

    [Fact]
    public async Task PickFromPoolAsync_UsesDbRowsOrderedBySortOrder()
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
        var day = new DateOnly(2026, 9, 3);
        var expectedPool = new[] { "库内第一条", "库内第二条", "库内第三条" };

        var greeting = await service.PickFromPoolAsync(7, day);

        Assert.Equal(WelcomeGreetingService.PickFromPool(7, day, expectedPool), greeting);
        Assert.Contains(greeting, expectedPool);
    }

    [Fact]
    public async Task PickFromPoolAsync_FallsBackWhenDbEmpty()
    {
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ => Task.FromResult("不应调用"));
        var service = CreateService(apiKey: null, factory);
        var day = new DateOnly(2026, 9, 3);

        var greeting = await service.PickFromPoolAsync(42, day);

        Assert.Equal(WelcomeGreetingService.PickFromPool(42, day), greeting);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _fx.Dispose();
    }
}
