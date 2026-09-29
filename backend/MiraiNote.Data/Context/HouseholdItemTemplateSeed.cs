using MiraiNote.Data.Entities;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Data.Context;

/// <summary>
/// PRD F3 模板。Id 固定，方便迁移种子数据和后续引用。
/// </summary>
public static class HouseholdItemTemplateSeed
{
    private static readonly DateTime SeedAt = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Unspecified);

    public static IReadOnlyList<HouseholdItemTemplate> All { get; } =
    [
        Recurring(1, "空调滤网", HouseholdCategory.HomeMaintenance, 3),
        Recurring(2, "净水器 PP 棉", HouseholdCategory.HomeMaintenance, 6),
        Recurring(3, "净水器活性炭", HouseholdCategory.HomeMaintenance, 12),
        Recurring(4, "净水器 RO 膜", HouseholdCategory.HomeMaintenance, 24),
        Recurring(5, "油烟机清洗", HouseholdCategory.HomeMaintenance, 6),
        Recurring(6, "热水器除垢", HouseholdCategory.HomeMaintenance, 12),
        Recurring(7, "烟雾报警器电池", HouseholdCategory.HomeMaintenance, 12),
        Recurring(8, "冰箱除味剂", HouseholdCategory.HomeMaintenance, 3),
        Recurring(9, "洗衣机槽清洁", HouseholdCategory.HomeMaintenance, 1),
        Recurring(10, "常规保养", HouseholdCategory.Vehicle, 6),
        Recurring(11, "年检", HouseholdCategory.Vehicle, 12),
        Recurring(12, "交强险/商业险", HouseholdCategory.Vehicle, 12),
        OneOff(13, "身份证", HouseholdCategory.Document),
        OneOff(14, "护照", HouseholdCategory.Document),
        OneOff(15, "驾照", HouseholdCategory.Document),
        OneOff(16, "签证", HouseholdCategory.Document),
        OneOff(17, "家电保修", HouseholdCategory.Warranty)
    ];

    private static HouseholdItemTemplate Recurring(int id, string name, HouseholdCategory category, int months) =>
        Create(id, name, category, HouseholdItemType.Recurring, months, HouseholdCycleUnit.Month);

    private static HouseholdItemTemplate OneOff(int id, string name, HouseholdCategory category) =>
        Create(id, name, category, HouseholdItemType.OneOffExpiry, null, null);

    private static HouseholdItemTemplate Create(
        int id,
        string name,
        HouseholdCategory category,
        HouseholdItemType itemType,
        int? cycleValue,
        HouseholdCycleUnit? cycleUnit) =>
        new()
        {
            Id = id,
            Name = name,
            Category = category,
            ItemType = itemType,
            CycleValue = cycleValue,
            CycleUnit = cycleUnit,
            SortOrder = id,
            IsDeleted = false,
            CreatedAt = SeedAt,
            CreatedBy = 1,
            UpdatedAt = SeedAt,
            UpdatedBy = 1
        };
}
