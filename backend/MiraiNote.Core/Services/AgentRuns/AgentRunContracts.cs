namespace MiraiNote.Core.Services.AgentRuns;

public static class AgentRunStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string AwaitingConfirmation = "awaiting_confirmation";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Stopped = "stopped";
    public const string Recoverable = "recoverable";
}

public static class AgentRunState
{
    public static bool IsTerminal(string status) => status is AgentRunStatus.Completed or AgentRunStatus.Failed or AgentRunStatus.Stopped;

    public static bool ShouldRecoverOnStartup(string status) =>
        status is AgentRunStatus.Queued or AgentRunStatus.Running or AgentRunStatus.AwaitingConfirmation;

    public static bool CanTransition(string from, string to)
    {
        if (IsTerminal(from)) return false;
        return (from, to) switch
        {
            (AgentRunStatus.Queued, AgentRunStatus.Running) => true,
            (AgentRunStatus.Queued, AgentRunStatus.Stopped) => true,
            (AgentRunStatus.Running, AgentRunStatus.AwaitingConfirmation) => true,
            (AgentRunStatus.Running, AgentRunStatus.Completed) => true,
            (AgentRunStatus.Running, AgentRunStatus.Failed) => true,
            (AgentRunStatus.Running, AgentRunStatus.Stopped) => true,
            (AgentRunStatus.Running, AgentRunStatus.Recoverable) => true,
            (AgentRunStatus.AwaitingConfirmation, AgentRunStatus.Running) => true,
            (AgentRunStatus.AwaitingConfirmation, AgentRunStatus.Stopped) => true,
            (AgentRunStatus.AwaitingConfirmation, AgentRunStatus.Recoverable) => true,
            (AgentRunStatus.Recoverable, AgentRunStatus.Queued) => true,
            (AgentRunStatus.Recoverable, AgentRunStatus.Stopped) => true,
            _ => false
        };
    }
}

public sealed record AgentRunEventDto(long Sequence, string Type, string DataJson, DateTime CreatedAt);

public sealed record AgentRunSnapshot(
    Guid Id,
    int SessionId,
    string Status,
    long LastSequence,
    string? FailureMessage,
    DateTime CreatedAt,
    DateTime? LastActivityAt,
    DateTime? RecoverableAt);
