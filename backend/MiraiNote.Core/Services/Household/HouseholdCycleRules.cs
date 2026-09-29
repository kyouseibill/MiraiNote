using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

/// <summary>近期到期分组。暂停事项不属于任何一组。</summary>
public enum UpcomingGroup
{
    None = 0,
    Overdue = 1,
    Within7Days = 2,
    Within30Days = 3
}

/// <summary>
/// 家务周期的纯规则：上海日历日、月末截断、从实际完成日顺延、库存不为负。
/// 今天由可注入的 <see cref="TimeProvider"/> 决定，测试可以固定 UTC 时刻。
/// </summary>
public sealed class HouseholdCycleRules
{
    /// <summary>within7Days 含今天和今天之后 7 天。</summary>
    public const int Within7DayWindow = 7;

    /// <summary>within30Days 含今天之后 8 天到 30 天。今天到今天+7 已归入 7 天组。</summary>
    public const int Within30DayWindow = 30;

    public const int DefaultLeadDays = 7;
    public const int DefaultRestockThreshold = 1;
    public const int DefaultDeductionQuantity = 1;

    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _shanghai;

    public HouseholdCycleRules(TimeProvider clock)
    {
        _clock = clock;
        _shanghai = ShanghaiClock.Resolve();
    }

    public DateOnly Today() => ShanghaiClock.ToShanghaiDate(_clock.GetUtcNow(), _shanghai);

    /// <summary>缺省今天（上海）。晚于今天则 400。</summary>
    public DateOnly ResolveCompletionDate(DateOnly? requested)
    {
        var today = Today();
        var date = requested ?? today;
        if (date > today)
            throw new BusinessException("完成日期不能晚于今天", 400);
        return date;
    }

    /// <summary>
    /// 按月、按年时，目标月没有该日则取当月最后一天。
    /// DateOnly.AddMonths / AddYears 的截断与 PRD 一致，例如 1/31 + 1 月 = 2/28（闰年 2/29），2/29 + 1 年 = 2/28。
    /// </summary>
    public static DateOnly AddCycle(DateOnly start, int value, HouseholdCycleUnit unit)
    {
        if (value <= 0)
            throw new BusinessException("周期数值必须大于 0", 400);
        if (value > 3650)
            throw new BusinessException("周期数值过大", 400);

        try
        {
            return unit switch
            {
                HouseholdCycleUnit.Day => start.AddDays(value),
                HouseholdCycleUnit.Month => start.AddMonths(value),
                HouseholdCycleUnit.Year => start.AddYears(value),
                _ => throw new BusinessException("不支持的周期单位", 400)
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new BusinessException("周期计算结果超出日期范围", 400);
        }
    }

    /// <summary>
    /// overdue：到期日早于今天。
    /// within7Days：今天到今天+7（含）。
    /// within30Days：今天+8 到今天+30（含）。
    /// 更远的日期和暂停事项返回 <see cref="UpcomingGroup.None"/>。
    /// </summary>
    public static UpcomingGroup Classify(DateOnly due, DateOnly today, bool isPaused)
    {
        if (isPaused)
            return UpcomingGroup.None;
        if (due < today)
            return UpcomingGroup.Overdue;
        if (due <= today.AddDays(Within7DayWindow))
            return UpcomingGroup.Within7Days;
        if (due <= today.AddDays(Within30DayWindow))
            return UpcomingGroup.Within30Days;
        return UpcomingGroup.None;
    }

    public static (int DaysOverdue, int DaysRemaining) DayOffsets(DateOnly due, DateOnly today)
    {
        var delta = due.DayNumber - today.DayNumber;
        return delta < 0 ? (-delta, 0) : (0, delta);
    }

    /// <summary>
    /// 库存不为负。请求扣减超过现存量时只扣到 0。
    /// 只要这次扣减之后库存为 0（含原本就是 0），<see cref="StockDeduction.NeedsRestock"/> 为 true。
    /// </summary>
    public static StockDeduction DeductStock(int currentStock, int requested)
    {
        if (requested < 0)
            throw new BusinessException("扣减数量不能为负", 400);
        if (currentStock < 0)
            currentStock = 0;
        if (requested == 0)
            return new StockDeduction(currentStock, 0, false);

        var actual = Math.Min(currentStock, requested);
        var newStock = currentStock - actual;
        return new StockDeduction(newStock, actual, newStock == 0);
    }
}

public readonly record struct StockDeduction(int NewStock, int ActualDeducted, bool NeedsRestock);
