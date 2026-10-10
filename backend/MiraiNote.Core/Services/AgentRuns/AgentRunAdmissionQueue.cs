namespace MiraiNote.Core.Services.AgentRuns;

/// <summary>
/// 进程内 FIFO 准入队列。全局与每用户上限同时生效；队头属于已满用户时，跳过它启动后面第一个可运行的任务。
/// </summary>
public sealed class AgentRunAdmissionQueue
{
    private readonly object _gate = new();
    private readonly LinkedList<PendingRun> _pending = new();
    private readonly Dictionary<int, int> _runningByUser = new();
    private readonly HashSet<Guid> _runningIds = new();
    private readonly int _maxConcurrent;
    private readonly int _maxPerUser;
    private int _running;
    private int _version;
    private TaskCompletionSource _pulse = NewPulse();

    public AgentRunAdmissionQueue(int maxConcurrent, int maxPerUser)
    {
        _maxConcurrent = Math.Max(1, maxConcurrent);
        _maxPerUser = Math.Max(1, maxPerUser);
    }

    public int MaxConcurrent => _maxConcurrent;
    public int MaxPerUser => _maxPerUser;

    public int Version
    {
        get { lock (_gate) return _version; }
    }

    public void Enqueue(Guid runId, int userId)
    {
        lock (_gate)
        {
            if (runId == Guid.Empty || _runningIds.Contains(runId) || ContainsPending(runId))
                return;
            _pending.AddLast(new PendingRun(runId, userId));
            BumpNoLock();
        }
    }

    public int RunningCount
    {
        get { lock (_gate) return _running; }
    }

    public int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    public bool IsRunning(Guid runId)
    {
        lock (_gate) return _runningIds.Contains(runId);
    }

    /// <summary>看一眼下一个可运行任务，不把它移出队列，也不占用名额。</summary>
    public bool TryPeekEligible(out PendingRun peeked)
    {
        lock (_gate)
        {
            peeked = default;
            if (_running >= _maxConcurrent) return false;

            var node = _pending.First;
            while (node != null)
            {
                var candidate = node.Value;
                if (_runningByUser.GetValueOrDefault(candidate.UserId) < _maxPerUser)
                {
                    peeked = candidate;
                    return true;
                }

                node = node.Next;
            }

            return false;
        }
    }

    /// <summary>
    /// 数据库已经确认任务仍是 queued 并领走之后，才把它记入正在执行的名额。
    /// 任务已不在等待队列里时返回 false，不占用名额。
    /// </summary>
    public bool TryReservePending(Guid runId, int userId)
    {
        lock (_gate)
        {
            var node = _pending.First;
            while (node != null)
            {
                if (node.Value.RunId == runId)
                {
                    _pending.Remove(node);
                    _running++;
                    _runningByUser[userId] = _runningByUser.GetValueOrDefault(userId) + 1;
                    _runningIds.Add(runId);
                    return true;
                }

                node = node.Next;
            }

            return false;
        }
    }

    /// <summary>停止或取消仍在排队的任务时立刻移出等待队列，不触碰正在执行的名额。</summary>
    public bool TryRemovePending(Guid runId)
    {
        lock (_gate)
        {
            var node = _pending.First;
            while (node != null)
            {
                if (node.Value.RunId == runId)
                {
                    _pending.Remove(node);
                    BumpNoLock();
                    return true;
                }

                node = node.Next;
            }

            return false;
        }
    }

    public void Release(Guid runId, int userId)
    {
        lock (_gate)
        {
            if (!_runningIds.Remove(runId)) return;
            _running = Math.Max(0, _running - 1);
            if (_runningByUser.TryGetValue(userId, out var count))
            {
                if (count <= 1) _runningByUser.Remove(userId);
                else _runningByUser[userId] = count - 1;
            }

            BumpNoLock();
        }
    }

    /// <summary>1-based position among runs still waiting. Null once the run is admitted or unknown.</summary>
    public int? GetPosition(Guid runId)
    {
        lock (_gate)
        {
            var position = 1;
            foreach (var pending in _pending)
            {
                if (pending.RunId == runId) return position;
                position++;
            }

            return null;
        }
    }

    public async Task WaitForChangeAsync(int observedVersion, CancellationToken ct)
    {
        Task task;
        lock (_gate)
        {
            if (_version != observedVersion) return;
            task = _pulse.Task;
        }

        await task.WaitAsync(ct);
    }

    private bool ContainsPending(Guid runId)
    {
        foreach (var pending in _pending)
        {
            if (pending.RunId == runId) return true;
        }

        return false;
    }

    private void BumpNoLock()
    {
        _version++;
        _pulse.TrySetResult();
        _pulse = NewPulse();
    }

    private static TaskCompletionSource NewPulse() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public readonly record struct PendingRun(Guid RunId, int UserId);
}
