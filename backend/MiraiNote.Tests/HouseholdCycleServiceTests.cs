using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Xunit;
using Microsoft.EntityFrameworkCore;
using MiraiNote.API.Controllers;
using MiraiNote.Core.Services.Household;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Tests;

public class HouseholdCycleServiceTests
{
    [Fact]
    public void Controllers_RequireBearerAuth()
    {
        foreach (var type in new[]
        {
            typeof(HouseholdController),
            typeof(HouseholdItemsController),
            typeof(HouseholdConsumablesController)
        })
        {
            Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        }
    }

    [Fact]
    public async Task FirstUse_CreatesHouseholdWithCallerAsAdmin()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var mine = await fx.Household.GetMineAsync(fx.OwnerId);
        Assert.Equal(Household.DefaultName, mine.Name);
        Assert.Equal(HouseholdRole.Admin, mine.MyRole);

        var again = await fx.Household.GetMineAsync(fx.OwnerId);
        Assert.Equal(mine.Id, again.Id);
    }

    [Fact]
    public async Task Members_AreIsolated_AndOnlyAdminManagesThem()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var ownerHome = await fx.Household.GetMineAsync(fx.OwnerId);
        var outsiderId = await fx.AddUserAsync("outsider");
        var outsiderHome = await fx.Household.GetMineAsync(outsiderId);
        Assert.NotEqual(ownerHome.Id, outsiderHome.Id);

        var occupied = await Assert.ThrowsAsync<BusinessException>(() =>
            fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "outsider" }));
        Assert.Equal(400, occupied.StatusCode);

        var memberId = await fx.AddUserAsync("member");
        var added = await fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest
        {
            UserIdentifier = "MEMBER",
            Role = HouseholdRole.Member
        });
        Assert.Equal(memberId, added.UserId);

        var shared = await fx.Household.GetMineAsync(memberId);
        Assert.Equal(ownerHome.Id, shared.Id);
        Assert.Equal(HouseholdRole.Member, shared.MyRole);

        var strangerId = await fx.AddUserAsync("stranger");
        var denied = await Assert.ThrowsAsync<BusinessException>(() =>
            fx.Household.AddMemberAsync(memberId, new AddHouseholdMemberRequest { UserIdentifier = "stranger" }));
        Assert.Equal(403, denied.StatusCode);
        Assert.NotEqual(ownerHome.Id, (await fx.Household.GetMineAsync(strangerId)).Id);

        var cross = await Assert.ThrowsAsync<BusinessException>(() =>
            fx.Household.RemoveMemberAsync(memberId, ownerHome.MyMemberId));
        Assert.Equal(403, cross.StatusCode);
    }

    [Fact]
    public async Task HouseholdKeepsAtLeastOneAdmin_RemovedMemberStartsANewHousehold()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var otherId = await fx.AddUserAsync("other");
        var owner = await fx.Household.GetMineAsync(fx.OwnerId);
        await fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest
        {
            UserIdentifier = "other",
            Role = HouseholdRole.Member
        });

        var stuck = await Assert.ThrowsAsync<BusinessException>(() =>
            fx.Household.RemoveMemberAsync(fx.OwnerId, owner.MyMemberId));
        Assert.Equal(400, stuck.StatusCode);

        var promoted = await fx.Household.ChangeRoleAsync(fx.OwnerId, (await fx.Household.ListMembersAsync(fx.OwnerId))
            .Single(m => m.UserId == otherId).Id, new ChangeHouseholdMemberRoleRequest { Role = HouseholdRole.Admin });
        Assert.Equal(HouseholdRole.Admin, promoted.Role);

        await fx.Household.RemoveMemberAsync(fx.OwnerId, owner.MyMemberId);
        var restarted = await fx.Household.GetMineAsync(fx.OwnerId);
        Assert.NotEqual(owner.Id, restarted.Id);
        Assert.Equal(HouseholdRole.Admin, restarted.MyRole);

        var original = await fx.Household.GetMineAsync(otherId);
        Assert.Equal(owner.Id, original.Id);
    }

    [Fact]
    public async Task DeleteItem_IsAdminOnly_UntilPolicyOpensIt()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var memberId = await fx.AddUserAsync("member");
        await fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "member" });
        var extra = await fx.CreateRecurringAsync(fx.OwnerId, "可删", new DateOnly(2026, 9, 1));
        await fx.Items.DeleteAsync(fx.OwnerId, extra.Id);

        var item = await fx.CreateRecurringAsync(fx.OwnerId, "空调滤网", new DateOnly(2026, 9, 1));

        var denied = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.DeleteAsync(memberId, item.Id));
        Assert.Equal(403, denied.StatusCode);

        var open = fx.ItemsWith(new HouseholdAccessPolicy { OnlyAdminCanDeleteItems = false });
        await open.DeleteAsync(memberId, item.Id);
        var missing = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.GetAsync(fx.OwnerId, item.Id));
        Assert.Equal(404, missing.StatusCode);

        var raw = await fx.Db.HouseholdItems.IgnoreQueryFilters().SingleAsync(i => i.Id == item.Id);
        Assert.True(raw.IsDeleted);
    }

    [Fact]
    public async Task CrossHouseholdAccess_IsHidden()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var otherId = await fx.AddUserAsync("other");
        var item = await fx.CreateRecurringAsync(fx.OwnerId, "年检", new DateOnly(2026, 1, 1), 12);
        var otherHome = await fx.Household.GetMineAsync(otherId);

        var get = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.GetAsync(otherId, item.Id));
        Assert.Equal(404, get.StatusCode);
        var history = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.HistoryAsync(otherId, item.Id));
        Assert.Equal(404, history.StatusCode);

        var assign = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CreateAsync(fx.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "借人",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 1),
            AssigneeMemberId = otherHome.MyMemberId
        }));
        Assert.Equal(403, assign.StatusCode);

        var consumable = await fx.Consumables.CreateAsync(otherId, new SaveHouseholdConsumableRequest { Name = "机油", CurrentStock = 2 });
        var link = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CreateAsync(fx.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "保养",
            Category = HouseholdCategory.Vehicle,
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 6,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 3, 1),
            ConsumableId = consumable.Id
        }));
        Assert.Equal(403, link.StatusCode);

        var hidden = await Assert.ThrowsAsync<BusinessException>(() => fx.Consumables.GetAsync(fx.OwnerId, consumable.Id));
        Assert.Equal(404, hidden.StatusCode);
    }

    [Fact]
    public async Task Complete_UsesActualDateInShanghai_AndRecomputesDue()
    {
        await using var fx = new HouseholdFixture(new DateTimeOffset(2026, 9, 30, 16, 30, 0, TimeSpan.Zero));
        var item = await fx.CreateRecurringAsync(fx.OwnerId, "洗衣机槽清洁", new DateOnly(2026, 8, 1), 1);

        var future = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 10, 2)
        }));
        Assert.Equal(400, future.StatusCode);

        var done = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 30),
            Cost = 680.126m,
            PhotoRefs = ["/uploads/1/images/done.jpg"],
            Note = "补记"
        });

        Assert.Equal(new DateOnly(2026, 9, 30), done.Item.LastDoneDate);
        Assert.Equal(new DateOnly(2026, 10, 30), done.Item.NextDueDate);
        Assert.Equal(680.13m, done.Record.Cost);
        Assert.Equal("tester", done.Record.CompletedByUsername);
        Assert.Equal(["/uploads/1/images/done.jpg"], done.Record.PhotoRefs);

        var sameDay = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 10, 1)
        });
        Assert.Equal(new DateOnly(2026, 11, 1), sameDay.Item.NextDueDate);

        var history = await fx.Items.HistoryAsync(fx.OwnerId, item.Id);
        Assert.Equal(
            [new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 30)],
            history.Select(r => r.CompletedOn).ToArray());
    }

    [Fact]
    public async Task Complete_MonthAndYearRollover_FollowCompletionDate()
    {
        await using var fx = new HouseholdFixture(Utc(2024, 3, 1, 2, 0));
        var monthly = await fx.CreateRecurringAsync(fx.OwnerId, "滤网", new DateOnly(2023, 12, 1), 1);
        var month = await fx.Items.CompleteAsync(fx.OwnerId, monthly.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2024, 1, 31)
        });
        Assert.Equal(new DateOnly(2024, 2, 29), month.Item.NextDueDate);

        var yearly = await fx.Items.CreateAsync(fx.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "闰年纪念日",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Year,
            LastDoneDate = new DateOnly(2024, 2, 29)
        });
        Assert.Equal(new DateOnly(2025, 2, 28), yearly.NextDueDate);
    }

    [Fact]
    public async Task OneOff_DoesNotAutoRoll_RenewalReplacesExpiry()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var item = await fx.Items.CreateAsync(fx.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "护照",
            Category = HouseholdCategory.Document,
            ItemType = HouseholdItemType.OneOffExpiry,
            ExpiryDate = new DateOnly(2026, 9, 1),
            MileageCycleKm = 5000,
            LeadDays = 0
        });
        Assert.Equal(new DateOnly(2026, 9, 1), item.NextDueDate);
        Assert.Equal(5000, item.MileageCycleKm);
        Assert.Equal(0, item.LeadDays);

        var renewalOnRecurring = await fx.CreateRecurringAsync(fx.OwnerId, "保养", new DateOnly(2026, 4, 1), 6);
        var rejected = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CompleteAsync(
            fx.OwnerId, renewalOnRecurring.Id, new CompleteHouseholdItemRequest { NewExpiryDate = new DateOnly(2027, 1, 1) }));
        Assert.Equal(400, rejected.StatusCode);

        var kept = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 15)
        });
        Assert.Equal(new DateOnly(2026, 9, 1), kept.Item.ExpiryDate);
        Assert.Equal(new DateOnly(2026, 9, 1), kept.Item.NextDueDate);
        Assert.Null(kept.Record.NewExpiryDate);

        var renewed = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            NewExpiryDate = new DateOnly(2036, 9, 1)
        });
        Assert.Equal(new DateOnly(2036, 9, 1), renewed.Item.ExpiryDate);
        Assert.Equal(new DateOnly(2036, 9, 1), renewed.Item.NextDueDate);
        Assert.Equal(new DateOnly(2036, 9, 1), renewed.Record.NewExpiryDate);
    }

    [Fact]
    public async Task Stock_ClampsAtZero_SkipLeavesItAlone_RestockResetsReminderFlag()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var empty = await fx.Consumables.CreateAsync(fx.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 0,
            Unit = "个"
        });
        Assert.Equal(1, empty.RestockThreshold);
        Assert.True(empty.IsLowStock);

        var item = await fx.CreateRecurringAsync(fx.OwnerId, "净水器 PP 棉", new DateOnly(2026, 4, 1), 6, empty.Id);
        var clamped = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest());
        Assert.Equal(0, clamped.ConsumableStockAfter);
        Assert.Equal(0, clamped.ConsumableQuantityDeducted);
        Assert.True(clamped.NeedsRestock);

        var stocked = await fx.Consumables.UpdateAsync(fx.OwnerId, empty.Id, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 2,
            RestockThreshold = 1
        });
        Assert.Equal(2, stocked.CurrentStock);

        var partial = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            ConsumableQuantity = 5
        });
        Assert.Equal(0, partial.ConsumableStockAfter);
        Assert.Equal(2, partial.ConsumableQuantityDeducted);
        Assert.True(partial.NeedsRestock);

        await fx.Consumables.RestockAsync(fx.OwnerId, empty.Id, new RestockHouseholdConsumableRequest { Quantity = 5 });
        var skipped = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            SkipConsumableDeduction = true,
            ConsumableQuantity = 3
        });
        Assert.False(skipped.NeedsRestock);
        Assert.Equal(0, skipped.ConsumableQuantityDeducted);
        Assert.Null(skipped.ConsumableStockAfter);
        Assert.Equal(5, (await fx.Consumables.GetAsync(fx.OwnerId, empty.Id)).CurrentStock);

        var row = await fx.Db.HouseholdConsumables.SingleAsync(c => c.Id == empty.Id);
        row.LowStockReminderSent = true;
        await fx.Db.SaveChangesAsync();
        var restocked = await fx.Consumables.RestockAsync(fx.OwnerId, empty.Id, new RestockHouseholdConsumableRequest { Quantity = 1 });
        Assert.Equal(6, restocked.CurrentStock);
        Assert.False(restocked.LowStockReminderSent);

        var negative = await Assert.ThrowsAsync<BusinessException>(() => fx.Consumables.CreateAsync(fx.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "坏库存",
            CurrentStock = -1
        }));
        Assert.Equal(400, negative.StatusCode);

        var blocked = await Assert.ThrowsAsync<BusinessException>(() => fx.Consumables.DeleteAsync(fx.OwnerId, empty.Id));
        Assert.Equal(400, blocked.StatusCode);
    }

    [Fact]
    public async Task Upcoming_GroupsByShanghaiToday_AndSkipsPaused()
    {
        await using var fx = new HouseholdFixture(new DateTimeOffset(2026, 9, 30, 16, 30, 0, TimeSpan.Zero));
        await fx.CreateOneOffAsync("逾期", new DateOnly(2026, 9, 30));
        await fx.CreateOneOffAsync("今天", new DateOnly(2026, 10, 1));
        await fx.CreateOneOffAsync("七天边界", new DateOnly(2026, 10, 8));
        await fx.CreateOneOffAsync("八天", new DateOnly(2026, 10, 9));
        await fx.CreateOneOffAsync("三十天", new DateOnly(2026, 10, 31));
        await fx.CreateOneOffAsync("太远", new DateOnly(2026, 11, 1));
        await fx.CreateOneOffAsync("暂停", new DateOnly(2026, 9, 1), paused: true);
        await fx.CreateOneOffAsync("车辆", new DateOnly(2026, 9, 20), HouseholdCategory.Vehicle);

        var upcoming = await fx.Items.UpcomingAsync(fx.OwnerId, new HouseholdUpcomingQuery());
        Assert.Equal(new DateOnly(2026, 10, 1), upcoming.Today);
        Assert.Equal(["车辆", "逾期"], upcoming.Overdue.Select(i => i.Name).ToArray());
        Assert.Equal(1, upcoming.Overdue.Single(i => i.Name == "逾期").DaysOverdue);
        Assert.Equal(["今天", "七天边界"], upcoming.Within7Days.Select(i => i.Name).ToArray());
        Assert.Equal(0, upcoming.Within7Days[0].DaysRemaining);
        Assert.Equal(7, upcoming.Within7Days[1].DaysRemaining);
        Assert.Equal(["八天", "三十天"], upcoming.Within30Days.Select(i => i.Name).ToArray());
        Assert.Equal(8, upcoming.Within30Days[0].DaysRemaining);
        Assert.Equal(30, upcoming.Within30Days[1].DaysRemaining);
        Assert.DoesNotContain(upcoming.Overdue, i => i.Name == "暂停");
        Assert.DoesNotContain(upcoming.Within7Days, i => i.Name == "太远");

        var vehicles = await fx.Items.UpcomingAsync(fx.OwnerId, new HouseholdUpcomingQuery { Category = HouseholdCategory.Vehicle });
        Assert.Equal(["车辆"], vehicles.Overdue.Select(i => i.Name).ToArray());
        Assert.Empty(vehicles.Within7Days);

        var active = await fx.Items.ListAsync(fx.OwnerId, new HouseholdItemListQuery { IncludePaused = false });
        Assert.DoesNotContain(active, i => i.Name == "暂停");
    }

    [Fact]
    public async Task Templates_MatchPrd_AndPrefillOverridableCycle()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var templates = await fx.Items.ListTemplatesAsync(fx.OwnerId);
        Assert.Equal(
            [
                "空调滤网", "净水器 PP 棉", "净水器活性炭", "净水器 RO 膜", "油烟机清洗",
                "热水器除垢", "烟雾报警器电池", "冰箱除味剂", "洗衣机槽清洁",
                "常规保养", "年检", "交强险/商业险",
                "身份证", "护照", "驾照", "签证", "家电保修"
            ],
            templates.Select(t => t.Name).ToArray());
        Assert.Equal(HouseholdItemType.OneOffExpiry, templates.Single(t => t.Name == "护照").ItemType);
        Assert.Equal(24, templates.Single(t => t.Name == "净水器 RO 膜").CycleValue);

        var created = await fx.Items.CreateFromTemplateAsync(fx.OwnerId, new CreateHouseholdItemFromTemplateRequest
        {
            TemplateId = templates.Single(t => t.Name == "空调滤网").Id,
            LastDoneDate = new DateOnly(2026, 1, 31),
            Location = "主卧",
            CycleValue = 2
        });
        Assert.Equal("空调滤网", created.Name);
        Assert.Equal(HouseholdCategory.HomeMaintenance, created.Category);
        Assert.Equal(2, created.CycleValue);
        Assert.Equal(HouseholdCycleUnit.Month, created.CycleUnit);
        Assert.Equal(new DateOnly(2026, 3, 31), created.NextDueDate);
        Assert.Equal("主卧", created.Location);

        var passport = await fx.Items.CreateFromTemplateAsync(fx.OwnerId, new CreateHouseholdItemFromTemplateRequest
        {
            TemplateId = templates.Single(t => t.Name == "护照").Id,
            ExpiryDate = new DateOnly(2031, 4, 2)
        });
        Assert.Equal(HouseholdItemType.OneOffExpiry, passport.ItemType);
        Assert.Equal(new DateOnly(2031, 4, 2), passport.NextDueDate);
    }

    [Fact]
    public async Task History_KeepsUsernameAfterMemberIsRemoved()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var helperId = await fx.AddUserAsync("helper");
        var helper = await fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest
        {
            UserIdentifier = "helper",
            Role = HouseholdRole.Admin
        });
        var item = await fx.Items.CreateAsync(fx.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "油烟机清洗",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 6,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 1, 1),
            AssigneeMemberId = helper.Id
        });
        Assert.Equal(helper.Id, item.AssigneeMemberId);
        await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 6, 1),
            CompletedByMemberId = helper.Id
        });

        await fx.Household.RemoveMemberAsync(fx.OwnerId, helper.Id);
        var history = await fx.Items.HistoryAsync(fx.OwnerId, item.Id);
        Assert.Equal("helper", history[0].CompletedByUsername);
        Assert.Equal(helperId, history[0].CompletedByUserId);

        var reloaded = await fx.Items.GetAsync(fx.OwnerId, item.Id);
        Assert.Null(reloaded.AssigneeMemberId);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private sealed class HouseholdFixture : IAsyncDisposable
    {
        private readonly MiraiTestFixture _fx;
        private readonly HouseholdCycleRules _rules;
        private readonly HouseholdAccessPolicy _policy = HouseholdAccessPolicy.Default;

        public MiraiNoteDbContext Db { get; }
        public HouseholdService Household { get; }
        public HouseholdItemService Items { get; }
        public HouseholdConsumableService Consumables { get; }
        public int OwnerId { get; }

        public HouseholdFixture(DateTimeOffset utcNow)
        {
            _fx = new MiraiTestFixture();
            Db = _fx.CreateContext();
            OwnerId = Db.Users.Single().Id;
            _rules = new HouseholdCycleRules(new FixedTimeProvider(utcNow));
            var access = new HouseholdAccessService(Db);
            Household = new HouseholdService(Db, access);
            Items = new HouseholdItemService(Db, access, _rules, _policy);
            Consumables = new HouseholdConsumableService(Db, access);
        }

        public HouseholdItemService ItemsWith(HouseholdAccessPolicy policy) =>
            new(Db, new HouseholdAccessService(Db), _rules, policy);

        public async Task<int> AddUserAsync(string username)
        {
            var user = new User
            {
                Username = username,
                Email = username + "@example.com",
                PasswordHash = "hash",
                IsEmailVerified = true
            };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();
            return user.Id;
        }

        public Task<HouseholdItemDto> CreateRecurringAsync(
            int userId, string name, DateOnly lastDone, int months = 1, int? consumableId = null) =>
            Items.CreateAsync(userId, new CreateHouseholdItemRequest
            {
                Name = name,
                ItemType = HouseholdItemType.Recurring,
                CycleValue = months,
                CycleUnit = HouseholdCycleUnit.Month,
                LastDoneDate = lastDone,
                ConsumableId = consumableId
            });

        public Task<HouseholdItemDto> CreateOneOffAsync(
            string name, DateOnly expiry, HouseholdCategory category = HouseholdCategory.HomeMaintenance, bool paused = false) =>
            Items.CreateAsync(OwnerId, new CreateHouseholdItemRequest
            {
                Name = name,
                Category = category,
                ItemType = HouseholdItemType.OneOffExpiry,
                ExpiryDate = expiry,
                IsPaused = paused
            });

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _fx.Dispose();
        }
    }
}
