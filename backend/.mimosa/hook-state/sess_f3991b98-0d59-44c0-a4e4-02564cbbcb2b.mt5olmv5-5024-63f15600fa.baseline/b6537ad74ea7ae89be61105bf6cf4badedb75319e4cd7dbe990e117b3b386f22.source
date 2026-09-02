// ============================================================
// Mirai M1 契约 C# DTO（与 docs/contracts/MiraiDtos.cs 同步，
// 仅按解决方案既有命名/序列化约定适配：JSON camelCase 由
// ASP.NET Core 全局序列化策略保证，不加注解）。
// 同步规则：与 api-contract.md、types.ts 三处同改，仅由主 Agent 执行变更。
// ============================================================

namespace MiraiNote.Shared.Dtos.Mirai;

// ---------- 枚举 ----------

/// <summary>捕获来源：1=HotkeyCapture（全局热键）2=TodayBar（今日流捕获条）3=Manual（收件箱手输）4=Retriage（纠错重分拣）。</summary>
public enum InboxSource
{
    HotkeyCapture = 1,
    TodayBar = 2,
    Manual = 3,
    Retriage = 4
}

/// <summary>捕获项状态：0=Pending 1=Triaging 2=Triaged 3=Dispatched 4=Discarded 5=Error。</summary>
public enum InboxStatus
{
    Pending = 0,
    Triaging = 1,
    Triaged = 2,
    Dispatched = 3,
    Discarded = 4,
    Error = 5
}

/// <summary>AI 建议 Decision（审计日志）：applied / ignored / discarded / undone。</summary>
public enum InboxDecision
{
    Applied,
    Ignored,
    Discarded,
    Undone
}

// ---------- 分拣（AiParse JSON 反序列化目标） ----------

/// <summary>分拣结果：建议条目列表 + 不确定说明列表。</summary>
public sealed record TriageResultDto(List<TriageSuggestionDto> Items, List<string> Uncertain);

/// <summary>单条分拣建议。</summary>
public sealed record TriageSuggestionDto(
    string SuggestionId,
    string Type,          // task | worklog | lifelog | knowledge | ignore
    double Confidence,
    string Rationale,
    FieldsDto? Fields);

/// <summary>建议字段（task/worklog/lifelog 三类字段共存，按 type 只填对应组）。</summary>
public sealed record FieldsDto(
    // task
    string? Content,
    string? RemindAtLocal,
    int? Priority,
    string? Section,      // work | life
    // worklog
    string? Title,
    List<string>? Tags,
    string? Category,
    // lifelog
    string? Mood);

// ---------- Inbox ----------

/// <summary>创建捕获项请求。</summary>
public sealed record CreateInboxItemRequest(string Raw, int Source, string LocalTime, int TzOffsetMinutes);

/// <summary>重新分拣请求（correction 可选）。</summary>
public sealed record RetriageRequest(string? Correction);

/// <summary>单条分发请求：建议 Id + 深合并覆盖字段。</summary>
public sealed record DispatchItemRequest(string SuggestionId, FieldsDto? Overrides);

/// <summary>分发请求。</summary>
public sealed record DispatchRequest(List<DispatchItemRequest> Items);

/// <summary>分发创建结果引用。</summary>
public sealed record CreatedRefDto(string SuggestionId, string Type, int Id, string Title);

/// <summary>分发结果。</summary>
public sealed record DispatchResultDto(int InboxItemId, List<CreatedRefDto> Created);

/// <summary>捕获项响应 DTO（AiParse 只含建议，不含内部换算上下文）。</summary>
public sealed record InboxItemDto(
    int Id,
    string Raw,
    int Source,
    int Status,
    TriageResultDto? AiParse,
    string? AiModel,
    string? CorrectionNote,
    string? Error,
    DateTime? TriagedAt,      // UTC
    DateTime CreatedAt);      // UTC

// ---------- 晨报与今日流 ----------

/// <summary>晨报引用来源。</summary>
public sealed record SourceRefDto(string Type, int Id, string Title);

/// <summary>晨报 DTO。</summary>
public sealed record BriefingDto(
    int Id,
    DateOnly Date,
    string Content,
    List<SourceRefDto> Sources,
    string Model,
    DateTime GeneratedAt);

/// <summary>到期/逾期任务 DTO。</summary>
public sealed record DueTaskDto(
    int Id,
    string Content,
    DateTime? RemindAt,       // UTC
    int Priority,
    string Section,
    bool IsDone,
    bool IsPinned);

/// <summary>今日流时间线条目。</summary>
public sealed record FeedItemDto(
    DateTime Time,            // UTC
    string Kind,              // capture | worklog | lifelog | memo | task | briefing
    string Title,
    int? RefId,
    string? AiSummary);       // M1 恒 null，M2 写后提炼预留

/// <summary>今日流聚合 DTO。</summary>
public sealed record DayOverviewDto(
    string Date,              // yyyy-MM-dd（客户端本地日期）
    BriefingDto? Briefing,
    string? BriefingError,
    List<DueTaskDto> DueTasks,
    List<DueTaskDto> OverdueTasks,
    List<FeedItemDto> TodayFeed,
    int InboxPendingCount,
    int WeekEntryCount);

/// <summary>晨报重生成请求（date 必传，yyyy-MM-dd）。</summary>
public sealed record RegenerateBriefingRequest(string Date);

// ---------- AI 统计 ----------

/// <summary>AI 调用统计。</summary>
public sealed record AiActionStatsDto(
    int Total,
    List<ActionTypeCountDto> ByActionType,
    List<DateCountDto> Last7Days);

/// <summary>按动作类型计数。</summary>
public sealed record ActionTypeCountDto(string ActionType, int Count);

/// <summary>按日期计数。</summary>
public sealed record DateCountDto(string Date, int Count);
