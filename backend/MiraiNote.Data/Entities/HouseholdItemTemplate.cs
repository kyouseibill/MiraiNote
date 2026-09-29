using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 全局事项模板，由迁移种子数据写入，不按家庭隔离。
/// </summary>
[Table("HouseholdItemTemplate")]
public class HouseholdItemTemplate : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public HouseholdCategory Category { get; set; }

    public HouseholdItemType ItemType { get; set; }

    public int? CycleValue { get; set; }

    public HouseholdCycleUnit? CycleUnit { get; set; }

    public int SortOrder { get; set; }
}
