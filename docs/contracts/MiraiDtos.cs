// ============================================================
// Mirai M1 契约 C# DTO（权威版本）
// 同步规则：与 api-contract.md、types.ts 三处同改，仅由主 Agent 执行变更。
// BE 流：本文件拷入 backend/MiraiNote.Shared/Dtos/Mirai/ 并按解决方案既有
// 命名/序列化约定适配（JSON camelCase 由全局序列化策略保证，不加注解）。
// ============================================================
// 注意：响应统一包现有 ApiResponse<T>（信封），本文件只定义 data 部分。

namespace MiraiNote.Shared.Dtos.Mirai;

// ---------- 枚举 ----------
public enum InboxSource { HotkeyCapture = 1, TodayBar = 2, Manual = 3, Retriage = 4 }
public enum InboxStatus { Pending = 0, Triaging = 1, Triaged = 2, Dispatched = 3, Discarded = 4, Error = 5 }
public enum InboxDecision { Applied, Ignored, Discarded, Undone }

// ---------- 分拣（AiParse JSON 反序列化目标） ----------
public sealed record TriageResultDto(List<TriageSuggestionDto> Items, List<string> Uncertain);

public sealed record TriageSuggestionDto(
    string SuggestionId,
    string Type,          // task | worklog | lifelog | knowledge | ignore
    double Confidence,
    string Rationale,
    FieldsDto? Fields);

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
public sealed record CreateInboxItemRequest(string Raw, int Source, string LocalTime, int TzOffsetMinutes);
public sealed record RetriageRequest(string? Correction);
public sealed record DispatchItemRequest(string SuggestionId, FieldsDto? Overrides);
public sealed record DispatchRequest(List<DispatchItemRequest> Items);
public sealed record CreatedRefDto(string SuggestionId, string Type, int Id, string Title);
public sealed record DispatchResultDto(int InboxItemId, List<CreatedRefDto> Created);

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
public sealed record SourceRefDto(string Type, int Id, string Title);

public sealed record BriefingDto(
    int Id,
    DateOnly Date,
    string Content,
    List<SourceRefDto> Sources,
    string Model,
    DateTime GeneratedAt);

public sealed record DueTaskDto(
    int Id,
    string Content,
    DateTime? RemindAt,       // UTC
    int Priority,
    string Section,
    bool IsDone,
    bool IsPinned);

public sealed record FeedItemDto(
    DateTime Time,            // UTC
    string Kind,              // capture | worklog | lifelog | memo | task | briefing
    string Title,
    int? RefId,
    string? AiSummary);       // M1 恒 null，M2 写后提炼预留

public sealed record DayOverviewDto(
    string Date,              // yyyy-MM-dd（客户端本地日期）
    BriefingDto? Briefing,
    string? BriefingError,
    List<DueTaskDto> DueTasks,
    List<DueTaskDto> OverdueTasks,
    List<FeedItemDto> TodayFeed,
    int InboxPendingCount,
    int WeekEntryCount);

public sealed record RegenerateBriefingRequest(string Date); // yyyy-MM-dd，必传

// ---------- AI 统计 ----------
public sealed record AiActionStatsDto(
    int Total,
    List<ActionTypeCountDto> ByActionType,
    List<DateCountDto> Last7Days);

public sealed record ActionTypeCountDto(string ActionType, int Count);
public sealed record DateCountDto(string Date, int Count);

// ---------- Chat 会话扩展 ----------
// CreateSessionRequest 在现有 DTO 基础上追加三个可空字段：
//   string? SessionType    // legacy | command | context
//   string? AttachToType   // worklog | lifelog | memo | inbox | briefing
//   int?    AttachToObjectId
// 校验：SessionType == "context" 时 AttachToType / AttachToObjectId 必填且对象存在。
// 会话响应 DTO 同步追加这三个可空字段回显。
