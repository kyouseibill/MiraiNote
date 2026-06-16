using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiraiNote.CLI.Agent;

/// <summary>
/// 上下文管理器。
/// 监控对话历史的 token 用量，接近限制时自动压缩历史消息为摘要。
/// 估算规则：中文 ~1.5 chars/token，英文 ~4 chars/token，取 ~2 chars/token 粗略估算。
/// </summary>
public class AgentContextManager
{
    /// <summary>目标模型上下文窗口（DeepSeek Chat 默认 128K）</summary>
    private const int MaxContextTokens = 128_000;

    /// <summary>系统提示词预估 token 数</summary>
    private const int SystemPromptTokens = 3_500;

    /// <summary>回复预留 token 数</summary>
    private const int ResponseReserve = 8_000;

    /// <summary>触发压缩的阈值（已用 tokens / 总 tokens）</summary>
    private const double CompactionThreshold = 0.45;

    /// <summary>压缩后保留最近 N 轮对话</summary>
    private const int KeepRecentRounds = 4;

    /// <summary>每个字符的 token 估算比率</summary>
    private const double CharsPerToken = 2.0;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 估算一段文本的 token 数。
    /// </summary>
    public static int EstimateTokens(string text)
        => (int)Math.Ceiling(text.Length / CharsPerToken);

    /// <summary>
    /// 估算消息列表的 token 总数。
    /// </summary>
    public static int EstimateTokens(IReadOnlyList<AgentMessage> messages)
        => messages.Sum(m => EstimateTokens(m.Role + m.Content));

    /// <summary>
    /// 判断是否需要压缩历史。
    /// </summary>
    /// <param name="messages">当前对话历史</param>
    /// <param name="availableTools">工具定义数量（每个工具约 200 tokens）</param>
    /// <returns>是否需要压缩</returns>
    public bool NeedsCompaction(IReadOnlyList<AgentMessage> messages, int toolCount)
    {
        var historyTokens = EstimateTokens(messages);
        var toolTokens = toolCount * 200;
        var totalEstimate = SystemPromptTokens + historyTokens + toolTokens + ResponseReserve;

        return totalEstimate > MaxContextTokens * CompactionThreshold;
    }

    /// <summary>
    /// 获取当前 token 用量摘要。
    /// </summary>
    public ContextUsage GetUsage(IReadOnlyList<AgentMessage> messages, int toolCount)
    {
        var historyTokens = EstimateTokens(messages);
        var toolTokens = toolCount * 200;
        var totalEstimate = SystemPromptTokens + historyTokens + toolTokens + ResponseReserve;
        var percent = (double)totalEstimate / MaxContextTokens * 100;

        return new ContextUsage
        {
            EstimatedTokens = totalEstimate,
            MaxTokens = MaxContextTokens,
            PercentUsed = Math.Round(percent, 1),
            MessageCount = messages.Count,
            NeedsCompaction = totalEstimate > MaxContextTokens * CompactionThreshold
        };
    }

    /// <summary>
    /// 压缩对话历史：将早期消息合并为摘要，只保留最近 N 轮完整对话。
    /// 如果最近一轮包含大量工具调用结果，也会被摘要替代。
    /// </summary>
    /// <returns>压缩后的消息列表</returns>
    public List<AgentMessage> Compact(IReadOnlyList<AgentMessage> messages)
    {
        if (messages.Count <= KeepRecentRounds * 2)  // 每轮 user+assistant
            return messages.ToList();

        // 分割：保留最近 N 轮 + 之前的内容总结为一条系统级消息
        var keepFrom = Math.Max(0, messages.Count - KeepRecentRounds * 2);

        var earlyMessages = messages.Take(keepFrom).ToList();
        var recentMessages = messages.Skip(keepFrom).ToList();

        if (earlyMessages.Count == 0)
            return recentMessages;

        // 编造摘要（本地生成，不调 LLM 以节省成本）
        var summary = GenerateLocalSummary(earlyMessages);

        var result = new List<AgentMessage>
        {
            new("system", $"[上下文摘要] 以下是之前对话的要点：\n{summary}")
        };
        result.AddRange(recentMessages);

        return result;
    }

    /// <summary>
    /// 本地摘要生成（不调 LLM，纯文本压缩）。
    /// </summary>
    private static string GenerateLocalSummary(List<AgentMessage> messages)
    {
        var sb = new StringBuilder();
        var userMessages = messages.Where(m => m.Role == "user").ToList();

        if (userMessages.Count == 0)
            return "（空对话历史）";

        sb.AppendLine($"此前共 {userMessages.Count} 轮对话，关键话题：");

        foreach (var m in userMessages.Take(10)) // 最多列出 10 条
        {
            var truncated = m.Content.Length > 80 ? m.Content[..80] + "..." : m.Content;
            sb.AppendLine($"  - {truncated}");
        }

        if (userMessages.Count > 10)
            sb.AppendLine($"  ... 以及另外 {userMessages.Count - 10} 条消息");

        return sb.ToString();
    }
}

/// <summary>
/// 上下文用量信息。
/// </summary>
public class ContextUsage
{
    public int EstimatedTokens { get; set; }
    public int MaxTokens { get; set; }
    public double PercentUsed { get; set; }
    public int MessageCount { get; set; }
    public bool NeedsCompaction { get; set; }

    public override string ToString()
        => $"📊 上下文用量：{EstimatedTokens:N0}/{MaxTokens:N0} tokens ({PercentUsed}%)，{MessageCount} 条消息{(NeedsCompaction ? " ⚠ 需要压缩" : "")}";
}
