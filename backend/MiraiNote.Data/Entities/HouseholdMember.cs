using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 家庭成员。通知设置在 <see cref="HouseholdNotificationSetting"/>，Bark 地址不写在本表。
/// </summary>
[Table("HouseholdMember")]
public class HouseholdMember : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int HouseholdId { get; set; }

    [ForeignKey(nameof(HouseholdId))]
    public Household? Household { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public HouseholdRole Role { get; set; } = HouseholdRole.Member;

    /// <summary>
    /// 提醒起算时间（UTC）。这一刻对应的推送日之前，本该发出的日程不补发。
    /// 自动创建的家庭与历史成员与 <see cref="BaseEntity.CreatedAt"/> 相同，因此已有成员仍会补上加入之后漏发的一条。
    /// </summary>
    public DateTime NotifyFromUtc { get; set; }
}
