using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;

namespace MiraiNote.Core.Services.ChatModels;

/// <summary>
/// 服务端唯一的模型允许列表。客户端只能看到可用模型的公开元数据。
/// </summary>
public sealed class ChatModelRegistry : IChatModelRegistry
{
    public const string NoneAvailableMessage = "当前没有可用的模型，请联系管理员";

    private readonly IReadOnlyList<ChatModelDescriptor> _models;
    private readonly string? _defaultModelKey;

    public ChatModelRegistry(IOptions<AiOptions> options)
        : this(options, Options.Create(new DeepSeekOptions()))
    {
    }

    public ChatModelRegistry(IOptions<AiOptions> options, IOptions<DeepSeekOptions> legacyDeepSeekOptions)
    {
        var value = options.Value;
        var legacyDeepSeek = legacyDeepSeekOptions.Value;
        _defaultModelKey = string.IsNullOrWhiteSpace(value.DefaultModelKey)
            ? $"deepseek:{legacyDeepSeek.Model}"
            : value.DefaultModelKey;
        var models = value.Providers
            .Where(provider => !string.IsNullOrWhiteSpace(provider.Key))
            .SelectMany(provider => provider.Models.Select(model =>
            {
                var apiKey = string.Equals(provider.Key, "deepseek", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(provider.ApiKey)
                    ? legacyDeepSeek.ApiKey
                    : provider.ApiKey;
                return new ChatModelDescriptor(
                model.Key,
                provider.Key,
                string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Key : provider.DisplayName,
                model.ModelId,
                string.IsNullOrWhiteSpace(model.DisplayName) ? model.ModelId : model.DisplayName,
                model.SupportsChat,
                model.SupportsWork,
                model.SupportsTools,
                model.Enabled && !string.IsNullOrWhiteSpace(apiKey));
            }))
            .ToList();

        // 迁移期的已部署环境尚未写入 AI 节；保留原 DeepSeek 配置的可用入口，
        // 防止升级后已有聊天突然没有任何可选模型。
        if (!models.Any(item => string.Equals(item.Provider, "deepseek", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(legacyDeepSeek.ApiKey) &&
            !string.IsNullOrWhiteSpace(legacyDeepSeek.Model))
        {
            models.Add(new ChatModelDescriptor(
                $"deepseek:{legacyDeepSeek.Model}",
                "deepseek",
                "DeepSeek",
                legacyDeepSeek.Model,
                legacyDeepSeek.Model,
                SupportsChat: true,
                SupportsWork: true,
                SupportsTools: true,
                IsAvailable: true));
        }

        _models = models;
    }

    public IReadOnlyList<ChatModelDescriptor> GetPublicCatalog() =>
        _models.Where(model => model.IsAvailable).ToArray();

    public ChatModelDescriptor ResolveForNewSession(string? key)
    {
        if (!GetPublicCatalog().Any())
            throw new ChatModelUnavailableException(NoneAvailableMessage);

        var resolvedKey = string.IsNullOrWhiteSpace(key) ? _defaultModelKey : key;
        var model = _models.FirstOrDefault(item => string.Equals(item.Key, resolvedKey, StringComparison.OrdinalIgnoreCase));
        if (model is null || !model.IsAvailable)
        {
            throw new ChatModelUnavailableException("所选模型当前不可用，请选择其他模型。");
        }

        return model;
    }

    public ChatModelDescriptor ResolveForExistingSession(string? provider, string? model)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
        {
            return ResolveForNewSession(null);
        }

        var descriptor = _models.FirstOrDefault(item =>
            string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ModelId, model, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null || !descriptor.IsAvailable)
        {
            if (!GetPublicCatalog().Any())
                throw new ChatModelUnavailableException(NoneAvailableMessage);
            throw new ChatModelUnavailableException("当前会话模型暂不可用，请使用默认模型创建新对话。");
        }

        return descriptor;
    }
}
