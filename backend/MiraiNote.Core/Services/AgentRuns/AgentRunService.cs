using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiraiNote.Core.Services.ChatModels;
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
    Task WaitForUpdateAsync(Guid runId, int observedSignalVersion, TimeSpan timeout, CancellationToken ct);
    int CurrentSignalVersion(Guid runId);
}

/// <summary>
/// Owns persistent run state. Its callers may disconnect at any time; only explicit stop changes a live run token.
/// </summary>
public sealed class AgentRunService : IAgentRunService
{
    private const int TokenCoalesceChars = 512;
    private const long TokenCoalesceWindowMs = 100;

    private readonly MiraiNoteDbContext _db;
    private readonly AgentRunDispatcher _dispatcher;
    private readonly AgentRunAdmissionQueue _admission;
    private readonly IChatService _chatService;
    private readonly IChatModelRegistry _modelRegistry;
    private readonly ILogger<AgentRunService> _logger;
    private readonly IServiceScopeFactory? _scopes;
    private readonly StringBuilder _tokenBuffer = new();
    private long _nextSequence;
    private bool _ownsSequence;
    private long _tokenBufferedSince;

    public AgentRunService(
        MiraiNoteDbContext db,
        AgentRunDispatcher dispatcher,
        AgentRunAdmissionQueue admission,
        IChatService chatService,
        IChatModelRegistry modelRegistry,
        ILogger<AgentRunService> logger,
        IServiceScopeFactory? scopes = null)
    {
        _db = db;
        _dispatcher = dispatcher;
        _admission = admission;
        _chatService = chatService;
        _modelRegistry = modelRegistry;
        _logger = logger;
        _scopes = scopes;
    }

    public async Task<AgentRunSnapshot> CreateAsync(int userId, int sessionId, SendMessageRequest request, CancellationToken ct)
    {
        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw new BusinessException("对话不存在", 404);
        if (string.IsNullOrWhiteSpace(request.Content) &&
            request.Attachments?.Any(attachment => attachment.IsImage && !string.IsNullOrWhiteSpace(attachment.DataUrl)) != true)
            throw new BusinessException("消息内容不能为空", 400);
        var model = _modelRegistry.ResolveForExistingSession(session.AiProvider, session.AiModel);
        if (!model.SupportsWork || !model.SupportsTools)
            throw new ChatModelUnavailableException("所选模型不支持工作模式，请创建新对话并选择其他模型。");
        ChatImagePolicy.Validate(request, model.Provider, model.ModelId);
        if (!string.Equals(session.AiProvider, model.Provider, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(session.AiModel, model.ModelId, StringComparison.OrdinalIgnoreCase))
        {
            session.AiProvider = model.Provider;
            session.AiModel = model.ModelId;
        }

        var run = new AgentRun
        {
            UserId = userId,
            SessionId = sessionId,
            AiProvider = model.Provider,
            AiModel = model.ModelId,
            Status = AgentRunStatus.Queued,
            RequestJson = JsonSerializer.Serialize(request),
            LastActivityAt = DateTime.UtcNow
        };
        _db.AgentRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        _admission.Enqueue(run.Id, run.UserId);
        return ToSnapshot(run, 0, _admission.GetPosition(run.Id));
    }

    public async Task<AgentRunSnapshot> GetAsync(int userId, Guid runId, CancellationToken ct)
    {
        // AsNoTracking：SSE 轮询不能复用第一次读到的 Queued 实体，否则完成后流不会结束。
        var run = await _db.AgentRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId && r.UserId == userId, ct)
            ?? throw new BusinessException("任务不存在", 404);
        var lastSequence = await _db.AgentRunEvents.AsNoTracking()
            .Where(e => e.RunId == runId)
            .Select(e => (long?)e.Sequence)
            .MaxAsync(ct) ?? 0;
        var position = run.Status == AgentRunStatus.Queued ? _admission.GetPosition(run.Id) : null;
        return ToSnapshot(run, lastSequence, position);
    }

