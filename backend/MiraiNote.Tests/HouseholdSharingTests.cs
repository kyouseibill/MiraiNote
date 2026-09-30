using Microsoft.EntityFrameworkCore;
using MiraiNote.Core.Services.Household;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;
using Xunit;

namespace MiraiNote.Tests;

public class HouseholdSharingTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 8, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InvitationFailures_UseTheSameResponse_AndRepeatRefreshesExpiry()
    {
        await using var lab = await SharingLab.CreateAsync(Morning);
        var outsider = await lab.AddUserAsync("outsider");
        await lab.Household.GetMineAsync(outsider);
        var inactive = await lab.AddUserAsync("inactive");
        var inactiveUser = await lab.Db.Users.SingleAsync(u => u.Id == inactive);
        inactiveUser.IsActive = false;
        await lab.Db.SaveChangesAsync();

        var missing = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Invitations.CreateAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "nobody" }));
        var occupied = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Invitations.CreateAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "outsider" }));
        var disabled = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Invitations.CreateAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "inactive" }));
        Assert.All(new[] { missing, occupied, disabled }, error =>
        {
            Assert.Equal(400, error.StatusCode);
            Assert.Equal(HouseholdService.AddMemberRejectedMessage, error.Message);
        });
        Assert.Equal(missing.StatusCode, occupied.StatusCode);
        Assert.Equal(missing.Message, occupied.Message);
        Assert.Equal(missing.Message, disabled.Message);

        var fresh = await lab.AddUserAsync("fresh");
        var first = await lab.Invitations.CreateAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "FRESH" });
        lab.Clock.UtcNow = lab.Clock.UtcNow.AddDays(1);
        var second = await lab.Invitations.CreateAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "fresh@example.com" });
        Assert.Equal(first.Id, second.Id);
        Assert.True(second.ExpiresAt > first.ExpiresAt);
        Assert.Equal(1, await lab.Db.HouseholdInvitations.CountAsync(i =>
            i.InviteeUserId == fresh && i.Status == HouseholdInvitationStatus.Pending));
        Assert.Equal(lab.Clock.UtcNow.Add(HouseholdInvitationService.Lifetime), second.ExpiresAt);

        var duplicate = new HouseholdInvitation
        {
            HouseholdId = first.HouseholdId,
            InviterUserId = lab.OwnerId,
            InviteeUserId = fresh,
            Role = HouseholdRole.Member,
            Status = HouseholdInvitationStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        lab.Db.HouseholdInvitations.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => lab.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Accept_Revalidates_IsIdempotent_AndHidesForeignIds()
    {
        await using var lab = await SharingLab.CreateAsync(Morning);
        var invitee = await lab.AddUserAsync("invitee");
        var stranger = await lab.AddUserAsync("stranger");
        var created = await lab.Invitations.CreateAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "invitee" });

        var foreignAccept = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Invitations.AcceptAsync(stranger, created.Id, "foreign"));
        var foreignReject = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Invitations.RejectAsync(stranger, created.Id));
        var memberId = await lab.AddUserAsync("housemate");
        await lab.Household.AddMemberAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "housemate" });
        var memberRevoke = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Invitations.RevokeAsync(memberId, created.Id));
        var otherAdmin = await lab.AddUserAsync("other-admin");
        await lab.Household.GetMineAsync(otherAdmin);
        var otherRevoke = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Invitations.RevokeAsync(otherAdmin, created.Id));
        Assert.All(new[] { foreignAccept, foreignReject, memberRevoke, otherRevoke }, error =>
        {
            Assert.Equal(404, error.StatusCode);
            Assert.Equal(HouseholdInvitationService.NotFoundMessage, error.Message);
        });

        var joined = await lab.Invitations.AcceptAsync(invitee, created.Id, "once");
        var replay = await lab.Invitations.AcceptAsync(invitee, created.Id, "again");
        Assert.Equal(joined.Id, replay.Id);
        Assert.Equal(1, await lab.Db.HouseholdMembers.CountAsync(m => m.UserId == invitee));
        Assert.Equal(HouseholdInvitationStatus.Accepted, await lab.Db.HouseholdInvitations.Where(i => i.Id == created.Id).Select(i => i.Status).SingleAsync());

        var pendingUser = await lab.AddUserAsync("pending-one");
        var blockedInvite = await lab.Invitations.CreateAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "pending-one" });
        await lab.Household.GetMineAsync(pendingUser);
        var blocked = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Invitations.AcceptAsync(pendingUser, blockedInvite.Id, null));
        Assert.Equal(400, blocked.StatusCode);
        Assert.Equal(HouseholdInvitationService.AlreadyElsewhereMessage, blocked.Message);

        var expiringUser = await lab.AddUserAsync("expiring");
        var expiring = await lab.Invitations.CreateAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "expiring" });
        lab.Clock.UtcNow = lab.Clock.UtcNow.Add(HouseholdInvitationService.Lifetime).AddSeconds(1);
        var expired = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Invitations.AcceptAsync(expiringUser, expiring.Id, null));
        Assert.Equal(400, expired.StatusCode);
        Assert.Equal(HouseholdInvitationService.ExpiredMessage, expired.Message);
        Assert.Equal(0, await lab.Db.HouseholdMembers.CountAsync(m => m.UserId == expiringUser));
        Assert.Empty(await lab.Invitations.ListIncomingAsync(expiringUser));

        var revokeUser = await lab.AddUserAsync("revokee");
        var revocable = await lab.Invitations.CreateAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "revokee" });
        await lab.Invitations.RevokeAsync(lab.OwnerId, revocable.Id);
        var revoked = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Invitations.AcceptAsync(revokeUser, revocable.Id, null));
        Assert.Equal(400, revoked.StatusCode);
        Assert.Equal(HouseholdInvitationService.HandledMessage, revoked.Message);
        Assert.Equal(0, await lab.Db.HouseholdMembers.CountAsync(m => m.UserId == revokeUser));

        var rejectUser = await lab.AddUserAsync("rejector");
        var rejection = await lab.Invitations.CreateAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "rejector" });
        var rejected = await lab.Invitations.RejectAsync(rejectUser, rejection.Id);
        Assert.Equal(HouseholdInvitationStatus.Rejected, rejected.Status);
        var rejectedAgain = await lab.Invitations.RejectAsync(rejectUser, rejection.Id);
        Assert.Equal(rejected.Id, rejectedAgain.Id);
        Assert.Equal(0, await lab.Db.HouseholdMembers.CountAsync(m => m.UserId == rejectUser));
    }

    [Fact]
    public async Task LeaveAndRemove_ClearDutyHistoryStays_AndLastAdminIsProtected()
    {
        await using var lab = await SharingLab.CreateAsync(Morning);
        var home = await lab.Household.GetMineAsync(lab.OwnerId);
        var helperId = await lab.AddUserAsync("helper");
        await lab.Household.AddMemberAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "helper" });
        var helper = (await lab.Household.ListMembersAsync(lab.OwnerId)).Single(m => m.UserId == helperId);
        var item = await lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "纱窗",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 1),
            AssigneeMemberId = helper.Id
        });
        await lab.Items.CompleteAsync(lab.OwnerId, item.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 20)
        });
        var history = await lab.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == item.Id);
        lab.Db.HouseholdNotificationSettings.Add(new HouseholdNotificationSetting
        {
            MemberId = helper.Id,
            EmailEnabled = true
        });
        await lab.Db.SaveChangesAsync();
        var draft = await lab.Chat.InterpretAsync(helperId, "今天换了纱窗");
        Assert.NotNull(draft.DraftId);

        var stuck = await Assert.ThrowsAsync<BusinessException>(() => lab.Household.LeaveAsync(lab.OwnerId));
        Assert.Equal(400, stuck.StatusCode);
        Assert.Equal(HouseholdService.LastAdminMessage, stuck.Message);
        var stuckRemove = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Household.RemoveMemberAsync(helperId, home.MyMemberId));
        Assert.Equal(403, stuckRemove.StatusCode);

        await lab.Household.LeaveAsync(helperId);
        var rawItem = await lab.Db.HouseholdItems.SingleAsync(i => i.Id == item.Id);
        Assert.Null(rawItem.AssigneeMemberId);
        Assert.Equal(history, await lab.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == item.Id));
        Assert.True(await lab.Db.HouseholdNotificationSettings.IgnoreQueryFilters()
            .AnyAsync(s => s.MemberId == helper.Id && s.IsDeleted));
        Assert.Empty(await lab.Db.HouseholdNotificationSettings.Where(s => s.MemberId == helper.Id).ToListAsync());
        Assert.DoesNotContain(helper.Id, await lab.Db.HouseholdMembers.Where(m => m.HouseholdId == home.Id).Select(m => m.Id).ToListAsync());
        Assert.True(await lab.Db.HouseholdChatDrafts.IgnoreQueryFilters().AnyAsync(d => d.Id == draft.DraftId && d.IsDeleted));
        var gone = await Assert.ThrowsAsync<BusinessException>(() => lab.Chat.ConfirmAsync(
            helperId,
            new ConfirmHouseholdChatRequest { DraftId = draft.DraftId!.Value, ItemId = item.Id },
            "after-leave"));
        Assert.Equal(404, gone.StatusCode);
        Assert.Equal(history, await lab.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == item.Id));

        var restarted = await lab.Household.GetMineAsync(helperId);
        Assert.NotEqual(home.Id, restarted.Id);

        var secondId = await lab.AddUserAsync("second-admin");
        await lab.Household.AddMemberAsync(lab.OwnerId, new AddHouseholdMemberRequest
        {
            UserIdentifier = "second-admin",
            Role = HouseholdRole.Admin
        });
        var ownerMember = (await lab.Household.ListMembersAsync(lab.OwnerId)).Single(m => m.UserId == lab.OwnerId);
        await lab.Household.RemoveMemberAsync(secondId, ownerMember.Id);
        var ownerRestarted = await lab.Household.GetMineAsync(lab.OwnerId);
        Assert.NotEqual(home.Id, ownerRestarted.Id);
        var last = await Assert.ThrowsAsync<BusinessException>(() => lab.Household.LeaveAsync(secondId));
        Assert.Equal(400, last.StatusCode);
        Assert.Equal(HouseholdService.LastAdminMessage, last.Message);
        var stillThere = await lab.Household.GetMineAsync(secondId);
        Assert.Equal(home.Id, stillThere.Id);
    }

    [Fact]
    public async Task Member_CannotEditPauseDeleteOrRemoveOthers_AndForeignItemIs404()
    {
        await using var lab = await SharingLab.CreateAsync(Morning);
        var memberId = await lab.AddUserAsync("member");
        await lab.Household.AddMemberAsync(lab.OwnerId, new AddHouseholdMemberRequest { UserIdentifier = "member" });
        var item = await lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "滤网",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 1)
        });
        var update = new UpdateHouseholdItemRequest
        {
            Name = "改名",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 1,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 9, 1)
        };
        var edit = await Assert.ThrowsAsync<BusinessException>(() => lab.Items.UpdateAsync(memberId, item.Id, update));
        var pause = await Assert.ThrowsAsync<BusinessException>(() => lab.Items.SetPausedAsync(memberId, item.Id, true));
        var delete = await Assert.ThrowsAsync<BusinessException>(() => lab.Items.DeleteAsync(memberId, item.Id));
        var owner = await lab.Household.GetMineAsync(lab.OwnerId);
        var remove = await Assert.ThrowsAsync<BusinessException>(() => lab.Household.RemoveMemberAsync(memberId, owner.MyMemberId));
        Assert.All(new[] { edit, pause, delete, remove }, error => Assert.Equal(403, error.StatusCode));

        var otherId = await lab.AddUserAsync("other");
        await lab.Household.GetMineAsync(otherId);
        var hidden = await Assert.ThrowsAsync<BusinessException>(() => lab.Items.GetAsync(otherId, item.Id));
        Assert.Equal(404, hidden.StatusCode);
    }

    [Fact]
    public async Task Consumable_ShowsLinks_RestockClearsReminder_AndRejectsBadLink()
    {
        await using var lab = await SharingLab.CreateAsync(Morning);
        var consumable = await lab.Consumables.CreateAsync(lab.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 1,
            PurchaseLink = "https://example.com/filter"
        });
        Assert.Equal("https://example.com/filter", consumable.PurchaseLink);
        await lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "厨房净水器",
            ItemType = HouseholdItemType.Recurring,
            CycleValue = 6,
            CycleUnit = HouseholdCycleUnit.Month,
            LastDoneDate = new DateOnly(2026, 4, 1),
            ConsumableId = consumable.Id
        });
        var archived = await lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "旧机器",
            ItemType = HouseholdItemType.OneOffExpiry,
            ExpiryDate = new DateOnly(2026, 8, 1),
            ConsumableId = consumable.Id
        });
        await lab.Items.CompleteAsync(lab.OwnerId, archived.Id, new CompleteHouseholdItemRequest());
        var listed = await lab.Consumables.ListAsync(lab.OwnerId);
        var links = Assert.Single(listed, item => item.Id == consumable.Id).LinkedItems;
        Assert.Contains(links, item => item.Name == "厨房净水器" && !item.IsArchived);
        Assert.Contains(links, item => item.Name == "旧机器" && item.IsArchived);

        var mine = await lab.Household.GetMineAsync(lab.OwnerId);
        lab.Db.HouseholdConsumableReminders.Add(new HouseholdConsumableReminder
        {
            ConsumableId = consumable.Id,
            MemberId = mine.MyMemberId,
            Channel = HouseholdNotificationChannel.Email,
            Status = HouseholdReminderDeliveryStatus.Sent
        });
        var row = await lab.Db.HouseholdConsumables.SingleAsync(c => c.Id == consumable.Id);
        row.LowStockReminderSent = true;
        await lab.Db.SaveChangesAsync();
        var restocked = await lab.Consumables.RestockAsync(lab.OwnerId, consumable.Id, new RestockHouseholdConsumableRequest { Quantity = 2 });
        Assert.False(restocked.LowStockReminderSent);
        Assert.Equal(2, restocked.LinkedItems.Count);
        Assert.Empty(await lab.Db.HouseholdConsumableReminders.Where(r => r.ConsumableId == consumable.Id).ToListAsync());
        Assert.True(await lab.Db.HouseholdConsumableReminders.IgnoreQueryFilters()
            .AnyAsync(r => r.ConsumableId == consumable.Id && r.IsDeleted));

        var bad = await Assert.ThrowsAsync<BusinessException>(() => lab.Consumables.CreateAsync(lab.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "坏链接",
            CurrentStock = 1,
            PurchaseLink = "http:evil.com"
        }));
        var expected = Assert.Throws<BusinessException>(() => HouseholdText.CleanPurchaseLink("http:evil.com"));
        Assert.Equal(400, bad.StatusCode);
        Assert.Equal(expected.StatusCode, bad.StatusCode);
        Assert.Equal(expected.Message, bad.Message);
    }

    private sealed class SharingLab : IAsyncDisposable
    {
        private readonly MiraiTestFixture _fx;
        public MiraiNoteDbContext Db { get; }
        public MutableClock Clock { get; }
        public HouseholdService Household { get; }
        public HouseholdInvitationService Invitations { get; }
        public HouseholdItemService Items { get; }
        public HouseholdConsumableService Consumables { get; }
        public HouseholdChatService Chat { get; }
        public int OwnerId { get; }

        private SharingLab(
            MiraiTestFixture fx,
            MiraiNoteDbContext db,
            MutableClock clock,
            HouseholdService household,
            HouseholdInvitationService invitations,
            HouseholdItemService items,
            HouseholdConsumableService consumables,
            HouseholdChatService chat,
            int ownerId)
        {
            _fx = fx;
            Db = db;
            Clock = clock;
            Household = household;
            Invitations = invitations;
            Items = items;
            Consumables = consumables;
            Chat = chat;
            OwnerId = ownerId;
        }

        public static Task<SharingLab> CreateAsync(DateTimeOffset utcNow)
        {
            var fx = new MiraiTestFixture();
            var db = fx.CreateContext();
            var clock = new MutableClock(utcNow);
            var rules = new HouseholdCycleRules(clock);
            var access = new HouseholdAccessService(db);
            var policy = HouseholdAccessPolicy.Default;
            var items = new HouseholdItemService(db, access, rules, policy);
            var consumables = new HouseholdConsumableService(db, access, policy);
            return Task.FromResult(new SharingLab(
                fx,
                db,
                clock,
                new HouseholdService(db, access),
                new HouseholdInvitationService(db, access, rules),
                items,
                consumables,
                new HouseholdChatService(db, access, items, rules),
                db.Users.Single().Id));
        }

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

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _fx.Dispose();
        }
    }

    private sealed class MutableClock : TimeProvider, IHouseholdClock
    {
        public MutableClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
