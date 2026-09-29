using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Entities;

/// <summary>一条提醒当天的投递结果。Skipped 不占住当天，事项恢复后仍可发送。</summary>
public enum HouseholdReminderDeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    Skipped = 4
}

/// <summary>
/// 事项提醒日志。唯一键是事项、成员、上海日历日、通道。冲突即视为其他执行已经占住，不重复发送。
/// </summary>
[Table("HouseholdReminderLog")]
public class HouseholdReminderLog : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int HouseholdItemId { get; set; }

    [ForeignKey(nameof(HouseholdItemId))]
    public HouseholdItem? Item { get; set; }

    public int MemberId { get; set; }

    [ForeignKey(nameof(MemberId))]
    public HouseholdMember? Member { get; set; }

    /// <summary>Asia/Shanghai 的日历日，不是到期日。</summary>
    public DateOnly ReminderDate { get; set; }

    public HouseholdNotificationChannel Channel { get; set; }

    /// <summary>Lead、Due 或 Overdue。补发时仍记最新状态对应的种类。</summary>
    [MaxLength(16)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>true 表示这不是当天的正点日程，而是离线后补的那一条。</summary>
    public bool IsCatchUp { get; set; }

    public HouseholdReminderDeliveryStatus Status { get; set; } = HouseholdReminderDeliveryStatus.Pending;

    /// <summary>当天已经尝试的次数。达到 3 次后不再重试。</summary>
    public int AttemptCount { get; set; }

    /// <summary>最近一次尝试的 UTC 时间，用来计算退避和判断是否仍在发送中。</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>失败摘要。只记异常类型，不记 Bark 地址、密钥或 SMTP 凭据。</summary>
    [MaxLength(200)]
    public string? LastError { get; set; }
}
