using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Chat;

namespace MiraiNote.Core.Services.AgentRuns;

public interface IAgentRunService
{
    Task<AgentRunSnapshot> CreateAsync(int userId, int sessionId, SendMessageRequest request, CancellationToken ct);
    Task<AgentRunSnapshot> GetAsync(int userId, Guid runId, CancellationToken ct);
    Task<IReadOnlyList<AgentRunEventDto>> GetEventsAsync(int userId, Guid runId, long afterSequence, CancellationToken ct);
    Task<bool> StopAsync(int userId, Guid runId, CancellationToken ct);
    Task<bool> ConfirmAsync(int userId, Guid runId, bool confirmed, CancellationToken ct);
    Task<bool> ResumeAsync(int userId, Guid runId, CancellationToken ct);
    Task ExecuteAsync(Guid runId, CancellationToken stoppingToken);
    Task RecoverInterruptedRunsAsync(CancellationToken ct);
}

/// <summary>
/// Owns persistent run state. Its callers may disconnect at any time; only explicit stop changes a live run token.
/// </summary>
public sealed class AgentRunService : IAgentRunService
{
    private readonly MiraiNoteDbContext _db;
    private readonly AgentRunDispatcher _dispatcher;
    private readonly IChatService _chatService;
    private readonly ILogger<AgentRunService> _logger;

    public AgentRunService(MiraiNoteDbContext db, AgentRunDispatcher dispatcher, IChatService chatService, ILogger<AgentRunService> logger)
    {
        _db = db;
        _dispatcher = dispatcher;
        _chatService = chatService;
        _logger = logger;
    }

    public async Task<AgentRunSnapshot> CreateAsync(int userId, int sessionId, SendMessageRequest request, CancellationToken ct)
    {
        var exists = await _db.ChatSessions.AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (!exists) throw new BusinessException("对话不存在", 404);
        if (string.IsNullOrWhiteSpace(request.Content)) throw new BusinessException("消息内容不能为空", 400);

        var run = new AgentRun
        {
            UserId = userId,
            SessionId = sessionId,
            Status = AgentRunStatus.Queued,
            RequestJson = JsonSerializer.Serialize(request),
            LastActivityAt = DateTime.UtcNow
        };
        _db.AgentRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        _dispatcher.Enqueue(run.Id);
        return ToSnapshot(run, 0);
    }

    public async Task<AgentRunSnapshot> GetAsync(int userId, Guid runId, CancellationToken ct)
    {
        var run = await FindOwnedAsync(userId, runId, ct);
        var lastSequence = await _db.AgentRunEvents.Where(e => e.RunId == runId).Select(e => (long?)e.Sequence).MaxAsync(ct) ?? 0;
        return ToSnapshot(run, lastSequence);
    }

