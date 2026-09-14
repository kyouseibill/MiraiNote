using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MiraiNote.API;
using MiraiNote.Core.Services;
using Moq;
using Xunit;

namespace MiraiNote.Tests;

public class AiConfigurationBindingTests
{
    [Fact]
    public void Ai_provider_key_overrides_legacy_DeepSeek_key_for_existing_services()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:Secret"] = "test-secret-that-is-long-enough-for-jwt-signing",
                ["DeepSeek:ApiKey"] = "legacy-key",
                ["AI:Providers:0:Key"] = "deepseek",
                ["AI:Providers:0:ApiKey"] = "ai-provider-key",
                ["AI:Providers:0:BaseUrl"] = "https://deepseek.example.test",
            })
            .Build();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(item => item.EnvironmentName).Returns("Development");
        var services = new ServiceCollection();

        services.AddApiLayer(configuration, environment.Object);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DeepSeekOptions>>().Value;

        Assert.Equal("ai-provider-key", options.ApiKey);
        Assert.Equal("https://deepseek.example.test", options.BaseUrl);
    }
}
