using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 家务 / 车辆 / 证件 / 保修事项。
/// 下次到期日持久化，便于近期到期查询；接口只读。
/// 提前提醒天数是单事项覆盖。提醒日志在独立表，按事项、成员、日期、通道唯一。
/// </summary>
[Table("HouseholdItem")]
public class HouseholdItem : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int HouseholdId { get; set; }

    [ForeignKey(nameof(HouseholdId))]
    public Household? Household { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public HouseholdCategory Category { get; set; } = HouseholdCategory.HomeMaintenance;

    [MaxLength(200)]
    public string? Location { get; set; }

    [MaxLength(200)]
    public string? ModelSpec { get; set; }

    public HouseholdItemType ItemType { get; set; }

    /// <summary>周期数值。周期型必填。</summary>
    public int? CycleValue { get; set; }

    /// <summary>周期单位。周期型必填。</summary>
    public HouseholdCycleUnit? CycleUnit { get; set; }

    /// <summary>上次完成日期。周期型必填，下次到期从这次日期起算。</summary>
    public DateOnly? LastDoneDate { get; set; }

    /// <summary>下次到期日。周期型 = 上次完成 + 周期；一次性到期 = 到期日。</summary>
    public DateOnly? NextDueDate { get; set; }

    /// <summary>一次性到期事项的到期日。</summary>
    public DateOnly? ExpiryDate { get; set; }

    /// <summary>提前提醒天数，默认 7。通知模块在 PR3 才消费。</summary>
    public int LeadDays { get; set; } = 7;

    public int? AssigneeMemberId { get; set; }

    [ForeignKey(nameof(AssigneeMemberId))]
    public HouseholdMember? Assignee { get; set; }

    public int? ConsumableId { get; set; }

    [ForeignKey(nameof(ConsumableId))]
    public HouseholdConsumable? Consumable { get; set; }

    [MaxLength(2000)]
    public string? Note { get; set; }

    [MaxLength(500)]
    public string? PurchaseLink { get; set; }

    /// <summary>暂停期间不进入近期到期，也不算逾期。标记完成不会解除暂停。</summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// 一次性事项完成且未续期后归档。归档后不进首页任何分组、不再提醒、不算逾期。
    /// 与软删除不同：归档可在事项列表筛选查看，管理员填写新的到期日后可恢复。
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>里程周期（公里）。只记录，不参与到期计算。</summary>
    public int? MileageCycleKm { get; set; }

    /// <summary>别名 JSON 数组，供后续 Chat 模糊匹配。例如 ["厨房滤芯","PP棉"]。</summary>
    public string? AliasesJson { get; set; }

    public ICollection<HouseholdCompletionRecord> Completions { get; set; } = new List<HouseholdCompletionRecord>();
}
