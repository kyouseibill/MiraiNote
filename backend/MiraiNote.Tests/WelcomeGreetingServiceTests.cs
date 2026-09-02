using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using Xunit;

namespace MiraiNote.Tests;

public class WelcomeGreetingServiceTests
{
    [Fact]
    public async Task GetGreeting_ReturnsOriginalGreetingFromAiResponse()
    {
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ =>
            Task.FromResult("今天，把重要的一件事做好。"));
        var service = new WelcomeGreetingService(
            Options.Create(new DeepSeekOptions
            {
                ApiKey = "test-key",
                BaseUrl = "https://example.test/v1/",
                Model = "deepseek-test"
            }),
            factory,
            NullLogger<WelcomeGreetingService>.Instance);

        var greeting = await service.GetGreetingAsync();

        Assert.Equal("今天，把重要的一件事做好。", greeting);
    }

    [Fact]
    public async Task GetGreeting_ReturnsFallbackWhenAiResponseIsTooLong()
    {
        var (factory, _) = MiraiTestFixture.MockDeepSeek(_ =>
            Task.FromResult(new string('好', 61)));
        var service = new WelcomeGreetingService(
            Options.Create(new DeepSeekOptions
            {
                ApiKey = "test-key",
                BaseUrl = "https://example.test/v1/",
                Model = "deepseek-test"
            }),
            factory,
            NullLogger<WelcomeGreetingService>.Instance);

        var greeting = await service.GetGreetingAsync();

        Assert.Equal(WelcomeGreetingService.FallbackGreeting, greeting);
    }
}
