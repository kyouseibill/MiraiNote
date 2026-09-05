using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services.Mirai;
using MiraiNote.Data.Context;

namespace MiraiNote.Core.Services;

public interface IWelcomeGreetingService
{
    Task<string> GetGreetingAsync(int userId, DateOnly localDate, string? exclude = null, CancellationToken ct = default);
}

/// <summary>生成 Dashboard 的短欢迎语；AI 失败时从文案池随机选句（可排除上次展示）。</summary>
public sealed class WelcomeGreetingService : IWelcomeGreetingService
{
    /// <summary>文案池首句；保留常量名供兼容旧测试/引用。</summary>
    public const string FallbackGreeting = "今天，AI 正在把不可能改写成日常";
    private const int MaxLength = 60;
    private const string PoolCacheKey = "welcome-greeting-pool";
    private static readonly TimeSpan PoolCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GenerateTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// P0 本地文案池（硬编码回退）。运行时优先读 WelcomeGreeting 表；
    /// 选句改为每次随机，并通过 exclude 避免连续重复（池大小 ≥ 2 时）。
    /// </summary>
    public static readonly string[] GreetingPool =
    [
        "今天，AI 正在把不可能改写成日常",
        "下一个超级个体，或许只差一个会行动的 AI",
        "AI 的下一站不是回答，而是替你完成",
        "当 AI 开始理解意图，搜索框就会消失",
        "未来的办公室，可能只剩人类和一群智能代理",
        "今天的模型，正在偷偷学会你的工作方式",
        "最先被 AI 改写的，也许是我们对时间的想象",
        "AI 不会取代所有人，但会取代拒绝使用它的人",
        "下一场技术革命，可能从一个私人 AI 开始",
        "真正的 AI 入口，也许不是屏幕，而是生活本身",
        "模型越会说话，真正稀缺的越是好问题",
        "AI 正从工具变成同事，边界正在变薄",
        "有一天，AI 会比你的待办清单更懂你",
        "今天训练的每个模型，都在预演一种新文明",
        "AI 的黄金时代，可能比我们想的更近",
        "当机器拥有长期记忆，个人知识将变成超能力",
        "未来最贵的能力，也许是判断什么不该交给 AI",
        "AI 正在把一个人的想法放大成一支团队",
        "下一个爆发点，可能是会自己使用工具的模型",
        "如果 AI 能替你行动，选择将比执行更重要",
        "模型在变小，能力却在变大，这只是开始",
        "AI 代理正在醒来，软件将从等待命令变成主动协作",
        "未来的个人电脑，可能是一位住在云端的伙伴",
        "AI 让知识变便宜，也让独立思考更昂贵",
        "下一代应用不会打开，它们会主动出现在你需要时",
        "AI 的真正终局，或许是让每个人拥有自己的研究院",
        "今天的自动化，可能是明天的日常生活",
        "当 AI 开始做梦，人类会重新定义创造力吗",
        "未来不是人类对抗 AI，而是人类选择与谁协作",
        "AI 正在重写软件，下一页可能由它自己来写",
        "一个人的生产力上限，正在被 AI 重新估算",
        "最危险的 AI 不是最聪明的，而是最容易被信任的",
        "AI 会让普通人的想法第一次拥有工业级执行力",
        "如果未来突然提前，今天可能就是它的序章",
        "AI 正在从云端下沉到每一台设备、每一个日常决定",
        "真正的智能，不是知道答案，而是知道下一步",
        "AI 的速度已经超过共识，社会正在追赶它",
        "未来的竞争，不是有没有 AI，而是谁更会定义目标",
        "当每个人都有 AI 助手，稀缺的将是独特的愿景",
        "今天，给你的 AI 一个值得完成的任务",
    ];

    private readonly MiraiNoteDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly DeepSeekOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WelcomeGreetingService> _logger;

