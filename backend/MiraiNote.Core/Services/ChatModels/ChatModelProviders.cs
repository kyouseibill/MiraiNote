using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;

namespace MiraiNote.Core.Services.ChatModels;

/// <summary>
/// Provider-specific, server-only connection data. It is never sent through a DTO.
/// </summary>
public sealed record ChatModelConnection(
    string ProviderKey,
    string ModelId,
    string BaseUrl,
    string ApiKey,
    bool UsesReasoningSplit)
{
    public Dictionary<string, object?> CreateRequestBody(
        object messages,
        bool stream,
        object? tools = null,
        bool includeTools = false)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = ModelId,
            ["messages"] = messages,
            ["stream"] = stream,
        };
        if (includeTools)
        {
            body["tools"] = tools;
            body["tool_choice"] = "auto";
        }
        if (UsesReasoningSplit) body["reasoning_split"] = true;
        return body;
    }

    public Dictionary<string, object?> CreateAssistantHistoryMessage(
        string? content,
        string? reasoningContent,
        object? toolCalls = null)
    {
        var message = new Dictionary<string, object?>
        {
            ["role"] = "assistant",
            ["content"] = content,
        };
        if (!string.IsNullOrWhiteSpace(reasoningContent))
        {
            // The OpenAI-compatible MiniMax API requires this reasoning chain to
            // be present on the assistant message for subsequent tool-call turns.
            message[UsesReasoningSplit ? "reasoning_details" : "reasoning_content"] = UsesReasoningSplit
                ? new[] { new Dictionary<string, string> { ["text"] = reasoningContent } }
                : reasoningContent;
        }
        if (toolCalls is not null) message["tool_calls"] = toolCalls;
        return message;
    }
}

public interface IChatModelProvider
{
    string ProviderKey { get; }
    ChatModelConnection CreateConnection(ChatModelDescriptor model);
}

public interface IChatModelProviderResolver
{
    ChatModelConnection ResolveConnection(ChatModelDescriptor model);
}

/// <summary>
/// Resolves a model descriptor into the provider adapter which owns its protocol details.
/// </summary>
public sealed class ChatModelProviderResolver(IEnumerable<IChatModelProvider> providers) : IChatModelProviderResolver
{
    private readonly IReadOnlyDictionary<string, IChatModelProvider> _providers = providers
        .ToDictionary(provider => provider.ProviderKey, StringComparer.OrdinalIgnoreCase);

    public ChatModelConnection ResolveConnection(ChatModelDescriptor model)
    {
        if (!_providers.TryGetValue(model.Provider, out var provider))
            throw new ChatModelUnavailableException("当前模型的服务商尚未启用，请选择其他模型。");
        return provider.CreateConnection(model);
    }
}

public sealed class DeepSeekChatModelProvider : IChatModelProvider
{
    private readonly AiOptions _aiOptions;
    private readonly DeepSeekOptions _legacyOptions;

    public DeepSeekChatModelProvider(IOptions<AiOptions> aiOptions, IOptions<DeepSeekOptions> legacyOptions)
    {
        _aiOptions = aiOptions.Value;
        _legacyOptions = legacyOptions.Value;
    }

    public string ProviderKey => "deepseek";

    public ChatModelConnection CreateConnection(ChatModelDescriptor model)
    {
        var provider = FindProvider(_aiOptions, ProviderKey);
        var apiKey = string.IsNullOrWhiteSpace(provider?.ApiKey) ? _legacyOptions.ApiKey : provider.ApiKey;
        var baseUrl = string.IsNullOrWhiteSpace(provider?.BaseUrl) ? _legacyOptions.BaseUrl : provider.BaseUrl;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(baseUrl))
            throw new ChatModelUnavailableException("当前模型暂不可用，请选择其他模型。");
        return new ChatModelConnection(ProviderKey, model.ModelId, baseUrl, apiKey, UsesReasoningSplit: false);
    }

    internal static AiProviderOptions? FindProvider(AiOptions options, string key) =>
        options.Providers.FirstOrDefault(provider => string.Equals(provider.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// MiniMax's OpenAI-compatible endpoint. reasoning_split keeps hidden reasoning out
/// of user-facing content while preserving it for the next function-call turn.
/// </summary>
public sealed class MiniMaxChatModelProvider(IOptions<AiOptions> aiOptions) : IChatModelProvider
{
    private readonly AiOptions _aiOptions = aiOptions.Value;

    public string ProviderKey => "minimax";

    public ChatModelConnection CreateConnection(ChatModelDescriptor model)
    {
        var provider = DeepSeekChatModelProvider.FindProvider(_aiOptions, ProviderKey);
        if (provider is null || string.IsNullOrWhiteSpace(provider.ApiKey))
            throw new ChatModelUnavailableException("当前模型暂不可用，请选择其他模型。");
        var baseUrl = string.IsNullOrWhiteSpace(provider.BaseUrl) ? "https://api.minimax.io/v1" : provider.BaseUrl;
        return new ChatModelConnection(ProviderKey, model.ModelId, baseUrl, provider.ApiKey, UsesReasoningSplit: true);
    }
}
