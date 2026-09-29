using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 耗材。一个耗材可以关联多个事项。
/// <see cref="LowStockReminderSent"/> 在本轮低库存提醒发出后置 true，补货时重置。
/// </summary>
[Table("HouseholdConsumable")]
public class HouseholdConsumable : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int HouseholdId { get; set; }

    [ForeignKey(nameof(HouseholdId))]
    public Household? Household { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? SpecModel { get; set; }

    public int CurrentStock { get; set; }

    public int RestockThreshold { get; set; } = 1;

    /// <summary>PR5 使用。本 PR 只在补货（库存增加）时重置为 false。</summary>
    public bool LowStockReminderSent { get; set; }

    [MaxLength(20)]
    public string? Unit { get; set; }

    [MaxLength(500)]
    public string? PurchaseLink { get; set; }

    [MaxLength(2000)]
    public string? Note { get; set; }

    public ICollection<HouseholdItem> Items { get; set; } = new List<HouseholdItem>();
}
