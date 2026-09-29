namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 把提醒日志里的失败摘要收成设置页能显示的分类。只返回固定短句，不回显原文。
/// </summary>
internal static class HouseholdDeliveryFailure
{
    public const string TimedOut = "超时";
    public const string Unreachable = "连接失败";
    public const string SendFailed = "发送失败";

    public static string Classify(string? summary)
    {
        if (string.IsNullOrEmpty(summary))
            return SendFailed;
        if (summary.Contains("TaskCanceledException", StringComparison.Ordinal)
            || summary.Contains("TimeoutException", StringComparison.Ordinal))
            return TimedOut;
        if (summary.Contains("HttpRequestException", StringComparison.Ordinal))
            return Unreachable;
        return SendFailed;
    }
}
