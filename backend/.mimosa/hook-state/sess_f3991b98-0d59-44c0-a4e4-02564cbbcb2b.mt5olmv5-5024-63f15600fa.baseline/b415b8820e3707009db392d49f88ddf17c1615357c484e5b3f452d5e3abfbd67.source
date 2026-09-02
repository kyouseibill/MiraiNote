using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.Shared.Agent;

/// <summary>
/// Agent 规划器接口。
/// 在 Agent 执行任务前生成执行计划。
/// </summary>
public interface IAgentPlanner
{
    /// <summary>判断用户消息是否需要规划</summary>
    bool NeedsPlanning(string userMessage);

    /// <summary>
    /// 调用 LLM 生成执行计划。
    /// </summary>
    /// <param name="userMessage">用户需求</param>
    /// <param name="availableTools">可用工具名称列表</param>
    /// <param name="history">对话历史（可选），每项为 (role, content)</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>执行计划，简单任务返回 null</returns>
    Task<ExecutionPlan?> GeneratePlanAsync(
        string userMessage,
        List<string> availableTools,
        List<(string Role, string Content)>? history = null,
        CancellationToken ct = default);
}