    public async Task<IReadOnlyList<AgentRunEventDto>> GetEventsAsync(int userId, Guid runId, long afterSequence, CancellationToken ct)
    {
        var owned = await _db.AgentRuns.AsNoTracking()
            .AnyAsync(r => r.Id == runId && r.UserId == userId, ct);
        if (!owned) throw new BusinessException("任务不存在", 404);
        return await _db.AgentRunEvents.AsNoTracking()
            .Where(e => e.RunId == runId && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .Select(e => new AgentRunEventDto(e.Sequence, e.Type, e.DataJson, e.CreatedAt))
            .ToListAsync(ct);
    }

    public int CurrentSignalVersion(Guid runId) => _dispatcher.SignalVersion(runId);

    public Task WaitForUpdateAsync(Guid runId, int observedSignalVersion, TimeSpan timeout, CancellationToken ct) =>
        _dispatcher.WaitForSignalSinceAsync(runId, observedSignalVersion, timeout, ct);

    public async Task<bool> StopAsync(int userId, Guid runId, CancellationToken ct)
    {
        var run = await FindOwnedTrackedAsync(userId, runId, ct);
        if (AgentRunState.IsTerminal(run.Status)) return false;
        run.Status = AgentRunStatus.Stopped;
        run.CompletedAt = DateTime.UtcNow;
        run.LastActivityAt = run.CompletedAt;
        await AppendEventAsync(run, "stopped", "{\"message\":\"已停止\"}", ct);
        await SaveChangesDetachingFailedEventsAsync(ct);
        _dispatcher.Signal(runId);
        _dispatcher.Stop(runId);
        return true;
    }

    public async Task<bool> ConfirmAsync(int userId, Guid runId, bool confirmed, CancellationToken ct)
    {
        var run = await FindOwnedTrackedAsync(userId, runId, ct);
        if (run.Status != AgentRunStatus.AwaitingConfirmation) return false;
        run.Status = AgentRunStatus.Running;
        run.LastActivityAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _dispatcher.Signal(runId);
        return _dispatcher.Confirm(runId, confirmed);
    }

    public async Task<bool> ResumeAsync(int userId, Guid runId, CancellationToken ct)
    {
        var run = await FindOwnedTrackedAsync(userId, runId, ct);
        if (run.Status != AgentRunStatus.Recoverable) return false;
        run.Status = AgentRunStatus.Queued;
        run.RecoverableAt = null;
        run.LastActivityAt = DateTime.UtcNow;
        await AppendEventAsync(run, "context", "{\"message\":\"任务已恢复，正在继续执行\"}", ct);
        await SaveChangesDetachingFailedEventsAsync(ct);
        _dispatcher.Signal(runId);
        _admission.Enqueue(runId, run.UserId);
        return true;
    }

    public async Task RecoverInterruptedRunsAsync(CancellationToken ct)
    {
        var runs = await _db.AgentRuns.AsNoTracking()
            .Where(r => r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running || r.Status == AgentRunStatus.AwaitingConfirmation)
            .Select(r => new { r.Id, r.Status })
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var row in runs)
        {
            // 条件更新：已经从 Queued 被 worker 领走的任务不会被覆盖回 recoverable。
            var updated = await _db.AgentRuns
                .Where(r => r.Id == row.Id && r.Status == row.Status)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, AgentRunStatus.Recoverable)
                    .SetProperty(r => r.RecoverableAt, now)
                    .SetProperty(r => r.LastActivityAt, now), ct);
            if (updated != 1) continue;

