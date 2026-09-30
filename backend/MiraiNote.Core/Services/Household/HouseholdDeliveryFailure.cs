using System.Net.Sockets;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 把提醒日志里的失败摘要收成设置页能显示的分类。只返回固定短句，不回显原文。
/// </summary>
internal static class HouseholdDeliveryFailure
{
    public const string TimedOut = "超时";
    public const string Unreachable = "连接失败";
    public const string SendFailed = "发送失败";

    public static string Classify(Exception exception) => FromKind(KindOf(exception));

    public static HouseholdDeliveryFailureKind KindOf(Exception exception)
    {
        if (exception is HouseholdNotificationDeliveryException delivery)
            return delivery.Kind;
        return KindFrom(exception);
    }

    public static string Classify(string? summary)
    {
        if (string.IsNullOrEmpty(summary))
            return SendFailed;
        if (summary.Contains("TaskCanceledException", StringComparison.Ordinal)
            || summary.Contains("TimeoutException", StringComparison.Ordinal))
            return TimedOut;
        if (summary.Contains("HttpRequestException", StringComparison.Ordinal)
            || summary.Contains("SocketException", StringComparison.Ordinal))
            return Unreachable;
        if (summary.Contains(TimedOut, StringComparison.Ordinal))
            return TimedOut;
        if (summary.Contains(Unreachable, StringComparison.Ordinal))
            return Unreachable;
        return SendFailed;
    }

    public static string FromKind(HouseholdDeliveryFailureKind kind) => kind switch
    {
        HouseholdDeliveryFailureKind.TimedOut => TimedOut,
        HouseholdDeliveryFailureKind.Unreachable => Unreachable,
        _ => SendFailed
    };

    private static HouseholdDeliveryFailureKind KindFrom(Exception exception)
    {
        var source = exception.InnerException ?? exception;
        if (Match(source) is { } innerKind)
            return innerKind;
        if (!ReferenceEquals(source, exception) && Match(exception) is { } outerKind)
            return outerKind;
        return HouseholdDeliveryFailureKind.SendFailed;
    }

    private static HouseholdDeliveryFailureKind? Match(Exception exception) => exception switch
    {
        TaskCanceledException or TimeoutException => HouseholdDeliveryFailureKind.TimedOut,
        SocketException or HttpRequestException => HouseholdDeliveryFailureKind.Unreachable,
        _ => null
    };
}
