using Xunit;
using MiraiNote.Core.Services;

namespace MiraiNote.Tests;

public class ChatSessionRunGateTests
{
    [Fact]
    public void Enter_CancelsPreviousRunForSameKey()
    {
        var gate = new ChatSessionRunGate();
        using var firstLinked = new CancellationTokenSource();
        var lease1 = gate.Enter(ChatSessionRunGate.SessionKey(1, 9), firstLinked.Token, out var run1);
        Assert.False(run1.IsCancellationRequested);

        using var secondLinked = new CancellationTokenSource();
        using var lease2 = gate.Enter(ChatSessionRunGate.SessionKey(1, 9), secondLinked.Token, out var run2);
        Assert.True(run1.IsCancellationRequested);
        Assert.False(run2.IsCancellationRequested);

        lease1.Dispose();
        Assert.False(run2.IsCancellationRequested);
    }

    [Fact]
    public void DifferentKeys_DoNotCancelEachOther()
    {
        var gate = new ChatSessionRunGate();
        using var a = new CancellationTokenSource();
        using var b = new CancellationTokenSource();
        using var leaseA = gate.Enter(ChatSessionRunGate.SessionKey(1, 1), a.Token, out var runA);
        using var leaseB = gate.Enter(ChatSessionRunGate.SessionKey(1, 2), b.Token, out var runB);
        Assert.False(runA.IsCancellationRequested);
        Assert.False(runB.IsCancellationRequested);
    }
}
