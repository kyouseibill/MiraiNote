using System.Text.Json.Serialization;
using MiraiNote.Shared.Common;

namespace MiraiNote.Shared.Dtos.Household;

/// <summary>事项分类。默认 <see cref="HomeMaintenance"/>（家务维护）。</summary>
[JsonConverter(typeof(CaseInsensitiveEnumConverter<HouseholdCategory>))]
public enum HouseholdCategory
{
    /// <summary>家务维护。</summary>
    HomeMaintenance = 1,

    /// <summary>车辆。</summary>
    Vehicle = 2,

    /// <summary>证件。</summary>
    Document = 3,

    /// <summary>保修。</summary>
    Warranty = 4
}

/// <summary>事项类型：周期型会按完成日顺延；一次性到期只在续期时更换到期日。</summary>
[JsonConverter(typeof(CaseInsensitiveEnumConverter<HouseholdItemType>))]
public enum HouseholdItemType
{
    /// <summary>周期型。</summary>
    Recurring = 1,

    /// <summary>一次性到期。</summary>
    OneOffExpiry = 2
}

/// <summary>周期单位。</summary>
[JsonConverter(typeof(CaseInsensitiveEnumConverter<HouseholdCycleUnit>))]
public enum HouseholdCycleUnit
{
    Day = 1,
    Month = 2,
    Year = 3
}

/// <summary>家庭角色。</summary>
[JsonConverter(typeof(CaseInsensitiveEnumConverter<HouseholdRole>))]
public enum HouseholdRole
{
    /// <summary>管理员：管理成员，并可编辑、暂停、删除事项和删除耗材。</summary>
    Admin = 1,

    /// <summary>成员：可查看、新建事项、标记完成。不能编辑、暂停或删除事项，也不能删除耗材。</summary>
    Member = 2
}

/// <summary>当前用户所在家庭。</summary>
public class HouseholdDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MyMemberId { get; set; }
    public HouseholdRole MyRole { get; set; }
}

public class HouseholdMemberDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    /// <summary>仅家庭管理员可见。普通成员为 null。</summary>
    public string? Email { get; set; }
    public HouseholdRole Role { get; set; }
}

public class AddHouseholdMemberRequest
{
    /// <summary>已有账号的用户名或邮箱。</summary>
    public string UserIdentifier { get; set; } = string.Empty;

    /// <summary>缺省为成员。</summary>
    public HouseholdRole? Role { get; set; }
}

public class ChangeHouseholdMemberRoleRequest
{
    public HouseholdRole Role { get; set; }
}

public class HouseholdItemListQuery
{
    public HouseholdCategory? Category { get; set; }

    /// <summary>是否包含已暂停事项。默认包含。</summary>
    public bool IncludePaused { get; set; } = true;
}

public class HouseholdUpcomingQuery
{
    public HouseholdCategory? Category { get; set; }
}

/// <summary>
/// 近期到期。分组按 Asia/Shanghai 的今天计算：
/// overdue 为到期日早于今天；within7Days 为今天到今天+7（含两端）；
/// within30Days 为今天+8 到今天+30（含两端）。暂停事项不出现。
/// </summary>
public class HouseholdUpcomingDto
{
    public DateOnly Today { get; set; }
    public List<HouseholdUpcomingItemDto> Overdue { get; set; } = [];
    public List<HouseholdUpcomingItemDto> Within7Days { get; set; } = [];
    public List<HouseholdUpcomingItemDto> Within30Days { get; set; } = [];
}

public class HouseholdUpcomingItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public HouseholdCategory Category { get; set; }
    public HouseholdItemType ItemType { get; set; }
    public string? Location { get; set; }
    public DateOnly DueDate { get; set; }
    public int DaysOverdue { get; set; }
    public int DaysRemaining { get; set; }
    public int? AssigneeMemberId { get; set; }
    public string? AssigneeName { get; set; }
}

public class HouseholdItemDto
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string Name { get; set; } = string.Empty;
    public HouseholdCategory Category { get; set; }
    public string? Location { get; set; }
    public string? ModelSpec { get; set; }
    public HouseholdItemType ItemType { get; set; }
    public int? CycleValue { get; set; }
    public HouseholdCycleUnit? CycleUnit { get; set; }
    public DateOnly? LastDoneDate { get; set; }

    /// <summary>下次到期日，由服务端计算，请求体中的同名字段会被忽略。</summary>
    public DateOnly? NextDueDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }
    public int LeadDays { get; set; }
    public int? AssigneeMemberId { get; set; }
    public string? AssigneeName { get; set; }
    public int? ConsumableId { get; set; }
    public string? Note { get; set; }
    public string? PurchaseLink { get; set; }
    public bool IsPaused { get; set; }
    public int? MileageCycleKm { get; set; }
    public List<string> Aliases { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateHouseholdItemRequest
{
    public string Name { get; set; } = string.Empty;
    public HouseholdCategory? Category { get; set; }
    public string? Location { get; set; }
    public string? ModelSpec { get; set; }
    public HouseholdItemType ItemType { get; set; }
    public int? CycleValue { get; set; }
    public HouseholdCycleUnit? CycleUnit { get; set; }
    public DateOnly? LastDoneDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public int? LeadDays { get; set; }
    public int? AssigneeMemberId { get; set; }
    public int? ConsumableId { get; set; }
    public string? Note { get; set; }
    public string? PurchaseLink { get; set; }
    public bool IsPaused { get; set; }
    public int? MileageCycleKm { get; set; }
    public List<string>? Aliases { get; set; }
}

/// <summary>整单更新。不修改暂停状态，暂停请走 pause / resume。下次到期日只读。</summary>
public class UpdateHouseholdItemRequest
{
    public string Name { get; set; } = string.Empty;
    public HouseholdCategory? Category { get; set; }
    public string? Location { get; set; }
    public string? ModelSpec { get; set; }
    public HouseholdItemType ItemType { get; set; }
    public int? CycleValue { get; set; }
    public HouseholdCycleUnit? CycleUnit { get; set; }
    public DateOnly? LastDoneDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public int? LeadDays { get; set; }
    public int? AssigneeMemberId { get; set; }
    public int? ConsumableId { get; set; }
    public string? Note { get; set; }
    public string? PurchaseLink { get; set; }
    public int? MileageCycleKm { get; set; }
    public List<string>? Aliases { get; set; }
}

public class CreateHouseholdItemFromTemplateRequest
{
    public int TemplateId { get; set; }
    public string? Name { get; set; }
    public HouseholdCategory? Category { get; set; }
    public int? CycleValue { get; set; }
    public HouseholdCycleUnit? CycleUnit { get; set; }
    public DateOnly? LastDoneDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Location { get; set; }
    public string? ModelSpec { get; set; }
    public int? LeadDays { get; set; }
    public int? AssigneeMemberId { get; set; }
    public int? ConsumableId { get; set; }
    public string? Note { get; set; }
    public string? PurchaseLink { get; set; }
    public int? MileageCycleKm { get; set; }
    public List<string>? Aliases { get; set; }
}

public class HouseholdItemTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public HouseholdCategory Category { get; set; }
    public HouseholdItemType ItemType { get; set; }
    public int? CycleValue { get; set; }
    public HouseholdCycleUnit? CycleUnit { get; set; }
    public int SortOrder { get; set; }
}