            var run = await _db.AgentRuns.FirstAsync(r => r.Id == row.Id, ct);
            await AppendEventAsync(run, "context", "{\"message\":\"服务重启，任务可继续执行\",\"recoverable\":true}", ct);
            await SaveChangesDetachingFailedEventsAsync(ct);
            _dispatcher.Signal(run.Id);
        }
    }

    public async Task ExecuteAsync(Guid runId, CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;
        var claimed = await _db.AgentRuns
            .Where(r => r.Id == runId && r.Status == AgentRunStatus.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, AgentRunStatus.Running)
                .SetProperty(r => r.LastActivityAt, now)
                .SetProperty(r => r.StartedAt, r => r.StartedAt ?? now), stoppingToken);
        if (claimed != 1) return;

        // ExecuteUpdate 不刷新跟踪器。同一上下文里若还留着 Queued 实体，后续 SaveChanges 会把它写回去。
        foreach (var stale in _db.ChangeTracker.Entries<AgentRun>().Where(e => e.Entity.Id == runId).ToList())
            stale.State = EntityState.Detached;

        var run = await _db.AgentRuns.FirstOrDefaultAsync(r => r.Id == runId, stoppingToken);
        if (run == null) return;

        _nextSequence = await _db.AgentRunEvents.AsNoTracking()
            .Where(e => e.RunId == runId)
            .Select(e => (long?)e.Sequence)
            .MaxAsync(stoppingToken) ?? 0;
        _ownsSequence = true;
        _tokenBuffer.Clear();
        _tokenBufferedSince = Environment.TickCount64;

        var request = JsonSerializer.Deserialize<SendMessageRequest>(run.RequestJson);
        if (request == null)
        {
            await MarkFailedAsync(run, "任务参数无法恢复", stoppingToken);
            return;
        }

        using var execution = _dispatcher.Begin(runId, stoppingToken);
        try
        {
            await AppendAndSaveAsync(run, "context", "{\"message\":\"任务开始执行\"}", execution.Token);

            async Task Callback(string type, string data)
            {
                if (execution.Token.IsCancellationRequested) return;
                if (type == "token")
                {
                    var chunk = TryReadString(data, "content");
                    if (!string.IsNullOrEmpty(chunk))
                        _tokenBuffer.Append(chunk);
                    if (ShouldFlushTokens())
                        await FlushTokensAsync(run, execution.Token);
                    return;
                }

                await FlushTokensAsync(run, execution.Token);
                var previous = SnapshotMutable(run);
                ApplyRunUpdate(run, type, data);
                try
                {
                    await AppendAndSaveAsync(run, type, data, execution.Token);
                }
                catch
                {
                    RestoreMutable(run, previous);
                    throw;
                }
            }

            request.ExistingUserMessageId = run.UserMessageId;
            request.ResumeCheckpointJson = run.CheckpointJson;
            await _chatService.SendMessageAgentStreamAsync(
                run.UserId, run.SessionId, request, Callback,
                () => WaitForConfirmationAsync(run, execution.Token), execution.Token);
            await FlushTokensAsync(run, execution.Token);
            if (!AgentRunState.IsTerminal(run.Status) && !execution.Token.IsCancellationRequested)
                await MarkFailedAsync(run, "任务未返回完成状态", execution.Token);
        }
        catch (OperationCanceledException) when (execution.Token.IsCancellationRequested)
        {
            // Host shutdown is intentionally not a user stop. Leave the durable state
            // non-terminal so the next process marks it recoverable instead of replaying it.
            if (stoppingToken.IsCancellationRequested)
                return;
            DiscardAddedEvents();
            try { await FlushTokensAsync(run, CancellationToken.None); }
            catch (Exception flushEx)
            {
                _logger.LogWarning(flushEx, "Agent run {RunId} 停止前刷新 token 失败", runId);
                DiscardAddedEvents();
            }
            if (run.Status != AgentRunStatus.Stopped)
                await MarkStoppedAsync(run, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent run {RunId} 执行失败", runId);
            DiscardAddedEvents();
            await MarkFailedAsync(run, "任务执行失败，请稍后重试", CancellationToken.None);
        }
    }

    private bool ShouldFlushTokens() =>
        _tokenBuffer.Length >= TokenCoalesceChars
        || Environment.TickCount64 - _tokenBufferedSince >= TokenCoalesceWindowMs;

    private async Task FlushTokensAsync(AgentRun run, CancellationToken ct)
    {
        if (_tokenBuffer.Length == 0) return;
        var content = _tokenBuffer.ToString();
        _tokenBuffer.Clear();
        _tokenBufferedSince = Environment.TickCount64;
        await AppendAndSaveAsync(run, "token", JsonSerializer.Serialize(new { content }), ct);
    }

    private async Task<bool> WaitForConfirmationAsync(AgentRun run, CancellationToken ct)
    {
        await FlushTokensAsync(run, ct);
        run.Status = AgentRunStatus.AwaitingConfirmation;
        run.LastActivityAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _dispatcher.Signal(run.Id);
        return await _dispatcher.WaitForConfirmationAsync(run.Id, ct);
    }

    private async Task MarkStoppedAsync(AgentRun run, CancellationToken ct)
    {
        if (AgentRunState.IsTerminal(run.Status)) return;
        DiscardAddedEvents();
        run.Status = AgentRunStatus.Stopped;
        run.CompletedAt = run.LastActivityAt = DateTime.UtcNow;
        await AppendAndSaveAsync(run, "stopped", "{\"message\":\"已停止\"}", ct);
    }

    private async Task MarkFailedAsync(AgentRun run, string message, CancellationToken ct)
    {
        if (AgentRunState.IsTerminal(run.Status)) return;
        DiscardAddedEvents();
        run.Status = AgentRunStatus.Failed;
        run.FailureMessage = message;
        run.CompletedAt = run.LastActivityAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(new { message });
        try
        {
            await AppendAndSaveAsync(run, "error", json, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent run {RunId} 失败标记写入被拒绝，改用干净上下文重试", run.Id);
            DiscardAddedEvents();
            await WriteTerminalInFreshScopeAsync(run.Id, AgentRunStatus.Failed, message, "error", json);
        }
    }

    private async Task WriteTerminalInFreshScopeAsync(Guid runId, string status, string? failure, string eventType, string dataJson)
    {
        if (_scopes == null) return;
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MiraiNoteDbContext>();
        var run = await db.AgentRuns.FirstOrDefaultAsync(r => r.Id == runId);
        if (run == null || AgentRunState.IsTerminal(run.Status)) return;

        var dbMax = await db.AgentRunEvents.AsNoTracking()
            .Where(e => e.RunId == runId)
            .Select(e => (long?)e.Sequence)
            .MaxAsync() ?? 0;
        var sequence = Math.Max(dbMax, _ownsSequence ? _nextSequence : 0) + 1;
        if (_ownsSequence) _nextSequence = sequence;

        run.Status = status;
        if (status == AgentRunStatus.Failed) run.FailureMessage = failure;
        run.CompletedAt = run.LastActivityAt = DateTime.UtcNow;
        db.AgentRunEvents.Add(new AgentRunEvent
        {
            RunId = runId,
            Sequence = sequence,
            Type = eventType,
            DataJson = dataJson
        });
        await db.SaveChangesAsync();
        _dispatcher.Signal(runId);
    }

    private async Task AppendAndSaveAsync(AgentRun run, string type, string dataJson, CancellationToken ct)
    {
        await AppendEventAsync(run, type, dataJson, ct);
        await SaveChangesDetachingFailedEventsAsync(ct);
        _dispatcher.Signal(run.Id);
    }

    private async Task AppendEventAsync(AgentRun run, string type, string dataJson, CancellationToken ct)
    {
        long sequence;
        if (_ownsSequence)
        {
            // 失败后序号只增不减，避免把没写成功的 Sequence 再交给失败标记。
            sequence = ++_nextSequence;
        }
        else
        {
            sequence = (await _db.AgentRunEvents.AsNoTracking()
                .Where(e => e.RunId == run.Id)
                .Select(e => (long?)e.Sequence)
                .MaxAsync(ct) ?? 0) + 1;
        }

        _db.AgentRunEvents.Add(new AgentRunEvent
        {
            RunId = run.Id,
            Sequence = sequence,
            Type = type,
            DataJson = dataJson
        });
        run.LastActivityAt = DateTime.UtcNow;
    }

    private async Task SaveChangesDetachingFailedEventsAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // 失败的 Added 事件若留在跟踪器里，下次 SaveChanges 会连同失败标记一起重试，撞上唯一索引。
            DiscardAddedEvents();
            throw;
        }

        DetachPersistedEvents();
    }

    private void DiscardAddedEvents()
    {
        foreach (var entry in _db.ChangeTracker.Entries<AgentRunEvent>().ToList())
        {
            if (entry.State != EntityState.Added) continue;
            var entity = entry.Entity;
            entry.State = EntityState.Detached;
            foreach (var runEntry in _db.ChangeTracker.Entries<AgentRun>().ToList())
                runEntry.Entity.Events.Remove(entity);
        }
    }

    private void DetachPersistedEvents()
    {
        foreach (var entry in _db.ChangeTracker.Entries<AgentRunEvent>().ToList())
        {
            if (entry.State != EntityState.Unchanged) continue;
            var entity = entry.Entity;
            entry.State = EntityState.Detached;
            foreach (var runEntry in _db.ChangeTracker.Entries<AgentRun>().ToList())
                runEntry.Entity.Events.Remove(entity);
        }
    }

    private async Task<AgentRun> FindOwnedTrackedAsync(int userId, Guid runId, CancellationToken ct) =>
        await _db.AgentRuns.FirstOrDefaultAsync(r => r.Id == runId && r.UserId == userId, ct)
        ?? throw new BusinessException("任务不存在", 404);

    private static AgentRunSnapshot ToSnapshot(AgentRun run, long lastSequence, int? queuePosition = null) =>
        new(run.Id, run.SessionId, run.Status, lastSequence, run.FailureMessage, run.CreatedAt, run.LastActivityAt, run.RecoverableAt, queuePosition);

    private static void ApplyRunUpdate(AgentRun run, string type, string data)
    {
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
            run.FailureMessage = TryReadString(data, "message");
            run.CompletedAt = DateTime.UtcNow;
        }
        else if (type == "stopped")
        {
            run.Status = AgentRunStatus.Stopped;
            run.CompletedAt = DateTime.UtcNow;
        }
    }

    private readonly record struct MutableSnapshot(
        string Status,
        string? FailureMessage,
        DateTime? CompletedAt,
        int? UserMessageId,
        int? AssistantMessageId,
        string? CheckpointJson);

    private static MutableSnapshot SnapshotMutable(AgentRun run) =>
        new(run.Status, run.FailureMessage, run.CompletedAt, run.UserMessageId, run.AssistantMessageId, run.CheckpointJson);

    private static void RestoreMutable(AgentRun run, MutableSnapshot snapshot)
    {
        run.Status = snapshot.Status;
        run.FailureMessage = snapshot.FailureMessage;
        run.CompletedAt = snapshot.CompletedAt;
        run.UserMessageId = snapshot.UserMessageId;
        run.AssistantMessageId = snapshot.AssistantMessageId;
        run.CheckpointJson = snapshot.CheckpointJson;
    }

    private static int? ReadInt(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    }

    private static string? TryReadString(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Live-only coordination: cancellation, confirmation, and subscriber wakeups. Durable state stays in SQL.</summary>
public sealed class AgentRunDispatcher
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _confirmations = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SignalSlot> _signals = new();

    public void Signal(Guid runId)
    {
        var slot = _signals.GetOrAdd(runId, static _ => new SignalSlot());
        lock (slot)
        {
            slot.Version++;
            slot.Pulse.TrySetResult();
            slot.Pulse = NewSignal();
        }
    }

    public int SignalVersion(Guid runId)
    {
        var slot = _signals.GetOrAdd(runId, static _ => new SignalSlot());
        lock (slot) return slot.Version;
    }

    public async Task WaitForSignalSinceAsync(Guid runId, int observedVersion, TimeSpan timeout, CancellationToken ct)
    {
        var slot = _signals.GetOrAdd(runId, static _ => new SignalSlot());
        Task task;
        lock (slot)
        {
            if (slot.Version != observedVersion) return;
            task = slot.Pulse.Task;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 没有新事件时按间隔再读一次库，避免信号丢失后一直挂着。
        }
    }

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

    private sealed class SignalSlot
    {
        public int Version;
        public TaskCompletionSource Pulse = NewSignal();
    }

    public sealed class RunLease(System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> runs, Guid id, CancellationTokenSource cts) : IDisposable
    {
        public CancellationToken Token => cts.Token;
        public void Dispose() { runs.TryRemove(id, out _); cts.Dispose(); }
    }
}
