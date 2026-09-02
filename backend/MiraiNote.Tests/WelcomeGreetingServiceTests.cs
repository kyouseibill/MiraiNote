using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using Xunit;

namespace MiraiNote.Tests;

public class WelcomeGreetingServiceTests
{
    private static WelcomeGreetingService CreateService(string? apiKey, IHttpClientFactory factory) =>
        new(
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
    public void PickFromPool_ChangesAcrossDaysForSameUser()
    {
        var userId = 7;
        var start = new DateOnly(2026, 9, 1);
        string? first = null;
        var foundDifferent = false;
        for (var i = 0; i < 14; i++)
        {
            var pick = WelcomeGreetingService.PickFromPool(userId, start.AddDays(i));
            first ??= pick;
            if (pick != first)
            {
                foundDifferent = true;
                break;
            }
        }
        Assert.True(foundDifferent, "14 天内应至少出现一句不同文案");
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
}
