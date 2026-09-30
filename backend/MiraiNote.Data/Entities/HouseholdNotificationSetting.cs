using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 成员自己的通知设置。Bark 地址只存密文和末 4 位，不明文落库。
/// </summary>
[Table("HouseholdNotificationSetting")]
public class HouseholdNotificationSetting : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int MemberId { get; set; }

    [ForeignKey(nameof(MemberId))]
    public HouseholdMember? Member { get; set; }

    public bool BarkEnabled { get; set; } = true;

    /// <summary>这个时刻及之前的 Bark 失败不再提示。重新保存、清空地址或关闭通道时写入。</summary>
    public DateTime? BarkFailureAcknowledgedAt { get; set; }

    /// <summary>加密后的 Bark 地址。不要记录、不要返回给前端。</summary>
    [MaxLength(4000)]
    public string? BarkAddressProtected { get; set; }

    /// <summary>供本人界面显示的末 4 位。不是完整地址。</summary>
    [MaxLength(8)]
    public string? BarkAddressSuffix { get; set; }

    public bool EmailEnabled { get; set; } = true;

    /// <summary>这个时刻及之前的邮件失败不再提示。关闭邮件通道时写入。</summary>
    public DateTime? EmailFailureAcknowledgedAt { get; set; }

    [MaxLength(200)]
    public string? NotificationEmail { get; set; }

    /// <summary>每天推送的小时，Asia/Shanghai，0-23。默认 9。</summary>
    public int PushHour { get; set; } = 9;

    /// <summary>每天推送的分钟，0-59。默认 0。</summary>
    public int PushMinute { get; set; }

    /// <summary>提前 N 天走的通道。默认邮件。</summary>
    public HouseholdNotificationChannel LeadChannel { get; set; } = HouseholdNotificationChannel.Email;

    /// <summary>到期当天和逾期走的通道。默认 Bark。</summary>
    public HouseholdNotificationChannel DueChannel { get; set; } = HouseholdNotificationChannel.Bark;

    /// <summary>逾期后每隔多少天再提醒一次。默认 3。</summary>
    public int OverdueIntervalDays { get; set; } = 3;
}
