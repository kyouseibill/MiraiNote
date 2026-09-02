using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiraiNote.Shared.Dtos.Mirai;

namespace MiraiNote.Core.Services.Mirai;

/// <summary>
/// Mirai 时间换算与 JSON 序列化辅助。
/// 服务端存储与返回一律 UTC；客户端本地时间由请求携带的 tzOffsetMinutes（UTC 偏移分钟，东八区=480）换算。
/// </summary>
internal static class MiraiTime
{
    /// <summary>tzOffsetMinutes 合法范围：±14 小时。</summary>
    public const int MaxTzOffsetMinutes = 14 * 60;

    /// <summary>
    /// 客户端本地时间（无时区后缀，如 2026-08-26T09:00）→ UTC。
    /// 换算规则：UTC = 本地时间 - tzOffsetMinutes（东八区 09:00 → UTC 01:00）。
    /// 空白或无法解析时返回 null（不抛错，交由调用方决定 400 或忽略）。
    /// </summary>
    public static DateTime? LocalToUtc(string? localTime, int tzOffsetMinutes)
    {
        if (string.IsNullOrWhiteSpace(localTime)) return null;
        if (!DateTime.TryParse(localTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;

        return DateTime.SpecifyKind(local.AddMinutes(-tzOffsetMinutes), DateTimeKind.Utc);
    }

    /// <summary>
    /// 客户端本地日期（yyyy-MM-dd）→ 该本地日的 [起始, 结束) UTC 边界。
    /// 起始 = 本地 00:00 - 偏移；结束 = 起始 + 24h。
    /// </summary>
    public static (DateTime StartUtc, DateTime EndUtc) LocalDayRangeUtc(DateOnly localDate, int tzOffsetMinutes)
    {
        var localMidnight = localDate.ToDateTime(TimeOnly.MinValue);
        var startUtc = DateTime.SpecifyKind(localMidnight.AddMinutes(-tzOffsetMinutes), DateTimeKind.Utc);
        return (startUtc, startUtc.AddDays(1));
    }

    /// <summary>
    /// 客户端本地日期 → 所在周（周一起始）的本地日范围。
    /// </summary>
    public static (DateOnly WeekStart, DateOnly WeekEnd) LocalWeekRange(DateOnly localDate)
    {
        var daysFromMonday = ((int)localDate.DayOfWeek + 6) % 7;
        var weekStart = localDate.AddDays(-daysFromMonday);
        return (weekStart, weekStart.AddDays(6));
    }

    /// <summary>本地日期 → 中文星期名。</summary>
    public static string WeekdayCn(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        _ => "日"
    };
}

/// <summary>
/// AiParse 列的存储信封：在契约 TriageResult 之上附加捕获时的客户端上下文
/// （tzOffsetMinutes/localTime），供 dispatch 阶段做 remindAtLocal→UTC 换算。
/// 对外 DTO 只暴露 Items/Uncertain，信封字段不出契约边界。
/// </summary>
internal sealed record AiParseEnvelope(
    List<TriageSuggestionDto> Items,
    List<string> Uncertain,
    int TzOffsetMinutes,
    string? LocalTime);

/// <summary>
/// dispatch undo 用的 AIActionLog.PayloadJson 结构。
/// </summary>
internal sealed record DispatchLogPayload(
    string SuggestionId,
    string? CreatedType,
    int? CreatedId,
    TriageSuggestionDto? Suggestion,
    FieldsDto? Overrides);

internal static class MiraiJson
{
    /// <summary>Mirai 内部 JSON 约定：camelCase（与契约一致）、大小写不敏感（容忍模型输出差异）。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
