using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 事项写入前的归一化结果。下次到期日在这里算完，调用方不能从外部传入。
/// </summary>
public sealed record HouseholdItemDraft(
    string Name,
    HouseholdCategory Category,
    string? Location,
    string? ModelSpec,
    HouseholdItemType ItemType,
    int? CycleValue,
    HouseholdCycleUnit? CycleUnit,
    DateOnly? LastDoneDate,
    DateOnly? ExpiryDate,
    int LeadDays,
    int? AssigneeMemberId,
    int? ConsumableId,
    string? Note,
    string? PurchaseLink,
    int? MileageCycleKm,
    IReadOnlyList<string> Aliases,
    DateOnly NextDueDate)
{
    public static HouseholdItemDraft Normalize(
        string? name,
        HouseholdCategory? category,
        string? location,
        string? modelSpec,
        HouseholdItemType itemType,
        int? cycleValue,
        HouseholdCycleUnit? cycleUnit,
        DateOnly? lastDoneDate,
        DateOnly? expiryDate,
        int? leadDays,
        int? assigneeMemberId,
        int? consumableId,
        string? note,
        string? purchaseLink,
        int? mileageCycleKm,
        IEnumerable<string>? aliases,
        DateOnly today)
    {
        if (!Enum.IsDefined(itemType))
            throw new BusinessException("请指定事项类型（周期型或一次性到期）", 400);

        var resolvedCategory = category ?? HouseholdCategory.HomeMaintenance;
        if (!Enum.IsDefined(resolvedCategory))
            throw new BusinessException("分类无效", 400);

        if (cycleUnit is HouseholdCycleUnit unit && !Enum.IsDefined(unit))
            throw new BusinessException("周期单位无效", 400);

        if (lastDoneDate is DateOnly doneOn && doneOn > today)
            throw new BusinessException("上次完成日期不能晚于今天", 400);

        var hasCycle = cycleValue != null || cycleUnit != null;
        DateOnly nextDue;
        if (itemType == HouseholdItemType.Recurring)
        {
            if (expiryDate != null)
                throw new BusinessException("周期型事项不填写到期日，下次到期日会自动计算", 400);
            if (cycleValue is not int value || cycleUnit is not HouseholdCycleUnit resolvedUnit)
                throw new BusinessException("周期型事项必须填写周期数值和单位", 400);
            if (lastDoneDate is not DateOnly done)
                throw new BusinessException("周期型事项必须填写上次完成日期", 400);

            nextDue = HouseholdCycleRules.AddCycle(done, value, resolvedUnit);
        }
        else
        {
            if (hasCycle)
                throw new BusinessException("一次性到期事项不填写周期，只填写到期日", 400);
            if (expiryDate is not DateOnly expiry)
                throw new BusinessException("一次性到期事项必须填写到期日", 400);
            nextDue = expiry;
        }

        var resolvedLeadDays = leadDays ?? HouseholdCycleRules.DefaultLeadDays;
        if (resolvedLeadDays < 0 || resolvedLeadDays > 3650)
            throw new BusinessException("提前提醒天数需在 0 到 3650 之间", 400);

        if (assigneeMemberId is <= 0)
            throw new BusinessException("负责人无效", 400);
        if (consumableId is <= 0)
            throw new BusinessException("耗材无效", 400);

        if (mileageCycleKm is int mileage && (mileage <= 0 || mileage > 10_000_000))
            throw new BusinessException("里程周期需大于 0", 400);

        return new HouseholdItemDraft(
            HouseholdText.Require(name, HouseholdFieldLimits.Name, "名称"),
            resolvedCategory,
            HouseholdText.Clean(location, HouseholdFieldLimits.Location, "位置"),
            HouseholdText.Clean(modelSpec, HouseholdFieldLimits.ModelSpec, "型号/规格"),
            itemType,
            itemType == HouseholdItemType.Recurring ? cycleValue : null,
            itemType == HouseholdItemType.Recurring ? cycleUnit : null,
            lastDoneDate,
            itemType == HouseholdItemType.OneOffExpiry ? expiryDate : null,
            resolvedLeadDays,
            assigneeMemberId,
            consumableId,
            HouseholdText.Clean(note, HouseholdFieldLimits.Note, "备注"),
            HouseholdText.CleanPurchaseLink(purchaseLink),
            mileageCycleKm,
            HouseholdAliases.Normalize(aliases),
            nextDue);
    }
}
