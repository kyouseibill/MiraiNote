using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Shared.Agent;

/// <summary>
/// Agent 反思器接口。
/// 任务执行完成后对输出做质量评估。
/// </summary>
public interface IAgentReflector
{
    /// <summary>
    /// 对 Agent 回复做质量反思。
    /// </summary>
    /// <param name="userMessage">原始用户需求</param>
    /// <param name="assistantResponse">Agent 的最终回复</param>
    /// <param name="toolCallsCount">工具调用次数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>反思结果，null 表示不需要反思</returns>
    Task<ReflectionResult?> ReflectAsync(
        string userMessage,
        string assistantResponse,
        int toolCallsCount,
        CancellationToken ct = default);
}
