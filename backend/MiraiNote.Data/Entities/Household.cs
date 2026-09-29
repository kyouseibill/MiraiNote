using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 家庭。家务周期的数据按家庭隔离。
/// 当前约定一名用户同时只属于一个未删除的家庭，之后若要支持多个家庭，只需放宽成员唯一索引并增加切换入口。
/// </summary>
[Table("Household")]
public class Household : BaseEntity
{
    public const string DefaultName = "我的家庭";

    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = DefaultName;

    public ICollection<HouseholdMember> Members { get; set; } = new List<HouseholdMember>();

    public ICollection<HouseholdItem> Items { get; set; } = new List<HouseholdItem>();

    public ICollection<HouseholdConsumable> Consumables { get; set; } = new List<HouseholdConsumable>();
}
