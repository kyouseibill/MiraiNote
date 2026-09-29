using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 一次低库存周期里，某成员某通道已经提醒过。补货时软删除，下次低库存可以再提醒。
/// </summary>
[Table("HouseholdConsumableReminder")]
public class HouseholdConsumableReminder : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int ConsumableId { get; set; }

    [ForeignKey(nameof(ConsumableId))]
    public HouseholdConsumable? Consumable { get; set; }

    public int MemberId { get; set; }

    [ForeignKey(nameof(MemberId))]
    public HouseholdMember? Member { get; set; }

    public HouseholdNotificationChannel Channel { get; set; }

    /// <summary>投递结果。已有行在迁移里默认记为 Sent，避免把历史补货提醒显示成失败。</summary>
    public HouseholdReminderDeliveryStatus Status { get; set; } = HouseholdReminderDeliveryStatus.Pending;

    /// <summary>最近一次尝试的 UTC 时间。</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>失败摘要。只记异常类型，不记 Bark 地址、密钥或 SMTP 凭据。</summary>
    [MaxLength(200)]
    public string? LastError { get; set; }
}
