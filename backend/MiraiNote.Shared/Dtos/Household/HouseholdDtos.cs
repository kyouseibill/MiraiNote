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

/// <summary>通知通道。提前 N 天、到期当天和逾期各选一个。</summary>
[JsonConverter(typeof(CaseInsensitiveEnumConverter<HouseholdNotificationChannel>))]
public enum HouseholdNotificationChannel
{
    /// <summary>邮件。</summary>
    Email = 1,

    /// <summary>Bark。</summary>
    Bark = 2
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

/// <summary>家庭邀请状态。待处理同一人同一家庭只保留一条。</summary>
[JsonConverter(typeof(CaseInsensitiveEnumConverter<HouseholdInvitationStatus>))]
public enum HouseholdInvitationStatus
{
    Pending = 1,
    Accepted = 2,
    Rejected = 3,
    Revoked = 4
}

/// <summary>当前用户所在家庭。没有家庭时 <see cref="HasHousehold"/> 为 false，Id 为 0。</summary>
public class HouseholdDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MyMemberId { get; set; }
    public HouseholdRole MyRole { get; set; }

    /// <summary>false 表示还没有家庭。有待处理邀请时不会自动创建。</summary>
    public bool HasHousehold { get; set; }

    /// <summary>有未过期、未撤回的待处理邀请。和 <see cref="HasHousehold"/> 为 false 一起出现时，先接受或拒绝。</summary>
    public bool HasPendingInvitations { get; set; }
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

public class HouseholdInvitationDto
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string HouseholdName { get; set; } = string.Empty;
    public int InviteeUserId { get; set; }
    public string InviteeUsername { get; set; } = string.Empty;

    /// <summary>只在本家庭管理员查看待发出的邀请时返回。</summary>
    public string? InviteeEmail { get; set; }

    /// <summary>
    /// 邀请人展示名。账号没有单独的昵称字段，因此就是用户名。收件箱不返回邀请人邮箱。
    /// </summary>
    public string InviterUsername { get; set; } = string.Empty;
    public HouseholdRole Role { get; set; }
    public HouseholdInvitationStatus Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsExpired { get; set; }
}

public class HouseholdConsumableLinkDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
}

public class HouseholdItemListQuery
{
    public HouseholdCategory? Category { get; set; }

    /// <summary>是否包含已暂停事项。默认包含。已归档事项不受这一项控制，见 <see cref="ArchivedOnly"/>。</summary>
    public bool IncludePaused { get; set; } = true;

    /// <summary>为 true 时只返回已归档事项。默认不包含已归档。</summary>
    public bool ArchivedOnly { get; set; }
}

public class HouseholdUpcomingQuery
{
    public HouseholdCategory? Category { get; set; }
}

/// <summary>
/// 近期到期。分组按 Asia/Shanghai 的今天计算：
/// overdue 为到期日早于今天；within7Days 为今天到今天+7（含两端）；
/// within30Days 为今天+8 到今天+30（含两端）。暂停事项和已归档事项不出现。
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

    /// <summary>一次性事项完成且未续期。已归档事项不进首页分组，不算逾期。</summary>
    public bool IsArchived { get; set; }

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

    /// <summary>仅管理员可传 true。成员传 true 时返回 403。</summary>
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

    /// <summary>仅一次性到期事项可填。必须晚于今天（Asia/Shanghai）且晚于完成日期。不填则归档，不自动顺延。补记更早日期时只写入历史，不改到期日、也不归档。</summary>
    public DateOnly? NewExpiryDate { get; set; }
}

/// <summary>恢复已归档的一次性事项。新的到期日必须晚于今天（Asia/Shanghai）。不传则 400。</summary>
public class RestoreHouseholdItemRequest
{
    public DateOnly? ExpiryDate { get; set; }
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

    /// <summary>本轮低库存已经提醒过。补货后重置。客户端不能直接修改。</summary>
    public bool LowStockReminderSent { get; set; }

