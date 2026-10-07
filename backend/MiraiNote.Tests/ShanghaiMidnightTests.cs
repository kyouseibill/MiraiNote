using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MiraiNote.API.Infrastructure;
using MiraiNote.Core.Services;
using MiraiNote.Data.Entities;
using MiraiNote.Shared;
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

    [Fact]
    public void Today_DoesNotFlipAcrossUtcMidnight()
    {
        var shanghaiMorning = new DateTimeOffset(2026, 10, 6, 23, 59, 0, TimeSpan.Zero);
        var shanghaiLater = new DateTimeOffset(2026, 10, 7, 0, 1, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 10, 7), ShanghaiClock.Today(shanghaiMorning));
        Assert.Equal(new DateOnly(2026, 10, 7), ShanghaiClock.Today(shanghaiLater));
        Assert.Equal(ShanghaiClock.Today(shanghaiMorning), ShanghaiClock.Today(shanghaiLater));

        var range = ShanghaiClock.DayRangeUtc(new DateOnly(2026, 10, 7));
        Assert.Equal(new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc), range.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 7, 16, 0, 0, DateTimeKind.Utc), range.EndUtc);
        Assert.True(shanghaiMorning.UtcDateTime >= range.StartUtc && shanghaiMorning.UtcDateTime < range.EndUtc);
        Assert.True(shanghaiLater.UtcDateTime >= range.StartUtc && shanghaiLater.UtcDateTime < range.EndUtc);
    }

    [Fact]
    public void CalendarDate_SerializesAsDateOnly_WhileInstantsKeepZone()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new UtcDateTimeJsonConverter());
        options.Converters.Add(new UtcNullableDateTimeJsonConverter());

        var dto = new WorkLogDto
        {
            LogDate = new DateTime(2026, 10, 8),
            CreatedAt = new DateTime(2026, 10, 7, 16, 0, 0, DateTimeKind.Utc)
        };
        var json = JsonSerializer.Serialize(dto, options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("2026-10-08", doc.RootElement.GetProperty("LogDate").GetString());
        Assert.Equal("2026-10-07T16:00:00Z", doc.RootElement.GetProperty("CreatedAt").GetString());

        var roundTrip = JsonSerializer.Deserialize<WorkLogDto>("""{"LogDate":"2026-10-08T00:00:00Z","CreatedAt":"2026-10-07T16:00:00Z"}""", options);
        Assert.NotNull(roundTrip);
        Assert.Equal(new DateTime(2026, 10, 8), roundTrip.LogDate);
    }

    public void Dispose() => _fx.Dispose();
}
