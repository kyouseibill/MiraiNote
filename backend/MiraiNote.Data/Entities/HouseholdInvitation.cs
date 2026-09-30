using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 加入家庭的待确认邀请。v1 只在站内展示，不发邮件。
/// 同一个人在同一家庭同时只有一条待处理邀请。
/// </summary>
[Table("HouseholdInvitation")]
public class HouseholdInvitation : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int HouseholdId { get; set; }

    [ForeignKey(nameof(HouseholdId))]
    public Household? Household { get; set; }

    public int InviterUserId { get; set; }

    public int InviteeUserId { get; set; }

    public HouseholdRole Role { get; set; } = HouseholdRole.Member;

    public HouseholdInvitationStatus Status { get; set; } = HouseholdInvitationStatus.Pending;

    /// <summary>过期时刻，UTC。按家务时钟计算，默认 7 天。</summary>
    public DateTime ExpiresAt { get; set; }
}
