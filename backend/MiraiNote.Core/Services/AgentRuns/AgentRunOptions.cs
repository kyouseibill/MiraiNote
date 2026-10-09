namespace MiraiNote.Core.Services.AgentRuns;

/// <summary>
/// 工作模式后台执行并发。生产默认全局 2、每用户 1，避免一个账号占满进程。
/// 配置节：AgentRuns:MaxConcurrent / AgentRuns:MaxPerUser（环境变量 AgentRuns__MaxConcurrent）。
/// </summary>
public sealed class AgentRunOptions
{
    public const string SectionName = "AgentRuns";

    /// <summary>全站同时处于执行中的工作模式任务上限。</summary>
    public int MaxConcurrent { get; set; } = 2;

    /// <summary>同一用户同时处于执行中的工作模式任务上限。超出的任务留在队列，不挡住其他用户。</summary>
    public int MaxPerUser { get; set; } = 1;
}
