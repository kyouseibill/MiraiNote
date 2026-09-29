using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 事项提醒日志。唯一键是事项、成员、上海日历日、通道。冲突即视为当天该通道已处理。
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
}
