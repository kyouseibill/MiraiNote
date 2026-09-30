namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 当前请求正在发送的对话 Id。家务草稿记在这次会话上，刷新后才能把卡片找回来。
/// </summary>
public static class HouseholdChatAmbient
{
    private static readonly AsyncLocal<int?> Current = new();

    public static int? SessionId => Current.Value is > 0 ? Current.Value : null;

    public static IDisposable Push(int sessionId)
    {
        var previous = Current.Value;
        Current.Value = sessionId;
        return new Restore(previous);
    }

    private sealed class Restore(int? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
