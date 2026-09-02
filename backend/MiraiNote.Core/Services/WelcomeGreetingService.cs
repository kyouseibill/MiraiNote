using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services.Mirai;

namespace MiraiNote.Core.Services;

public interface IWelcomeGreetingService
{
    Task<string> GetGreetingAsync(int userId, DateOnly localDate, CancellationToken ct = default);
}

/// <summary>生成 Dashboard 的短欢迎语；AI 失败时按用户+本地日期从文案池稳定选句。</summary>
public sealed class WelcomeGreetingService : IWelcomeGreetingService
{
    /// <summary>文案池首句；保留常量名供兼容旧测试/引用。</summary>
    public const string FallbackGreeting = "今天，安静地推进";
    private const int MaxLength = 60;
    private static readonly TimeSpan GenerateTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// P0 本地文案池。选句算法：UTF-8 字节对 key="{userId}:{yyyy-MM-dd}" 做 FNV-1a 32-bit，
    /// 再 mod Pool.Length，保证同用户同日本地日期结果稳定。
    /// </summary>
    public static readonly string[] GreetingPool =
    [
        "今天，安静地推进",
        "今天，只把一件重要的事做好",
        "慢慢来，但不要停",
        "先写下一行，再谈后面的事",
        "今天的节奏，由你自己定",
        "把注意力收回到眼前这一步",
        "不必赶完所有，完成最要紧的就好",
        "深呼吸一次，然后开始",
        "今天适合稳步向前",
        "小事做好，也是前进",
        "给专注留一段不被打断的时间",
        "今天，少一点焦虑，多一点行动",
        "先开始，完美稍后再说",
        "把复杂的事拆小一点",
        "今天也值得认真对待",
        "安静工作，比匆忙更有效",
        "记住你为什么开始",
        "今天，把能量用在刀刃上",
        "完成比完美更靠近目标",
        "允许自己按自己的速度走",
        "先清理桌面，再清理思绪",
        "今天，留下一点可见的进展",
        "不必一次走完，迈出下一步即可",
        "把今天过成自己能复盘的一天",
        "少开几个标签页，多做一件实事",
        "今天适合把拖延换成开始",
        "专注当下这一刻就够了",
        "温柔对待自己，认真对待工作",
        "今天，写清楚再动手",
        "进展不一定很大，但要真实",
        "把干扰先放到一边",
        "今天，做能积累的事",
        "慢一点，也要把路走对",
        "先兑现对自己的一个小承诺",
        "今天的你，比昨天多一点清晰",
        "把待办收束到三件以内",
        "安静里，更容易想明白",
        "今天，给重要的事留出主场",
        "做完一件，再打开下一件",
        "今天，也请好好照顾自己的节奏",
    ];

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

    public async Task<string> GetGreetingAsync(int userId, DateOnly localDate, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
                return PickFromPool(userId, localDate);

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
                new
                {
                    role = "user",
                    content = $"今天是 {localDate.ToString("yyyy年M月d日", CultureInfo.InvariantCulture)}，请生成今日欢迎语。"
                }
            };
            var greeting = await DeepSeekJsonClient.CompleteAsync(
                client, _options.Model, messages,
                temperature: 0.8, maxTokens: 100, jsonObject: false,
                timeout: GenerateTimeout, ct);

            return IsValid(greeting) ? greeting.Trim() : PickFromPool(userId, localDate);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning("今日欢迎语生成失败：{Message}", ex.Message);
            return PickFromPool(userId, localDate);
        }
    }

    public static string PickFromPool(int userId, DateOnly localDate)
    {
        var key = $"{userId}:{localDate:yyyy-MM-dd}";
        var hash = Fnv1a32(Encoding.UTF8.GetBytes(key));
        var index = (int)(hash % (uint)GreetingPool.Length);
        return GreetingPool[index];
    }

    private static uint Fnv1a32(ReadOnlySpan<byte> data)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }

    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= MaxLength
        && !value.Contains('\n')
        && !value.Contains('\r');
}
