using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.ChatModels;
using Xunit;

namespace MiraiNote.Tests;

public class ChatModelRegistryTests
{
    [Fact]
    public void ResolveForNewSession_uses_the_configured_default_enabled_model()
    {
        var registry = CreateRegistry(deepSeekApiKey: "test-deepseek-key", miniMaxApiKey: string.Empty);

        var model = registry.ResolveForNewSession(null);

        Assert.Equal("deepseek:deepseek-test", model.Key);
        Assert.True(model.SupportsChat);
    }

    [Fact]
    public void Public_catalog_hides_a_provider_without_an_api_key()
    {
        var registry = CreateRegistry(deepSeekApiKey: "test-deepseek-key", miniMaxApiKey: string.Empty);

        var catalog = registry.GetPublicCatalog();

        Assert.Contains(catalog, x => x.Key == "deepseek:deepseek-test");
        Assert.DoesNotContain(catalog, x => x.Provider == "minimax");
    }

    [Fact]
    public void ResolveForNewSession_rejects_a_disabled_or_unknown_model_key()
    {
        var registry = CreateRegistry(deepSeekApiKey: "test-deepseek-key", miniMaxApiKey: string.Empty);

        var error = Assert.Throws<ChatModelUnavailableException>(() => registry.ResolveForNewSession("minimax:MiniMax-M2.7"));

        Assert.Equal("MODEL_UNAVAILABLE", error.Code);
    }

    [Fact]
    public void Public_catalog_keeps_the_legacy_deepseek_configuration_available_during_migration()
    {
        var options = new AiOptions
        {
            DefaultModelKey = "deepseek:deepseek-v4-flash",
            Providers =
            [
                new AiProviderOptions
                {
                    Key = "deepseek",
                    DisplayName = "DeepSeek",
                    ApiKey = string.Empty,
                    Models =
                    [
                        new AiModelOptions { Key = "deepseek:deepseek-v4-flash", ModelId = "deepseek-v4-flash", DisplayName = "DeepSeek V4 Flash" },
                    ],
                },
            ],
        };

        var registry = new ChatModelRegistry(
            Options.Create(options),
            Options.Create(new DeepSeekOptions { ApiKey = "legacy-deepseek-key", Model = "deepseek-v4-flash" }));

        Assert.Contains(registry.GetPublicCatalog(), x => x.Key == "deepseek:deepseek-v4-flash");
    }

    [Fact]
    public void Public_catalog_builds_a_legacy_deepseek_entry_when_the_ai_section_is_absent()
    {
        var registry = new ChatModelRegistry(
            Options.Create(new AiOptions()),
            Options.Create(new DeepSeekOptions { ApiKey = "legacy-deepseek-key", Model = "deepseek-test" }));

        var model = Assert.Single(registry.GetPublicCatalog());

        Assert.Equal("deepseek:deepseek-test", model.Key);
        Assert.Equal("DeepSeek", model.ProviderDisplayName);
    }

    private static ChatModelRegistry CreateRegistry(string deepSeekApiKey, string miniMaxApiKey) =>
        new(Options.Create(new AiOptions
        {
            DefaultModelKey = "deepseek:deepseek-test",
            Providers =
            [
                new AiProviderOptions
                {
                    Key = "deepseek",
                    DisplayName = "DeepSeek",
                    ApiKey = deepSeekApiKey,
                    Models =
                    [
                        new AiModelOptions
                        {
                            Key = "deepseek:deepseek-test",
                            ModelId = "deepseek-test",
                            DisplayName = "DeepSeek Test",
                            SupportsChat = true,
                            SupportsWork = true,
                            SupportsTools = true,
                        },
                    ],
                },
                new AiProviderOptions
                {
                    Key = "minimax",
                    DisplayName = "MiniMax",
                    ApiKey = miniMaxApiKey,
                    Models =
                    [
                        new AiModelOptions
                        {
                            Key = "minimax:MiniMax-M2.7",
                            ModelId = "MiniMax-M2.7",
                            DisplayName = "MiniMax M2.7",
                            SupportsChat = true,
                            SupportsWork = true,
                            SupportsTools = true,
                        },
                    ],
                },
            ],
        }));
}