public class CompleteHouseholdItemRequest
{
    /// <summary>缺省为 Asia/Shanghai 的今天。可以是过去的日期，不能晚于今天。</summary>
    public DateOnly? CompletedOn { get; set; }

    /// <summary>执行人的家庭成员 Id。缺省为当前成员。</summary>
    public int? CompletedByMemberId { get; set; }

    /// <summary>先调用 POST /api/v1/upload/image，再把返回的相对路径放在这里。</summary>
    public List<string>? PhotoRefs { get; set; }

    public decimal? Cost { get; set; }
    public string? PurchaseLink { get; set; }
    public string? Note { get; set; }

    /// <summary>为 true 时不扣减耗材，即使同时传了数量。</summary>
    public bool SkipConsumableDeduction { get; set; }

    /// <summary>扣减数量。关联了耗材且未跳过时，缺省为 1；0 表示不扣。</summary>
    public int? ConsumableQuantity { get; set; }

    /// <summary>仅一次性到期事项可填。必须晚于今天（Asia/Shanghai）且晚于完成日期。补记更早日期时只写入历史，不改到期日。</summary>
    public DateOnly? NewExpiryDate { get; set; }
}

public class HouseholdCompletionDto
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public DateOnly CompletedOn { get; set; }
    public int CompletedByMemberId { get; set; }
    public int CompletedByUserId { get; set; }
    public string CompletedByUsername { get; set; } = string.Empty;
    public List<string> PhotoRefs { get; set; } = [];
    public decimal? Cost { get; set; }
    public string? PurchaseLink { get; set; }
    public string? Note { get; set; }
    public int? ConsumableId { get; set; }
    public int ConsumableQuantityDeducted { get; set; }
    public bool NeedsRestock { get; set; }
    public DateOnly? NewExpiryDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CompleteHouseholdItemResult
{
    public HouseholdItemDto Item { get; set; } = new();
    public HouseholdCompletionDto Record { get; set; } = new();
    public int ConsumableQuantityDeducted { get; set; }
    public int? ConsumableStockAfter { get; set; }
    public bool NeedsRestock { get; set; }
}

public class HouseholdConsumableDto
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SpecModel { get; set; }
    public int CurrentStock { get; set; }
    public int RestockThreshold { get; set; }
    public bool IsLowStock { get; set; }

    /// <summary>PR5 补货提醒去重用。补货后重置为 false。客户端不能直接修改。</summary>
    public bool LowStockReminderSent { get; set; }

    public string? Unit { get; set; }
    public string? PurchaseLink { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SaveHouseholdConsumableRequest
{
    public string Name { get; set; } = string.Empty;
    public string? SpecModel { get; set; }
    public int CurrentStock { get; set; }
    public int? RestockThreshold { get; set; }
    public string? Unit { get; set; }
    public string? PurchaseLink { get; set; }
    public string? Note { get; set; }
}

public class RestockHouseholdConsumableRequest
{
    public int Quantity { get; set; }
}

/// <summary>测试时钟状态。仅在非 Production 且 Household:TestClock:Enabled=true 时可用。</summary>
public class HouseholdTestClockDto
{
    public bool Enabled { get; set; }

    /// <summary>System、Absolute 或 Offset。</summary>
    public string Mode { get; set; } = "System";

    public DateTimeOffset UtcNow { get; set; }
    public DateOnly ShanghaiToday { get; set; }
    public DateTimeOffset? AbsoluteUtc { get; set; }
    public long? OffsetSeconds { get; set; }
}

/// <summary>设置测试时钟。utcNow 与 offsetSeconds 必须二选一。</summary>
public class SetHouseholdTestClockRequest
{
    /// <summary>绝对 UTC 时间。</summary>
    public DateTimeOffset? UtcNow { get; set; }

    /// <summary>相对系统时钟的偏移秒数。正数把“现在”拨到未来。</summary>
    public long? OffsetSeconds { get; set; }
}