    public string? Unit { get; set; }
    public string? PurchaseLink { get; set; }
    public string? Note { get; set; }
    public List<HouseholdConsumableLinkDto> LinkedItems { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SaveHouseholdConsumableRequest
{
    public string Name { get; set; } = string.Empty;
    public string? SpecModel { get; set; }

    /// <summary>只在创建时写入。更新接口忽略此字段，库存只通过补货和完成扣减变更。</summary>
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

/// <summary>家务模块的「今天」。测试时钟关闭时接口返回 404，生产环境不改前端本地日期。</summary>
public class HouseholdServerTodayDto
{
    public DateOnly Today { get; set; }
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

/// <summary>
/// 当前成员自己的通知设置。没有 Bark 地址字段，响应里只有「已配置」和末 4 位。
/// </summary>
public class HouseholdNotificationSettingsDto
{
    public bool BarkEnabled { get; set; }
    public bool BarkConfigured { get; set; }
    public string? BarkAddressSuffix { get; set; }
    public bool EmailEnabled { get; set; }

    /// <summary>账号邮箱。第一版不能改成其他地址。</summary>
    public string? Email { get; set; }
    public int PushHour { get; set; } = 9;
    public int PushMinute { get; set; }
    public HouseholdNotificationChannel LeadChannel { get; set; } = HouseholdNotificationChannel.Email;
    public HouseholdNotificationChannel DueChannel { get; set; } = HouseholdNotificationChannel.Bark;
    public int OverdueIntervalDays { get; set; } = 3;

    /// <summary>服务器总开关。关闭时到点不推送，设置和发送测试仍可用。</summary>
    public bool NotificationsEnabled { get; set; }

    /// <summary>密文无法用当前密钥解开。为 true 时不要再显示「已配置」。</summary>
    public bool BarkAddressUnreadable { get; set; }

    /// <summary>该通道最近一次投递是失败，且之后没有成功。成功之后为 null。</summary>
    public HouseholdNotificationDeliveryFailureDto? BarkFailure { get; set; }

    /// <summary>该通道最近一次投递是失败，且之后没有成功。成功之后为 null。</summary>
    public HouseholdNotificationDeliveryFailureDto? EmailFailure { get; set; }
}

/// <summary>设置页上的投递失败提示。原因是固定分类，不含地址、密钥或异常细节。</summary>
public class HouseholdNotificationDeliveryFailureDto
{
    /// <summary>失败时间，Asia/Shanghai。</summary>
    public DateTimeOffset FailedAt { get; set; }

    /// <summary>超时、连接失败或发送失败。</summary>
    public string Reason { get; set; } = string.Empty;
}

public class UpdateHouseholdNotificationSettingsRequest
{
    public bool BarkEnabled { get; set; } = true;

    /// <summary>非空时替换 Bark 地址。空值表示不修改。</summary>
    public string? BarkAddress { get; set; }

    public bool ClearBarkAddress { get; set; }
    public bool EmailEnabled { get; set; } = true;

    /// <summary>只能是账号邮箱。空值表示不修改收件人；其它地址返回 400。未传的推送时间、通道和间隔会恢复默认。</summary>
    public string? Email { get; set; }

    public int PushHour { get; set; } = 9;
    public int PushMinute { get; set; }
    public HouseholdNotificationChannel LeadChannel { get; set; } = HouseholdNotificationChannel.Email;
    public HouseholdNotificationChannel DueChannel { get; set; } = HouseholdNotificationChannel.Bark;
    public int OverdueIntervalDays { get; set; } = 3;
}

public class TestHouseholdBarkRequest
{
    /// <summary>不填则用已保存的地址。响应不会回显这个值。</summary>
    public string? BarkAddress { get; set; }
}

public class TestHouseholdEmailRequest
{
    /// <summary>不填则发给账号邮箱。与账号邮箱不一致时返回 400。</summary>
    public string? Email { get; set; }
}

public class HouseholdChatCandidateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public bool IsPaused { get; set; }
    public DateOnly? NextDueDate { get; set; }
    public int? ConsumableId { get; set; }
    public string? ConsumableName { get; set; }
    public int? ConsumableStock { get; set; }
}

public class HouseholdChatHistoryLineDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public DateOnly CompletedOn { get; set; }
    public decimal? Cost { get; set; }
}

public class HouseholdChatUpcomingLineDto
{
    public int ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
}

/// <summary>Chat 工具只返回这份草稿或查询结果，不写入完成记录。</summary>
public class HouseholdChatInterpretationDto
{
    public string Kind { get; set; } = "unrecognized";
    public string Message { get; set; } = string.Empty;
    public int? DraftId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public decimal? Cost { get; set; }
    public bool DeductConsumable { get; set; }
    public HouseholdChatCandidateDto? Item { get; set; }
    public List<HouseholdChatCandidateDto> Candidates { get; set; } = [];
    public string? SuggestedName { get; set; }
    public List<HouseholdChatHistoryLineDto> History { get; set; } = [];
    public List<HouseholdChatUpcomingLineDto> Upcoming { get; set; } = [];
}

public class ConfirmHouseholdChatRequest
{
    public int DraftId { get; set; }
    public int ItemId { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public decimal? Cost { get; set; }

    /// <summary>缺省沿用草稿，默认扣减。false 表示这次不扣。</summary>
    public bool? DeductConsumable { get; set; }
}
