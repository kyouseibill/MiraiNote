using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
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

    [Fact]
    public async Task MemberList_HidesEmailFromMembers_AndAddFailuresShareOneMessage()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var memberId = await fx.AddUserAsync("member");
        await fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "member" });

        var asAdmin = await fx.Household.ListMembersAsync(fx.OwnerId);
        Assert.Contains(asAdmin, m => m.UserId == memberId && m.Email == "member@example.com");
        Assert.Contains(asAdmin, m => m.UserId == fx.OwnerId && m.Email == "tester@example.com");

        var asMember = await fx.Household.ListMembersAsync(memberId);
        Assert.All(asMember, m => Assert.Null(m.Email));
        Assert.Contains(asMember, m => m.Username == "member");

        var missing = await Assert.ThrowsAsync<BusinessException>(() =>
            fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "nobody" }));
        var outsiderId = await fx.AddUserAsync("outsider");
        await fx.Household.GetMineAsync(outsiderId);
        var occupied = await Assert.ThrowsAsync<BusinessException>(() =>
            fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "outsider" }));
        Assert.Equal(400, missing.StatusCode);
        Assert.Equal(400, occupied.StatusCode);
        Assert.Equal(HouseholdService.AddMemberRejectedMessage, missing.Message);
        Assert.Equal(missing.Message, occupied.Message);

        var inactiveId = await fx.AddUserAsync("inactive");
        var inactive = await fx.Db.Users.SingleAsync(u => u.Id == inactiveId);
        inactive.IsActive = false;
        await fx.Db.SaveChangesAsync();
        var disabled = await Assert.ThrowsAsync<BusinessException>(() =>
            fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "inactive" }));
        Assert.Equal(missing.Message, disabled.Message);
    }

    [Fact]
    public async Task Members_CanCreateAndComplete_ButCannotEditPauseOrDelete()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var memberId = await fx.AddUserAsync("member");
        await fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "member" });
        var created = await fx.CreateRecurringAsync(memberId, "成员新建", new DateOnly(2026, 9, 1));
        var done = await fx.Items.CompleteAsync(memberId, created.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 2)
        });
        Assert.Equal(new DateOnly(2026, 10, 2), done.Item.NextDueDate);

        var update = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.UpdateAsync(memberId, created.Id, new UpdateHouseholdItemRequest
        {
            Name = "改名",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 1)
        }));
        Assert.Equal(403, update.StatusCode);
        var pause = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.SetPausedAsync(memberId, created.Id, true));
        Assert.Equal(403, pause.StatusCode);
        var delete = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.DeleteAsync(memberId, created.Id));
        Assert.Equal(403, delete.StatusCode);

        var consumable = await fx.Consumables.CreateAsync(memberId, new SaveHouseholdConsumableRequest { Name = "滤芯", CurrentStock = 1 });
        var remove = await Assert.ThrowsAsync<BusinessException>(() => fx.Consumables.DeleteAsync(memberId, consumable.Id));
        Assert.Equal(403, remove.StatusCode);
        Assert.Equal("滤芯", (await fx.Consumables.GetAsync(fx.OwnerId, consumable.Id)).Name);
    }

    [Fact]
    public async Task CrossHousehold_UpdateDeleteCompletePause_Return404()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var otherId = await fx.AddUserAsync("other");
        await fx.Household.GetMineAsync(otherId);
        var item = await fx.CreateRecurringAsync(fx.OwnerId, "年检", new DateOnly(2026, 1, 1), 12);

        var update = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.UpdateAsync(otherId, item.Id, new UpdateHouseholdItemRequest
        {
            Name = "年检",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 12,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 1, 1)
        }));
        var delete = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.DeleteAsync(otherId, item.Id));
        var complete = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CompleteAsync(otherId, item.Id, new CompleteHouseholdItemRequest()));
        var pause = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.SetPausedAsync(otherId, item.Id, true));
        Assert.Equal(404, update.StatusCode);
        Assert.Equal(404, delete.StatusCode);
        Assert.Equal(404, complete.StatusCode);
        Assert.Equal(404, pause.StatusCode);

        var still = await fx.Items.GetAsync(fx.OwnerId, item.Id);
        Assert.False(still.IsPaused);
        Assert.Equal(new DateOnly(2026, 1, 1), still.LastDoneDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CycleValue_ZeroOrNegative_IsRejected(int cycle)
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var ex = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CreateAsync(fx.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "坏周期",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = cycle,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 1)
        }));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task FutureLastDone_AndRenewalNotAfterToday_AreRejected()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var future = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CreateAsync(fx.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "未来",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 10, 2)
        }));
        Assert.Equal(400, future.StatusCode);

        var item = await fx.CreateOneOffAsync("护照", new DateOnly(2026, 9, 1));
        var today = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 20),
            NewExpiryDate = new DateOnly(2026, 10, 1)
        }));
        Assert.Equal(400, today.StatusCode);
        var notAfterCompletion = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 10, 1),
            NewExpiryDate = new DateOnly(2026, 10, 1)
        }));
        Assert.Equal(400, notAfterCompletion.StatusCode);
        Assert.Equal(new DateOnly(2026, 9, 1), (await fx.Items.GetAsync(fx.OwnerId, item.Id)).ExpiryDate);
    }

    [Fact]
    public async Task EarlierCompletion_WritesHistoryWithoutMovingNextDue()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var item = await fx.CreateRecurringAsync(fx.OwnerId, "洗衣机槽清洁", new DateOnly(2026, 8, 1), 1);
        var latest = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 30)
        });
        Assert.Equal(new DateOnly(2026, 10, 30), latest.Item.NextDueDate);

        var backfill = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 7, 15),
            Note = "补记更早"
        });
        Assert.Equal(new DateOnly(2026, 9, 30), backfill.Item.LastDoneDate);
        Assert.Equal(new DateOnly(2026, 10, 30), backfill.Item.NextDueDate);
        var history = await fx.Items.HistoryAsync(fx.OwnerId, item.Id);
        Assert.Equal(
            [new DateOnly(2026, 9, 30), new DateOnly(2026, 7, 15)],
            history.Select(r => r.CompletedOn).ToArray());
    }

    [Fact]
    public async Task Complete_DuplicateWithinWindow_IsRejected_AndIdempotencyKeyDeductsOnce()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var consumable = await fx.Consumables.CreateAsync(fx.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 4
        });
        var item = await fx.CreateRecurringAsync(fx.OwnerId, "净水器 PP 棉", new DateOnly(2026, 4, 1), 6, consumable.Id);
        var request = new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 1),
            ConsumableQuantity = 1
        };

        var first = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, request, "key-1");
        var replay = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, request, "key-1");
        Assert.Equal(first.Record.Id, replay.Record.Id);
        Assert.Equal(3, replay.ConsumableStockAfter);
        Assert.Equal(1, await fx.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == item.Id));

        var duplicate = await Assert.ThrowsAsync<BusinessException>(() =>
            fx.Items.CompleteAsync(fx.OwnerId, item.Id, request));
        Assert.Equal(409, duplicate.StatusCode);
        Assert.Equal(3, (await fx.Consumables.GetAsync(fx.OwnerId, consumable.Id)).CurrentStock);

        var different = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 2),
            SkipConsumableDeduction = true
        });
        Assert.Equal(new DateOnly(2027, 3, 2), different.Item.NextDueDate);
        Assert.Equal(3, (await fx.Consumables.GetAsync(fx.OwnerId, consumable.Id)).CurrentStock);
    }

    [Fact]
    public async Task Complete_UnderRetryingExecutionStrategy_CommitsInsteadOfRejectingUserTransaction()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0), useRetryingExecutionStrategy: true);
        var strategy = fx.Db.Database.CreateExecutionStrategy();
        Assert.True(strategy.RetriesOnFailure);

        // EF 9 的 BeginTransaction 自己走执行策略，不会当场抛。旧写法是先开事务再 SaveChanges，
        // 这时策略发现已有用户事务，抛出与生产 SqlServerRetryingExecutionStrategy 相同的异常。
        InvalidOperationException raw;
        await using (var tx = await fx.Db.Database.BeginTransactionAsync())
        {
            raw = await Assert.ThrowsAsync<InvalidOperationException>(() => fx.Db.SaveChangesAsync());
            await tx.RollbackAsync();
        }

        Assert.Contains("user-initiated transactions", raw.Message, StringComparison.OrdinalIgnoreCase);
        fx.Db.ChangeTracker.Clear();

        var consumable = await fx.Consumables.CreateAsync(fx.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 4
        });
        var item = await fx.CreateRecurringAsync(fx.OwnerId, "净水器 PP 棉", new DateOnly(2026, 4, 1), 6, consumable.Id);
        var done = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 1),
            ConsumableQuantity = 1
        });

        Assert.Equal(new DateOnly(2026, 9, 1), done.Item.LastDoneDate);
        Assert.Equal(3, done.ConsumableStockAfter);
        Assert.Equal(1, await fx.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == item.Id));
        Assert.Equal(0, HouseholdItemService.CompletionGateCount);
    }

    [Fact]
    public async Task Complete_RetriesOnceAfterTransientSaveFailure_WritesOneRecordAndDeductsOnce()
    {
        HouseholdTransientRetryStrategy.RetryCount = 0;
        var interceptor = new OnceTransientSaveFailureInterceptor();
        await using var fx = new HouseholdFixture(
            Utc(2026, 10, 1, 2, 0),
            executionStrategy: dependencies => new HouseholdTransientRetryStrategy(dependencies),
            interceptor: interceptor);

        var consumable = await fx.Consumables.CreateAsync(fx.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 4
        });
        var item = await fx.CreateRecurringAsync(fx.OwnerId, "净水器 PP 棉", new DateOnly(2026, 4, 1), 6, consumable.Id);
        interceptor.Arm();

        var done = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 1),
            ConsumableQuantity = 1
        });

        Assert.Equal(1, interceptor.Thrown);
        Assert.Equal(1, HouseholdTransientRetryStrategy.RetryCount);
        Assert.Equal(new DateOnly(2026, 9, 1), done.Item.LastDoneDate);
        Assert.Equal(3, done.ConsumableStockAfter);
        Assert.Equal(3, (await fx.Consumables.GetAsync(fx.OwnerId, consumable.Id)).CurrentStock);
        Assert.Equal(1, await fx.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == item.Id));
        Assert.Equal(0, HouseholdItemService.CompletionGateCount);
    }

    [Fact]
    public async Task PurchaseLink_IsValidatedOnItemAndConsumableWrites()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));

        var rejectedCreate = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CreateAsync(fx.OwnerId, Recurring("滤网", "JAVASCRIPT:alert(1)")));
        Assert.Equal(400, rejectedCreate.StatusCode);

        var item = await fx.Items.CreateAsync(fx.OwnerId, Recurring("滤网", " https://example.com/filter "));
        Assert.Equal("https://example.com/filter", item.PurchaseLink);

        var rejectedUpdate = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.UpdateAsync(
            fx.OwnerId, item.Id, UpdateOf(item, " javascript:alert(1)")));
        Assert.Equal(400, rejectedUpdate.StatusCode);
        Assert.Equal("https://example.com/filter", (await fx.Items.GetAsync(fx.OwnerId, item.Id)).PurchaseLink);

        var rejectedComplete = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CompleteAsync(
            fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
            {
                CompletedOn = new DateOnly(2026, 9, 2),
                PurchaseLink = "\tjavascript:alert(1)"
            }));
        Assert.Equal(400, rejectedComplete.StatusCode);
        Assert.Equal(0, await fx.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == item.Id));

        var done = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 2),
            PurchaseLink = "http://example.com/order"
        });
        Assert.Equal("http://example.com/order", done.Record.PurchaseLink);

        var rejectedConsumable = await Assert.ThrowsAsync<BusinessException>(() => fx.Consumables.CreateAsync(
            fx.OwnerId, new SaveHouseholdConsumableRequest
            {
                Name = "棉芯",
                CurrentStock = 2,
                PurchaseLink = "data:text/html,<script>alert(1)</script>"
            }));
        Assert.Equal(400, rejectedConsumable.StatusCode);

        var consumable = await fx.Consumables.CreateAsync(fx.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "棉芯",
            CurrentStock = 2,
            PurchaseLink = "https://example.com/cotton"
        });
        var rejectedRestockLink = await Assert.ThrowsAsync<BusinessException>(() => fx.Consumables.UpdateAsync(
            fx.OwnerId, consumable.Id, new SaveHouseholdConsumableRequest
            {
                Name = "棉芯",
                CurrentStock = 2,
                PurchaseLink = "vbscript:msgbox(1)"
            }));
        Assert.Equal(400, rejectedRestockLink.StatusCode);
        Assert.Equal("https://example.com/cotton", (await fx.Consumables.GetAsync(fx.OwnerId, consumable.Id)).PurchaseLink);

        var template = new HouseholdItemTemplate
        {
            Name = "滤网模板",
            Category = HouseholdCategory.HomeMaintenance,
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 3,
            CycleUnit = HouseholdCycleUnit.Month
        };
        fx.Db.HouseholdItemTemplates.Add(template);
        await fx.Db.SaveChangesAsync();
        var rejectedTemplate = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CreateFromTemplateAsync(
            fx.OwnerId, new CreateHouseholdItemFromTemplateRequest
            {
                TemplateId = template.Id,
                LastDoneDate = new DateOnly(2026, 9, 1),
                PurchaseLink = "/foo"
            }));
        Assert.Equal(400, rejectedTemplate.StatusCode);

        var fromTemplate = await fx.Items.CreateFromTemplateAsync(fx.OwnerId, new CreateHouseholdItemFromTemplateRequest
        {
            TemplateId = template.Id,
            LastDoneDate = new DateOnly(2026, 9, 1),
            PurchaseLink = "http://example.com/template"
        });
        Assert.Equal("http://example.com/template", fromTemplate.PurchaseLink);
    }

    private static CreateHouseholdItemRequest Recurring(string name, string? purchaseLink) => new()
    {
        Name = name,
        ItemType = HouseholdItemType.Recurring,
        CycleValue = 1,
        CycleUnit = HouseholdCycleUnit.Month,
        LastDoneDate = new DateOnly(2026, 9, 1),
        PurchaseLink = purchaseLink
    };

    private static UpdateHouseholdItemRequest UpdateOf(HouseholdItemDto item, string? purchaseLink) => new()
    {
        Name = item.Name,
        Category = item.Category,
        ItemType = item.ItemType,
        CycleValue = item.CycleValue,
        CycleUnit = item.CycleUnit,
        LastDoneDate = item.LastDoneDate,
        PurchaseLink = purchaseLink
    };

    [Fact]
    public async Task EarlierCompletion_WithRenewalExpiry_IsRejected()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var item = await fx.CreateOneOffAsync("护照", new DateOnly(2026, 9, 1));
        await fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 20)
        });

        var rejected = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 1),
            NewExpiryDate = new DateOnly(2027, 6, 1)
        }));
        Assert.Equal(400, rejected.StatusCode);
        Assert.Equal(HouseholdItemService.BackfillRenewalMessage, rejected.Message);

        var reloaded = await fx.Items.GetAsync(fx.OwnerId, item.Id);
        Assert.Equal(new DateOnly(2026, 9, 1), reloaded.ExpiryDate);
        Assert.Equal(new DateOnly(2026, 9, 20), reloaded.LastDoneDate);
        Assert.Equal(1, await fx.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == item.Id));
    }

    [Fact]
    public async Task IdempotencyKey_IsScopedToUser_AndRejectsADifferentBody()
    {
        await using var fx = new HouseholdFixture(Utc(2026, 10, 1, 2, 0));
        var memberId = await fx.AddUserAsync("member");
        await fx.Household.AddMemberAsync(fx.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "member" });
        var consumable = await fx.Consumables.CreateAsync(fx.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "滤芯",
            CurrentStock = 5
        });
        var item = await fx.CreateRecurringAsync(fx.OwnerId, "净水器", new DateOnly(2026, 4, 1), 6, consumable.Id);
        var request = new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 1),
            ConsumableQuantity = 1
        };

        var first = await fx.Items.CompleteAsync(fx.OwnerId, item.Id, request, "same-key");
        var mismatch = await Assert.ThrowsAsync<BusinessException>(() => fx.Items.CompleteAsync(fx.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 2),
            ConsumableQuantity = 1
        }, "same-key"));
        Assert.Equal(422, mismatch.StatusCode);
        Assert.Equal(HouseholdItemService.IdempotencyBodyMismatchMessage, mismatch.Message);
        Assert.Equal(4, (await fx.Consumables.GetAsync(fx.OwnerId, consumable.Id)).CurrentStock);
        Assert.Equal(first.Record.Id, (await fx.Items.HistoryAsync(fx.OwnerId, item.Id)).Single(r => r.CompletedOn == new DateOnly(2026, 9, 1)).Id);

        var otherUser = await fx.Items.CompleteAsync(memberId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 3),
            SkipConsumableDeduction = true
        }, "same-key");
        Assert.NotEqual(first.Record.Id, otherUser.Record.Id);
        Assert.Equal(new DateOnly(2027, 3, 3), otherUser.Item.NextDueDate);
        Assert.Equal(0, HouseholdItemService.CompletionGateCount);
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

        public HouseholdFixture(
            DateTimeOffset utcNow,
            bool useRetryingExecutionStrategy = false,
            IInterceptor? interceptor = null,
            Func<ExecutionStrategyDependencies, IExecutionStrategy>? executionStrategy = null)
        {
            _fx = new MiraiTestFixture();
            var builder = new DbContextOptionsBuilder<MiraiNoteDbContext>();
            if (executionStrategy != null)
            {
                builder.UseSqlite(_fx.ConnectionString, sqlite => sqlite.ExecutionStrategy(executionStrategy));
            }
            else if (useRetryingExecutionStrategy)
            {
                builder.UseSqlite(_fx.ConnectionString, sqlite =>
                    sqlite.ExecutionStrategy(dependencies => new HouseholdRetryingExecutionStrategy(dependencies)));
            }
            else
            {
                builder.UseSqlite(_fx.ConnectionString);
            }

            if (interceptor != null)
                builder.AddInterceptors(interceptor);

            Db = new MiraiNoteDbContext(builder.Options);
            OwnerId = Db.Users.Single().Id;
            _rules = new HouseholdCycleRules(new DelegatingHouseholdClock(new FixedTimeProvider(utcNow)));
            var access = new HouseholdAccessService(Db);
            Household = new HouseholdService(Db, access);
            Items = new HouseholdItemService(Db, access, _rules, _policy);
            Consumables = new HouseholdConsumableService(Db, access, _policy);
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

    /// <summary>
    /// 测试替身：<see cref="RetriesOnFailure"/> 为 true，因此直接 <c>BeginTransaction</c> 会抛出
    /// 与生产环境 SqlServerRetryingExecutionStrategy 相同的 InvalidOperationException。
    /// </summary>
    private sealed class HouseholdRetryingExecutionStrategy : ExecutionStrategy
    {
        public HouseholdRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
            : base(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(10))
        {
        }

        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    /// <summary>第一次 SaveChanges 抛一次可重试异常，供执行策略重试后成功。</summary>
    private sealed class OnceTransientSaveFailureInterceptor : SaveChangesInterceptor
    {
        private int _remaining;
        public int Thrown { get; private set; }

        public void Arm() => _remaining = 1;

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            ThrowIfArmed();
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void ThrowIfArmed()
        {
            if (_remaining <= 0)
                return;
            _remaining--;
            Thrown++;
            throw new HouseholdTransientFailureException("injected transient failure");
        }
    }

    private sealed class HouseholdTransientFailureException : Exception
    {
        public HouseholdTransientFailureException(string message) : base(message)
        {
        }
    }

    /// <summary>只重试测试注入的瞬时失败，业务异常仍然立刻抛出。</summary>
    private sealed class HouseholdTransientRetryStrategy : ExecutionStrategy
    {
        public static int RetryCount;

        public HouseholdTransientRetryStrategy(ExecutionStrategyDependencies dependencies)
            : base(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.Zero)
        {
        }

        protected override bool ShouldRetryOn(Exception exception)
        {
            if (exception is not HouseholdTransientFailureException)
                return false;
            RetryCount++;
            return true;
        }
    }
}
