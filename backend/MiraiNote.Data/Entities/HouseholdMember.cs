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
}
