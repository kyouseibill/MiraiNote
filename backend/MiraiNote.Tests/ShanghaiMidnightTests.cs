using Microsoft.EntityFrameworkCore;
using MiraiNote.Core.Services;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Dtos.WorkLogs;
using Xunit;

namespace MiraiNote.Tests;

public class ShanghaiMidnightTests : IDisposable
{
    private readonly MiraiTestFixture _fx = new();

    [Fact]
    public async Task WorkLogDate_StaysOnShanghaiCalendarDay()
    {
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        var service = new WorkLogService(db);
        var created = await service.CreateAsync(userId, new CreateWorkLogRequest
        {
            Title = "跨日",
            LogDate = new DateTime(2026, 10, 7)
        });

        Assert.Equal(new DateTime(2026, 10, 7), created.LogDate.Date);

        await using var read = _fx.CreateContext();
        var stored = await read.WorkLogs.SingleAsync(w => w.Id == created.Id);
        Assert.Equal(new DateTime(2026, 10, 7), stored.LogDate.Date);
    }

    [Fact]
    public void MemoReminder_ShanghaiMidnight_IsDueAtUtc1600()
    {
        var remindAt = ShanghaiClock.ParseToUtc("2026-10-07 00:00");
        Assert.Equal(new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc), remindAt);

        var before = new DateTime(2026, 10, 6, 15, 59, 0, DateTimeKind.Utc);
        var atMidnight = new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc);
        Assert.False(remindAt <= before);
        Assert.True(remindAt <= atMidnight);
    }

    [Fact]
    public async Task MemoReminder_StoredUtcInstant_RoundTrips()
    {
        var remindAt = ShanghaiClock.ParseToUtc("2026-10-07 00:00")!.Value;
        await using var db = _fx.CreateContext();
        var userId = await db.Users.Select(u => u.Id).SingleAsync();
        db.Memos.Add(new Memo
        {
            UserId = userId,
            Section = "work",
            Content = "上海零点",
            RemindAt = remindAt,
            RemindMethods = 2
        });
        await db.SaveChangesAsync();

        await using var read = _fx.CreateContext();
        var memo = await read.Memos.SingleAsync();
        Assert.Equal(new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc), memo.RemindAt);
        Assert.True(memo.RemindAt <= new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc));
        Assert.False(memo.RemindAt <= new DateTime(2026, 10, 6, 15, 59, 0, DateTimeKind.Utc));
    }

    public void Dispose() => _fx.Dispose();
}
