using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services.Mirai;

namespace MiraiNote.Core.Services;

public interface IWelcomeGreetingService
{
    Task<string> GetGreetingAsync(CancellationToken ct = default);
}

/// <summary>生成 Dashboard 的短欢迎语；任何异常或不合规输出均回退到固定文案。</summary>
public sealed class WelcomeGreetingService : IWelcomeGreetingService
{
    public const string FallbackGreeting = "今天，安静地推进";
    private const int MaxLength = 60;
    private static readonly TimeSpan GenerateTimeout = TimeSpan.FromSeconds(10);

    private readonly DeepSeekOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WelcomeGreetingService> _logger;

    public WelcomeGreetingService(
        IOptions<DeepSeekOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<WelcomeGreetingService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetGreetingAsync(CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
                return FallbackGreeting;

            using var client = DeepSeekJsonClient.CreateAuthorizedClient(
                _httpClientFactory, _options.BaseUrl, _options.ApiKey);
            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = "你为 MiraiNote 首页写一句中文欢迎语。只输出文案本身，不要解释、标题或 Markdown。" +
                              "默认原创，单行且不超过 60 个汉字或字符，语气安静、克制、鼓励行动。" +
                              "只有在准确知道作者时才可引用名言；引用必须在末尾以“——作者”落款。"
                },
                new { role = "user", content = $"今天是 {DateTime.Now:yyyy年M月d日}，请生成今日欢迎语。" }
            };
            var greeting = await DeepSeekJsonClient.CompleteAsync(
                client, _options.Model, messages,
                temperature: 0.8, maxTokens: 100, jsonObject: false,
                timeout: GenerateTimeout, ct);

            return IsValid(greeting) ? greeting.Trim() : FallbackGreeting;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning("今日欢迎语生成失败：{Message}", ex.Message);
            return FallbackGreeting;
        }
    }

    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= MaxLength
        && !value.Contains('\n')
        && !value.Contains('\r');
}