    public WelcomeGreetingService(
        MiraiNoteDbContext db,
        IMemoryCache cache,
        IOptions<DeepSeekOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<WelcomeGreetingService> logger)
    {
        _db = db;
        _cache = cache;
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetGreetingAsync(
        int userId,
        DateOnly localDate,
        string? exclude = null,
        CancellationToken ct = default)
    {
        _ = userId; // 保留签名兼容；随机池选句不再依赖 userId
        try
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
                return await PickFromPoolAsync(exclude, ct);

            using var client = DeepSeekJsonClient.CreateAuthorizedClient(
                _httpClientFactory, _options.BaseUrl, _options.ApiKey);
            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = "你是 MiraiNote 首页的 AI 前沿观察者与未来预言者，请写一句中文欢迎语。只输出文案本身，不要解释、标题或 Markdown。" +
                              "内容要贴近当下人工智能产业的真实进展与下一步可能：可关注 OpenAI、Anthropic、Google、SpaceX 等机构的模型、智能体、机器人、算力与航天 AI 动向，也可把这些趋势延伸成大胆预言。" +
                              "优先写出具体而有画面的技术变化、竞争信号或时代转折；不要捏造未经确认的新闻、产品发布、人物言论或数字。无法确认的内容必须使用‘可能’‘也许’‘正在逼近’‘未来’等推演语气。" +
                              "默认原创，单行且不超过 60 个汉字或字符，语言简洁、有冲击力和悬念感，让人想继续思考。"
                },
                new
                {
                    role = "user",
                    content = $"今天是 {localDate.ToString("yyyy年M月d日", CultureInfo.InvariantCulture)}，请生成今日欢迎语。"
                }
            };
            var greeting = await DeepSeekJsonClient.CompleteAsync(
                client, _options.Model, messages,
                temperature: 0.8, maxTokens: 256, jsonObject: false,
                timeout: GenerateTimeout, ct, disableThinking: true);

            if (IsValid(greeting))
                return greeting.Trim();

            _logger.LogWarning(
                "今日欢迎语生成结果无效，已回退文案池：内容长度 {Length}，包含换行 {HasLineBreak}",
                greeting.Length,
                greeting.Contains('\n') || greeting.Contains('\r'));
            return await PickFromPoolAsync(exclude, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning("今日欢迎语生成失败：{Message}", ex.Message);
            return await PickFromPoolAsync(exclude, ct);
        }
    }

    /// <summary>从缓存/DB 加载文案池后随机选句；池空或异常时回退硬编码原 40 条。</summary>
    public async Task<string> PickFromPoolAsync(string? exclude = null, CancellationToken ct = default)
    {
        var pool = await LoadPoolAsync(ct);
        return PickRandomFromPool(pool, exclude);
    }

    /// <summary>对硬编码 GreetingPool 随机选句（兼容旧测试入口）。</summary>
    public static string PickRandomFromPool(string? exclude = null, Random? random = null) =>
        PickRandomFromPool(GreetingPool, exclude, random);

    /// <summary>
    /// 从文案列表随机选句。pool 为空时回退 GreetingPool。
    /// exclude 非空且池大小 ≥ 2 时，排除与 exclude 完全相同的句子，避免连续重复；
    /// 池大小为 1 时允许重复返回该句。
    /// </summary>
    public static string PickRandomFromPool(
        IReadOnlyList<string> pool,
        string? exclude = null,
        Random? random = null)
    {
        var effective = pool is { Count: > 0 } ? pool : GreetingPool;
        random ??= Random.Shared;

        if (effective.Count == 1)
            return effective[0];

        IReadOnlyList<string> candidates = effective;
        if (!string.IsNullOrEmpty(exclude))
        {
            var filtered = effective.Where(s => !string.Equals(s, exclude, StringComparison.Ordinal)).ToList();
            if (filtered.Count > 0)
                candidates = filtered;
        }

        return candidates[random.Next(candidates.Count)];
    }

    private async Task<IReadOnlyList<string>> LoadPoolAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(PoolCacheKey, out IReadOnlyList<string>? cached) && cached is { Count: > 0 })
            return cached;

        try
        {
            // 全局软删除过滤器已排除 IsDeleted=1；再按 IsActive + SortOrder,Id 排序
            var list = await _db.WelcomeGreetings
                .AsNoTracking()
                .Where(g => g.IsActive)
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Id)
                .Select(g => g.Content)
                .ToListAsync(ct);

            if (list.Count == 0)
            {
                _logger.LogWarning("WelcomeGreeting 表无可用文案，回退硬编码文案池");
                return GreetingPool;
            }

            _cache.Set(PoolCacheKey, (IReadOnlyList<string>)list, PoolCacheDuration);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载 WelcomeGreeting 文案池失败，回退硬编码文案池");
            return GreetingPool;
        }
    }

    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= MaxLength
        && !value.Contains('\n')
        && !value.Contains('\r');
}