    public async Task<IReadOnlyList<AgentRunEventDto>> GetEventsAsync(int userId, Guid runId, long afterSequence, CancellationToken ct)
    {
        await FindOwnedAsync(userId, runId, ct);
        return await _db.AgentRunEvents.AsNoTracking()
            .Where(e => e.RunId == runId && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .Select(e => new AgentRunEventDto(e.Sequence, e.Type, e.DataJson, e.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<bool> StopAsync(int userId, Guid runId, CancellationToken ct)
    {
        var run = await FindOwnedAsync(userId, runId, ct);
        if (AgentRunState.IsTerminal(run.Status)) return false;
        run.Status = AgentRunStatus.Stopped;
        run.CompletedAt = DateTime.UtcNow;
        run.LastActivityAt = run.CompletedAt;
        await AppendEventAsync(run, "stopped", "{\"message\":\"已停止\"}", ct);
        await _db.SaveChangesAsync(ct);
        _dispatcher.Stop(runId);
        return true;
    }

    public async Task<bool> ConfirmAsync(int userId, Guid runId, bool confirmed, CancellationToken ct)
    {
        var run = await FindOwnedAsync(userId, runId, ct);
        if (run.Status != AgentRunStatus.AwaitingConfirmation) return false;
        run.Status = AgentRunStatus.Running;
        run.LastActivityAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return _dispatcher.Confirm(runId, confirmed);
    }

    public async Task<bool> ResumeAsync(int userId, Guid runId, CancellationToken ct)
    {
        var run = await FindOwnedAsync(userId, runId, ct);
        if (run.Status != AgentRunStatus.Recoverable) return false;
        run.Status = AgentRunStatus.Queued;
        run.RecoverableAt = null;
        run.LastActivityAt = DateTime.UtcNow;
        await AppendEventAsync(run, "context", "{\"message\":\"任务已恢复，正在继续执行\"}", ct);
        await _db.SaveChangesAsync(ct);
        _dispatcher.Enqueue(runId);
        return true;
    }

    public async Task RecoverInterruptedRunsAsync(CancellationToken ct)
    {
        var runs = await _db.AgentRuns
            .Where(r => r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running || r.Status == AgentRunStatus.AwaitingConfirmation)
            .ToListAsync(ct);
        foreach (var run in runs)
        {
            run.Status = AgentRunStatus.Recoverable;
            run.RecoverableAt = DateTime.UtcNow;
            run.LastActivityAt = run.RecoverableAt;
            await AppendEventAsync(run, "context", "{\"message\":\"服务重启，任务可继续执行\",\"recoverable\":true}", ct);
        }
        if (runs.Count > 0) await _db.SaveChangesAsync(ct);
    }

    public async Task ExecuteAsync(Guid runId, CancellationToken stoppingToken)
    {
        var run = await _db.AgentRuns.FirstOrDefaultAsync(r => r.Id == runId, stoppingToken);
        if (run == null || run.Status != AgentRunStatus.Queued) return;

        var request = JsonSerializer.Deserialize<SendMessageRequest>(run.RequestJson);
        if (request == null)
        {
            await MarkFailedAsync(run, "任务参数无法恢复", stoppingToken);
            return;
        }

        using var execution = _dispatcher.Begin(runId, stoppingToken);
        run.Status = AgentRunStatus.Running;
        run.StartedAt ??= DateTime.UtcNow;
        run.LastActivityAt = DateTime.UtcNow;
        await AppendEventAsync(run, "context", "{\"message\":\"任务开始执行\"}", execution.Token);
        await _db.SaveChangesAsync(execution.Token);

        async Task Callback(string type, string data)
        {
            if (run.Status == AgentRunStatus.Stopped) return;
            await AppendEventAsync(run, type, data, execution.Token);
            if (type == "user_msg") run.UserMessageId = ReadInt(data, "id") ?? run.UserMessageId;
            if (type == "tool_result") run.CheckpointJson = data;
            if (type == "done")
            {
                run.AssistantMessageId = ReadInt(data, "messageId") ?? run.AssistantMessageId;
                run.Status = AgentRunStatus.Completed;
                run.CompletedAt = DateTime.UtcNow;
            }
            else if (type == "error")
            {
                run.Status = AgentRunStatus.Failed;
                run.FailureMessage = ReadString(data, "message");
                run.CompletedAt = DateTime.UtcNow;
            }
            else if (type == "stopped")
            {
                run.Status = AgentRunStatus.Stopped;
                run.CompletedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(execution.Token);
        }

        try
        {
            // ExistingUserMessageId prevents a recovered run from writing its user prompt twice.
            request.ExistingUserMessageId = run.UserMessageId;
            request.ResumeCheckpointJson = run.CheckpointJson;
            await _chatService.SendMessageAgentStreamAsync(
                run.UserId, run.SessionId, request, Callback,
                () => WaitForConfirmationAsync(run, execution.Token), execution.Token);
            if (!AgentRunState.IsTerminal(run.Status) && !execution.Token.IsCancellationRequested)
                await MarkFailedAsync(run, "任务未返回完成状态", execution.Token);
        }
        catch (OperationCanceledException) when (execution.Token.IsCancellationRequested)
        {
            // Host shutdown is intentionally not a user stop. Leave the durable state
            // non-terminal so the next process marks it recoverable instead of replaying it.
            if (stoppingToken.IsCancellationRequested)
                return;
            if (run.Status != AgentRunStatus.Stopped)
                await MarkStoppedAsync(run, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent run {RunId} 执行失败", runId);
            await MarkFailedAsync(run, "任务执行失败，请稍后重试", CancellationToken.None);
        }
    }

    private async Task<bool> WaitForConfirmationAsync(AgentRun run, CancellationToken ct)
    {
        run.Status = AgentRunStatus.AwaitingConfirmation;
        run.LastActivityAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await _dispatcher.WaitForConfirmationAsync(run.Id, ct);
    }

    private async Task MarkStoppedAsync(AgentRun run, CancellationToken ct)
    {
        if (AgentRunState.IsTerminal(run.Status)) return;
        run.Status = AgentRunStatus.Stopped;
        run.CompletedAt = run.LastActivityAt = DateTime.UtcNow;
        await AppendEventAsync(run, "stopped", "{\"message\":\"已停止\"}", ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task MarkFailedAsync(AgentRun run, string message, CancellationToken ct)
    {
        if (AgentRunState.IsTerminal(run.Status)) return;
        run.Status = AgentRunStatus.Failed;
        run.FailureMessage = message;
        run.CompletedAt = run.LastActivityAt = DateTime.UtcNow;
        await AppendEventAsync(run, "error", JsonSerializer.Serialize(new { message }), ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task AppendEventAsync(AgentRun run, string type, string dataJson, CancellationToken ct)
    {
        var next = (await _db.AgentRunEvents.Where(e => e.RunId == run.Id).Select(e => (long?)e.Sequence).MaxAsync(ct) ?? 0) + 1;
        _db.AgentRunEvents.Add(new AgentRunEvent { RunId = run.Id, Sequence = next, Type = type, DataJson = dataJson });
        run.LastActivityAt = DateTime.UtcNow;
        _dispatcher.Signal(run.Id);
    }

    private async Task<AgentRun> FindOwnedAsync(int userId, Guid runId, CancellationToken ct) =>
        await _db.AgentRuns.FirstOrDefaultAsync(r => r.Id == runId && r.UserId == userId, ct)
        ?? throw new BusinessException("任务不存在", 404);

    private static AgentRunSnapshot ToSnapshot(AgentRun run, long lastSequence) =>
        new(run.Id, run.SessionId, run.Status, lastSequence, run.FailureMessage, run.CreatedAt, run.LastActivityAt, run.RecoverableAt);

    private static int? ReadInt(string json, string name) =>
        JsonDocument.Parse(json).RootElement.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static string? ReadString(string json, string name) =>
        JsonDocument.Parse(json).RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
}

/// <summary>Live-only coordination: queue, cancellation and pending confirmation. Durable state stays in SQL.</summary>
public sealed class AgentRunDispatcher
{
    private readonly System.Threading.Channels.Channel<Guid> _queue = System.Threading.Channels.Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _confirmations = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _signals = new();

    public void Enqueue(Guid runId) => _queue.Writer.TryWrite(runId);
    public ValueTask<Guid> DequeueAsync(CancellationToken ct) => _queue.Reader.ReadAsync(ct);
    public void Signal(Guid runId)
    {
        var signal = _signals.GetOrAdd(runId, _ => NewSignal());
        signal.TrySetResult();
        _signals[runId] = NewSignal();
    }
    public Task WaitForSignalAsync(Guid runId, CancellationToken ct) => _signals.GetOrAdd(runId, _ => NewSignal()).Task.WaitAsync(ct);
    public bool Stop(Guid runId) => _cancellations.TryGetValue(runId, out var cts) && TryCancel(cts);
    public bool Confirm(Guid runId, bool confirmed) => _confirmations.TryRemove(runId, out var tcs) && tcs.TrySetResult(confirmed);
    public async Task<bool> WaitForConfirmationAsync(Guid runId, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirmations[runId] = tcs;
        try { return await tcs.Task.WaitAsync(ct); }
        finally { _confirmations.TryRemove(runId, out _); }
    }
    public RunLease Begin(Guid runId, CancellationToken stoppingToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        if (!_cancellations.TryAdd(runId, cts)) throw new InvalidOperationException("任务已在执行");
        return new RunLease(_cancellations, runId, cts);
    }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static bool TryCancel(CancellationTokenSource cts) { try { cts.Cancel(); return true; } catch (ObjectDisposedException) { return false; } }
    public sealed class RunLease(ConcurrentDictionary<Guid, CancellationTokenSource> runs, Guid id, CancellationTokenSource cts) : IDisposable
    {
        public CancellationToken Token => cts.Token;
        public void Dispose() { runs.TryRemove(id, out _); cts.Dispose(); }
    }
}
