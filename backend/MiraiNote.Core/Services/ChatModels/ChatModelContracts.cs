namespace MiraiNote.Core.Services.ChatModels;

/// <summary>
/// AI 提供商与模型的部署配置。密钥只在服务端配置中存在，绝不进入 DTO。
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "AI";

    public string? DefaultModelKey { get; set; }
    /// <summary>Work 完成校验的单次最长等待秒数（5..60）。</summary>
    public int WorkVerificationTimeoutSeconds { get; set; } = 15;
    public List<AiProviderOptions> Providers { get; set; } = [];
}

public sealed class AiProviderOptions
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public List<AiModelOptions> Models { get; set; } = [];
}

public sealed class AiModelOptions
{
    public string Key { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool SupportsChat { get; set; } = true;
    public bool SupportsWork { get; set; } = true;
    public bool SupportsTools { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public sealed record ChatModelDescriptor(
    string Key,
    string Provider,
    string ProviderDisplayName,
    string ModelId,
    string DisplayName,
    bool SupportsChat,
    bool SupportsWork,
    bool SupportsTools,
    bool IsAvailable);

public interface IChatModelRegistry
{
    ChatModelDescriptor ResolveForNewSession(string? key);
    ChatModelDescriptor ResolveForExistingSession(string? provider, string? model);
    IReadOnlyList<ChatModelDescriptor> GetPublicCatalog();
}

public sealed class ChatModelUnavailableException : InvalidOperationException
{
    public const string ErrorCode = "MODEL_UNAVAILABLE";

    public ChatModelUnavailableException(string message) : base(message) { }

    public string Code => ErrorCode;
}
