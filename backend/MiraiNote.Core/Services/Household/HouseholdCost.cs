namespace MiraiNote.Core.Services.Household;

/// <summary>
/// 费用上限。完成记录和草稿的金额列是 decimal(18,2)，写入前统一卡在这个值。
/// </summary>
public static class HouseholdCost
{
    public const decimal MaxAmount = 999_999_999.99m;
}
