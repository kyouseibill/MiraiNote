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
}
