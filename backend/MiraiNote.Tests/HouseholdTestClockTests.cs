using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MiraiNote.API.Controllers;
using MiraiNote.Core.Services.Household;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Tests;

public class HouseholdTestClockTests
{
    private static readonly DateTimeOffset Sep30At2359Shanghai =
        new(2026, 9, 30, 15, 59, 0, TimeSpan.Zero);

    [Fact]
    public void TestClockController_DoesNotRequireAuthBeforeTheFeatureCheck()
    {
        Assert.Null(typeof(HouseholdTestClockController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Policy_IsForceDisabledInProduction()
    {
        var options = new HouseholdOptions { TestClock = new HouseholdTestClockOptions { Enabled = true } };
        Assert.False(HouseholdTestClockPolicy.IsEnabled(options, new TestHostEnvironment(Environments.Production)));
        Assert.True(HouseholdTestClockPolicy.IsEnabled(options, new TestHostEnvironment(Environments.Development)));
        Assert.False(HouseholdTestClockPolicy.IsEnabled(
            new HouseholdOptions(),
            new TestHostEnvironment(Environments.Development)));
    }

    [Fact]
    public void FakeClock_ShanghaiMidnightBoundary_AndMonthRollover()
    {
        var clock = new AdjustableHouseholdTimeProvider(new FixedTimeProvider(Sep30At2359Shanghai));
        var rules = new HouseholdCycleRules(clock);

        Assert.Equal(new DateOnly(2026, 9, 30), rules.Today());

        clock.SetAbsolute(new DateTimeOffset(2026, 9, 30, 16, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 10, 1), rules.Today());

        clock.SetOffset(TimeSpan.FromMinutes(-1));
        Assert.Equal(new DateOnly(2026, 9, 30), rules.Today());
        clock.Reset();
        Assert.Equal(new DateOnly(2026, 9, 30), rules.Today());

        clock.SetAbsolute(new DateTimeOffset(2026, 1, 31, 4, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 1, 31), rules.Today());
        Assert.Equal(new DateOnly(2026, 2, 28), HouseholdCycleRules.AddCycle(rules.Today(), 1, HouseholdCycleUnit.Month));

        clock.SetAbsolute(new DateTimeOffset(2024, 1, 31, 4, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2024, 2, 29), HouseholdCycleRules.AddCycle(rules.Today(), 1, HouseholdCycleUnit.Month));

        clock.SetAbsolute(new DateTimeOffset(2024, 2, 29, 4, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2024, 2, 29), rules.Today());
        Assert.Equal(new DateOnly(2025, 2, 28), HouseholdCycleRules.AddCycle(rules.Today(), 1, HouseholdCycleUnit.Year));
    }

    [Fact]
    public async Task Endpoints_Return404WhenDisabled_AndOnlySystemAdminCanChangeClock()
    {
        await using var fx = new HouseholdFixture(Sep30At2359Shanghai);
        var disabled = fx.CreateClock(enabled: false, Environments.Development);
        var missing = await Assert.ThrowsAsync<BusinessException>(() => disabled.GetAsync(fx.OwnerId));
        Assert.Equal(404, missing.StatusCode);

        var production = fx.CreateClock(enabled: true, Environments.Production);
        var blocked = await Assert.ThrowsAsync<BusinessException>(() => production.SetAsync(fx.OwnerId, new SetHouseholdTestClockRequest
        {
            UtcNow = new DateTimeOffset(2026, 9, 30, 16, 0, 0, TimeSpan.Zero)
        }));
        Assert.Equal(404, blocked.StatusCode);
        Assert.Equal(new DateOnly(2026, 9, 30), new HouseholdCycleRules(fx.Clock).Today());

        var enabled = fx.CreateClock(enabled: true, Environments.Development);
        var forbidden = await Assert.ThrowsAsync<BusinessException>(() => enabled.GetAsync(fx.OwnerId));
        Assert.Equal(403, forbidden.StatusCode);

        await fx.MakeOwnerAdminAsync();
        var current = await enabled.GetAsync(fx.OwnerId);
        Assert.Equal("System", current.Mode);
        Assert.Equal(new DateOnly(2026, 9, 30), current.ShanghaiToday);

        var shifted = await enabled.SetAsync(fx.OwnerId, new SetHouseholdTestClockRequest
        {
            UtcNow = new DateTimeOffset(2026, 9, 30, 16, 0, 0, TimeSpan.Zero)
        });
        Assert.Equal("Absolute", shifted.Mode);
        Assert.Equal(new DateOnly(2026, 10, 1), shifted.ShanghaiToday);
        Assert.Equal(new DateOnly(2026, 10, 1), new HouseholdCycleRules(fx.Clock).Today());

        var both = await Assert.ThrowsAsync<BusinessException>(() => enabled.SetAsync(fx.OwnerId, new SetHouseholdTestClockRequest
        {
            UtcNow = shifted.UtcNow,
            OffsetSeconds = 60
        }));
        Assert.Equal(400, both.StatusCode);

        var offset = await enabled.SetAsync(fx.OwnerId, new SetHouseholdTestClockRequest { OffsetSeconds = 60 });
        Assert.Equal("Offset", offset.Mode);
        Assert.Equal(new DateOnly(2026, 10, 1), offset.ShanghaiToday);

        var reset = await enabled.ResetAsync(fx.OwnerId);
        Assert.Equal("System", reset.Mode);
        Assert.Equal(new DateOnly(2026, 9, 30), reset.ShanghaiToday);
    }

    [Fact]
    public void PublicBaseUrl_HasNoBuiltInHost()
    {
        var empty = new HouseholdLinkBuilder(Options.Create(new HouseholdOptions()));
        Assert.Null(empty.ItemPage(12));

        var builder = new HouseholdLinkBuilder(Options.Create(new HouseholdOptions
        {
            PublicBaseUrl = "https://example.test/app/"
        }));
        Assert.Equal("https://example.test/app/household/items/12", builder.ItemPage(12));
    }

    private sealed class HouseholdFixture : IAsyncDisposable
    {
        private readonly MiraiTestFixture _fx;

        public AdjustableHouseholdTimeProvider Clock { get; }
        public int OwnerId { get; }
        private readonly MiraiNote.Data.Context.MiraiNoteDbContext _db;

        public HouseholdFixture(DateTimeOffset utcNow)
        {
            _fx = new MiraiTestFixture();
            _db = _fx.CreateContext();
            OwnerId = _db.Users.Single().Id;
            Clock = new AdjustableHouseholdTimeProvider(new FixedTimeProvider(utcNow));
        }

        public HouseholdTestClockService CreateClock(bool enabled, string environment) =>
            new(Clock,
                Options.Create(new HouseholdOptions
                {
                    TestClock = new HouseholdTestClockOptions { Enabled = enabled }
                }),
                new TestHostEnvironment(environment),
                _db);

        public async Task MakeOwnerAdminAsync()
        {
            var user = await _db.Users.SingleAsync(u => u.Id == OwnerId);
            user.IsAdmin = true;
            await _db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            _fx.Dispose();
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName) => EnvironmentName = environmentName;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "MiraiNote.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
