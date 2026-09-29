using MiraiNote.Core.Services.Household;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;
using Xunit;

namespace MiraiNote.Tests;

public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}

public class HouseholdCycleRulesTests
{
    private static readonly DateTimeOffset ShanghaiNewYearEve =
        new(2026, 9, 30, 16, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Today_UsesShanghai_WhenUtcDateIsStillPreviousDay()
    {
        var rules = new HouseholdCycleRules(new DelegatingHouseholdClock(new FixedTimeProvider(ShanghaiNewYearEve)));
        Assert.Equal(new DateOnly(2026, 10, 1), rules.Today());
    }

    [Theory]
    [InlineData(15, 59, 59, 2026, 9, 30)]
    [InlineData(16, 0, 0, 2026, 10, 1)]
    public void ShanghaiDate_ChangesAtUtc1600(int hour, int minute, int second, int year, int month, int day)
    {
        var utc = new DateTimeOffset(2026, 9, 30, hour, minute, second, TimeSpan.Zero);
        var date = ShanghaiClock.ToShanghaiDate(utc);
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Fact]
    public void Resolve_FallsBackToWindowsTimeZoneId()
    {
        var windows = TimeZoneInfo.CreateCustomTimeZone(
            ShanghaiClock.WindowsId, TimeSpan.FromHours(8), "CST", "CST");
        var zone = ShanghaiClock.Resolve(id =>
        {
            if (id == ShanghaiClock.IanaId)
                throw new TimeZoneNotFoundException(id);
            if (id == ShanghaiClock.WindowsId)
                return windows;
            throw new InvalidTimeZoneException(id);
        });

        Assert.Equal(ShanghaiClock.WindowsId, zone.Id);
        Assert.Equal(
            new DateOnly(2026, 10, 1),
            ShanghaiClock.ToShanghaiDate(ShanghaiNewYearEve, zone));
    }

    [Fact]
    public void Resolve_ThrowsWhenNeitherTimeZoneIdExists()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ShanghaiClock.Resolve(_ => throw new TimeZoneNotFoundException("missing")));
        Assert.Contains("Asia/Shanghai", ex.Message);
    }

    [Theory]
    [InlineData("2026-01-31", 1, HouseholdCycleUnit.Month, "2026-02-28")]
    [InlineData("2024-01-31", 1, HouseholdCycleUnit.Month, "2024-02-29")]
    [InlineData("2026-03-31", 1, HouseholdCycleUnit.Month, "2026-04-30")]
    [InlineData("2026-01-31", 3, HouseholdCycleUnit.Month, "2026-04-30")]
    [InlineData("2024-02-29", 1, HouseholdCycleUnit.Year, "2025-02-28")]
    [InlineData("2024-02-29", 4, HouseholdCycleUnit.Year, "2028-02-29")]
    [InlineData("2026-01-31", 1, HouseholdCycleUnit.Day, "2026-02-01")]
    [InlineData("2026-01-15", 24, HouseholdCycleUnit.Month, "2028-01-15")]
    public void AddCycle_ClampsEndOfMonthAndLeapDay(string start, int value, HouseholdCycleUnit unit, string expected)
    {
        var actual = HouseholdCycleRules.AddCycle(DateOnly.Parse(start), value, unit);
        Assert.Equal(DateOnly.Parse(expected), actual);
    }

    [Fact]
    public void ResolveCompletionDate_RejectsShanghaiTomorrow()
    {
        var rules = new HouseholdCycleRules(new DelegatingHouseholdClock(new FixedTimeProvider(ShanghaiNewYearEve)));
        var ex = Assert.Throws<BusinessException>(() => rules.ResolveCompletionDate(new DateOnly(2026, 10, 2)));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(new DateOnly(2026, 10, 1), rules.ResolveCompletionDate(null));
        Assert.Equal(new DateOnly(2026, 9, 30), rules.ResolveCompletionDate(new DateOnly(2026, 9, 30)));
    }

    [Theory]
    [InlineData("2026-09-30", UpcomingGroup.Overdue, 1, 0)]
    [InlineData("2026-10-01", UpcomingGroup.Within7Days, 0, 0)]
    [InlineData("2026-10-08", UpcomingGroup.Within7Days, 0, 7)]
    [InlineData("2026-10-09", UpcomingGroup.Within30Days, 0, 8)]
    [InlineData("2026-10-31", UpcomingGroup.Within30Days, 0, 30)]
    [InlineData("2026-11-01", UpcomingGroup.None, 0, 31)]
    public void Classify_UsesInclusiveBoundaries(string dueText, UpcomingGroup group, int overdue, int remaining)
    {
        var today = new DateOnly(2026, 10, 1);
        var due = DateOnly.Parse(dueText);
        Assert.Equal(group, HouseholdCycleRules.Classify(due, today, isPaused: false));
        var offsets = HouseholdCycleRules.DayOffsets(due, today);
        Assert.Equal(overdue, offsets.DaysOverdue);
        Assert.Equal(remaining, offsets.DaysRemaining);
    }

    [Fact]
    public void Classify_PausedItemIsNotOverdue()
    {
        var today = new DateOnly(2026, 10, 1);
        Assert.Equal(
            UpcomingGroup.None,
            HouseholdCycleRules.Classify(new DateOnly(2026, 9, 1), today, isPaused: true));
    }

    [Theory]
    [InlineData(0, 1, 0, 0, true)]
    [InlineData(2, 5, 0, 2, true)]
    [InlineData(1, 1, 0, 1, true)]
    [InlineData(5, 1, 4, 1, false)]
    [InlineData(3, 0, 3, 0, false)]
    public void DeductStock_NeverGoesNegative(int current, int requested, int stock, int actual, bool needsRestock)
    {
        var result = HouseholdCycleRules.DeductStock(current, requested);
        Assert.Equal(stock, result.NewStock);
        Assert.Equal(actual, result.ActualDeducted);
        Assert.Equal(needsRestock, result.NeedsRestock);
    }

    [Fact]
    public void DeductStock_RejectsNegativeRequest()
    {
        var ex = Assert.Throws<BusinessException>(() => HouseholdCycleRules.DeductStock(3, -1));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Normalize_RecurringDueDateComesFromLastDone_NotFromToday()
    {
        var draft = HouseholdItemDraft.Normalize(
            "空调滤网", null, "客厅", null,
            HouseholdItemType.Recurring, 1, HouseholdCycleUnit.Month,
            new DateOnly(2026, 1, 31), null,
            null, null, null, null, null, null, [" 滤网 ", "滤网"], new DateOnly(2026, 10, 1));

        Assert.Equal(new DateOnly(2026, 2, 28), draft.NextDueDate);
        Assert.Equal(HouseholdCategory.HomeMaintenance, draft.Category);
        Assert.Equal(7, draft.LeadDays);
        Assert.Equal(["滤网"], draft.Aliases);
    }

    [Fact]
    public void Normalize_OneOffUsesExpiryAndDoesNotRoll()
    {
        var draft = HouseholdItemDraft.Normalize(
            "护照", HouseholdCategory.Document, null, null,
            HouseholdItemType.OneOffExpiry, null, null,
            null, new DateOnly(2030, 5, 1),
            3, null, null, null, null, null, null, new DateOnly(2026, 10, 1));

        Assert.Equal(new DateOnly(2030, 5, 1), draft.NextDueDate);
        Assert.Equal(new DateOnly(2030, 5, 1), draft.ExpiryDate);
        Assert.Null(draft.CycleValue);
        Assert.Equal(3, draft.LeadDays);
    }

    [Fact]
    public void Normalize_RejectsMixedScheduleFields()
    {
        var recurring = Assert.Throws<BusinessException>(() => HouseholdItemDraft.Normalize(
            "年检", HouseholdCategory.Vehicle, null, null,
            HouseholdItemType.Recurring, 12, HouseholdCycleUnit.Month,
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1),
            null, null, null, null, null, null, null, new DateOnly(2026, 10, 1)));
        Assert.Equal(400, recurring.StatusCode);

        var oneOff = Assert.Throws<BusinessException>(() => HouseholdItemDraft.Normalize(
            "护照", HouseholdCategory.Document, null, null,
            HouseholdItemType.OneOffExpiry, 12, HouseholdCycleUnit.Month,
            null, new DateOnly(2030, 1, 1),
            null, null, null, null, null, null, null, new DateOnly(2026, 10, 1)));
        Assert.Equal(400, oneOff.StatusCode);
    }

    [Fact]
    public void PhotoRefs_AcceptUploadedRelativePathsOnly()
    {
        var stored = HouseholdPhotoRefs.Serialize(
        [
            "/uploads/1/images/a.jpg",
            " /uploads/1/images/a.jpg ",
            "/uploads/1/images/b.png"
        ]);
        Assert.Equal(["/uploads/1/images/a.jpg", "/uploads/1/images/b.png"], HouseholdPhotoRefs.Deserialize(stored));

        var ex = Assert.Throws<BusinessException>(() => HouseholdPhotoRefs.Normalize(["https://example.com/a.jpg"]));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JAVASCRIPT:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("\tjavascript:alert(1)")]
    [InlineData("java\nscript:alert(1)")]
    [InlineData("java\tscript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("/foo")]
    [InlineData("example.com/filter")]
    [InlineData("http://")]
    [InlineData("https://")]
    [InlineData(" HTTPS:// ")]
    [InlineData("http:evil.com")]
    [InlineData("http:///evil")]
    [InlineData("http:/evil.com")]
    [InlineData("https://example.com/\n")]
    [InlineData("https://example.com/\tpath")]
    [InlineData("https://ex\u200bample.com")]
    [InlineData("https://example.com/\u200c")]
    [InlineData("https://example.com/\u200d")]
    [InlineData("\uFEFFhttps://example.com")]
    [InlineData("\t")]
    public void PurchaseLink_RejectsUnsafeValues(string value)
    {
        var ex = Assert.Throws<BusinessException>(() => HouseholdText.CleanPurchaseLink(value));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com/item", "https://example.com/item")]
    [InlineData(" HTTP://shop.example/a ", "http://shop.example/a")]
    [InlineData("https://example.com/filter", "https://example.com/filter")]
    [InlineData("http://example.com/order", "http://example.com/order")]
    [InlineData("http://example.com/template", "http://example.com/template")]
    public void PurchaseLink_AllowsHttpAndHttps(string value, string expected)
    {
        Assert.Equal(expected, HouseholdText.CleanPurchaseLink(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PurchaseLink_AllowsEmpty(string? value)
    {
        Assert.Null(HouseholdText.CleanPurchaseLink(value));
    }
}
