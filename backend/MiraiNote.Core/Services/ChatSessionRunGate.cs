using System.Collections.Concurrent;

namespace MiraiNote.Core.Services;

/// <summary>
/// 同一用户会话同时只允许一个生成 run；新 run 进入时取消旧 run，避免停止后仍续跑或双流写库。
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
            if (_runs.TryRemove(key, out var previous))
            {
                try { previous.Cancel(); }
                catch (ObjectDisposedException) { /* ignore */ }
                previous.Dispose();
            }

            if (_runs.TryAdd(key, linked))
            {
                runCt = linked.Token;
                return new Lease(this, key, linked);
            }
        }
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
            _cts.Dispose();
        }
    }
}
