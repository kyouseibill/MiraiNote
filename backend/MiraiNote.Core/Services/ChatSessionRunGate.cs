using System.Collections.Concurrent;

namespace MiraiNote.Core.Services;

/// <summary>
/// 同一用户会话同时只允许一个生成 run；新 run 进入时取消旧 run，避免停止后仍续跑或双流写库。
/// 也可通过 Cancel 主动取消当前 run（前端点「停止」），不依赖 TCP/代理是否立刻断开。
/// </summary>
public sealed class ChatSessionRunGate
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runs = new(StringComparer.Ordinal);

    public static string SessionKey(int userId, int sessionId) => $"s:{userId}:{sessionId}";

    public static string TemporaryKey(int userId, string temporaryId) =>
        $"t:{userId}:{temporaryId}";

    public IDisposable Enter(string key, CancellationToken requestCt, out CancellationToken runCt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
        while (true)
        {
            CancelAndRemove(key);

            if (_runs.TryAdd(key, linked))
            {
                runCt = linked.Token;
                return new Lease(this, key, linked);
            }
        }
    }

    /// <summary>
    /// 取消指定会话当前 run（若有）。返回是否找到并取消了活跃 run。
    /// </summary>
    public bool Cancel(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return CancelAndRemove(key);
    }

    private bool CancelAndRemove(string key)
    {
        if (!_runs.TryRemove(key, out var previous))
            return false;

        try { previous.Cancel(); }
        catch (ObjectDisposedException) { /* ignore */ }
        try { previous.Dispose(); }
        catch (ObjectDisposedException) { /* ignore */ }
        return true;
    }

    private sealed class Lease : IDisposable
    {
        private readonly ChatSessionRunGate _gate;
        private readonly string _key;
        private readonly CancellationTokenSource _cts;
        private int _disposed;

        public Lease(ChatSessionRunGate gate, string key, CancellationTokenSource cts)
        {
            _gate = gate;
            _key = key;
            _cts = cts;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_gate._runs.TryGetValue(_key, out var current) && ReferenceEquals(current, _cts))
                _gate._runs.TryRemove(_key, out _);
            try { _cts.Dispose(); }
            catch (ObjectDisposedException) { /* ignore */ }
        }
    }
}
