using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 家庭成员。PR3 的按成员通知设置应挂在独立表上（成员 Id 唯一），不要把通道密钥写进本表。
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
