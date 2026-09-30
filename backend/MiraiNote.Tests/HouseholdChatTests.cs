using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiraiNote.Core.Services.Household;
using MiraiNote.Core.Services.Tools;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;
using Xunit;

namespace MiraiNote.Tests;

public class HouseholdChatTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 8, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RelativeDates_UseShanghaiClock()
    {
        var today = new DateOnly(2026, 10, 8);
        var saturday = HouseholdChatPhrase.Parse("上周六给车做了保养，花了 680", today);
        Assert.Equal(HouseholdChatIntent.Record, saturday.Intent);
        Assert.Equal(new DateOnly(2026, 10, 3), saturday.CompletedOn);
        Assert.Equal(680m, saturday.Cost);
        Assert.Equal("车保养", saturday.NameHint);

        var yesterday = HouseholdChatPhrase.Parse("昨天换了厨房净水器 PP 棉", today);
        Assert.Equal(new DateOnly(2026, 10, 7), yesterday.CompletedOn);
        Assert.Equal("厨房净水器 PP 棉", yesterday.NameHint);

        var todayPhrase = HouseholdChatPhrase.Parse("今天换了厨房净水器 PP 棉", today);
        Assert.Equal(today, todayPhrase.CompletedOn);
        Assert.False(todayPhrase.FutureDate);
    }

    [Fact]
    public void RecordName_StripsLeadingTimeAspectAdverbs_ButKeepsThemInsideTheName()
    {
        var today = new DateOnly(2026, 10, 8);
        Assert.Equal("净水器滤芯", HouseholdChatPhrase.Parse("刚换了净水器滤芯", today).NameHint);
        Assert.Equal("滤网", HouseholdChatPhrase.Parse("刚刚换了滤网", today).NameHint);
        Assert.Equal("PP棉", HouseholdChatPhrase.Parse("已经换了PP棉", today).NameHint);
        Assert.Equal("滤网", HouseholdChatPhrase.Parse("昨天又换了滤网", today).NameHint);

        var spent = HouseholdChatPhrase.Parse("刚换了净水器滤芯，花了128元", today);
        Assert.Equal("净水器滤芯", spent.NameHint);
        Assert.Equal(128m, spent.Cost);
        Assert.Equal("净水器刚滤芯", HouseholdChatPhrase.Parse("换了净水器刚滤芯", today).NameHint);
    }

    [Fact]
    public void MonthDay_UsesThisYear_UntilItWouldBeAfterToday()
    {
        var newYear = new DateOnly(2026, 1, 3);
        var lastDecember = HouseholdChatPhrase.Parse("12月28日换了滤网", newYear);
        Assert.False(lastDecember.InvalidInput);
        Assert.False(lastDecember.FutureDate);
        Assert.Equal(new DateOnly(2025, 12, 28), lastDecember.CompletedOn);

        var october = new DateOnly(2026, 10, 8);
        var earlierThisYear = HouseholdChatPhrase.Parse("10月1日换了滤网", october);
        Assert.Equal(new DateOnly(2026, 10, 1), earlierThisYear.CompletedOn);

        var laterThisYear = HouseholdChatPhrase.Parse("12月28日换了滤网", october);
        Assert.Equal(new DateOnly(2025, 12, 28), laterThisYear.CompletedOn);

        var boundary = HouseholdChatPhrase.Parse("1月1日换了滤网", new DateOnly(2026, 1, 1));
        Assert.Equal(new DateOnly(2026, 1, 1), boundary.CompletedOn);

        var nextDay = HouseholdChatPhrase.Parse("1月2日换了滤网", new DateOnly(2026, 1, 1));
        Assert.Equal(new DateOnly(2025, 1, 2), nextDay.CompletedOn);
    }

    [Fact]
    public void InvalidDateOrHugeCost_DoesNotThrow_AndAsksToRephrase()
    {
        var today = new DateOnly(2026, 10, 8);
        foreach (var text in new[]
        {
            "2月30日换了滤网",
            "13月1日换了滤网",
            "2026年2月30日换了滤网",
            "2026-02-30换了滤网",
            "今天换了滤网，花了 1000000000",
            "今天换了滤网，花了 999999999999999999999"
        })
        {
            var parsed = HouseholdChatPhrase.Parse(text, today);
            Assert.True(parsed.InvalidInput);
            Assert.NotEqual(HouseholdChatIntent.Record, parsed.Intent);
        }
    }

    [Fact]
    public void Amounts_KeepThousandsSeparatorsAndFullWidthDigits()
    {
        var today = new DateOnly(2026, 10, 8);

        Assert.Equal(1000m, HouseholdChatPhrase.Parse("今天换了滤网，花了1,000元", today).Cost);
        Assert.Equal(123m, HouseholdChatPhrase.Parse("今天换了滤网，花了１２３元", today).Cost);
        Assert.Equal(1000m, HouseholdChatPhrase.Parse("今天换了滤网，花了1，000元", today).Cost);
        Assert.Equal(1234567m, HouseholdChatPhrase.Parse("今天换了滤网，花了1,234,567元", today).Cost);
        Assert.Equal(12345.60m, HouseholdChatPhrase.Parse("今天换了滤网，花了12,345.60元", today).Cost);
        Assert.Equal(HouseholdCost.MaxAmount, HouseholdChatPhrase.Parse("今天换了滤网，花了999999999.99元", today).Cost);

        var split = HouseholdChatPhrase.Parse("花了12，3个人分", today);
        Assert.Equal(12m, split.Cost);
        Assert.NotEqual(123m, split.Cost);
        Assert.False(split.InvalidInput);

        var over = HouseholdChatPhrase.Parse("花了1000000000元", today);
        Assert.True(over.InvalidInput);
        Assert.Null(over.Cost);
    }

    [Fact]
    public async Task Interpret_MatchesUniqueMultipleOrNone_WithoutWriting()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        await lab.CreateAsync("厨房净水器 PP 棉");
        await lab.CreateAsync("客厅净水器 PP 棉");
        await lab.CreateAsync("护照", aliases: ["证件"]);

        var unique = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了护照");
        Assert.Equal("confirm", unique.Kind);
        Assert.Equal("护照", unique.Item!.Name);
        Assert.NotNull(unique.DraftId);
        Assert.NotNull(unique.ExpiresAt);

        var many = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了净水器 PP 棉");
        Assert.Equal("choose", many.Kind);
        Assert.Equal(2, many.Candidates.Count);
        Assert.Contains(many.Candidates, item => item.Name == "厨房净水器 PP 棉");
        Assert.Contains(many.Candidates, item => item.Name == "客厅净水器 PP 棉");

        var none = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了阳台纱窗");
        Assert.Equal("create", none.Kind);
        Assert.Equal("阳台纱窗", none.SuggestedName);
        Assert.Null(none.DraftId);
        Assert.Equal(0, await lab.Db.HouseholdCompletionRecords.CountAsync());

        var tool = new ServerHouseholdChatTool(lab.Chat);
        var json = await tool.ExecuteAsync(lab.OwnerId, """{"utterance":"今天换了护照"}""", CancellationToken.None);
        Assert.Contains("\"kind\":\"confirm\"", json);
        Assert.Contains("\"draftId\":", json);
        var modelJson = ServerHouseholdChatTool.ForModel(json);
        Assert.DoesNotContain("draftId", modelJson);
        Assert.DoesNotContain("expiresAt", modelJson);
        Assert.Contains("\"confirmBy\":\"2026-10-08 09:10\"", modelJson);
        Assert.Equal(0, await lab.Db.HouseholdCompletionRecords.CountAsync());
    }

    [Fact]
    public async Task Interpret_GenericOverlapIsNotAMatch_CapsCandidates_AndRejectsRetiredNames()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        await lab.CreateAsync("净水器PP棉", location: "厨房");
        await lab.CreateAsync("空调滤网");

        var fish = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了厨房鱼缸过滤棉");
        Assert.Equal("create", fish.Kind);
        Assert.Equal("厨房鱼缸过滤棉", fish.SuggestedName);
        Assert.Null(fish.DraftId);
        Assert.Empty(fish.Candidates);

        var cloth = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了厨房抹布");
        Assert.Equal("create", cloth.Kind);
        Assert.Equal("厨房抹布", cloth.SuggestedName);
        Assert.Null(cloth.DraftId);
        Assert.DoesNotContain(cloth.Candidates, item => item.Name is "净水器PP棉" or "空调滤网");

        var tank = await lab.CreateAsync("鱼缸过滤棉");
        var genuine = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了鱼缸过滤棉");
        Assert.Equal("confirm", genuine.Kind);
        Assert.Equal(tank.Id, genuine.Item!.Id);
        Assert.Single(genuine.Candidates);

        var names = new List<string>();
        for (var i = 1; i <= 6; i++)
        {
            var item = await lab.CreateAsync($"季度巡检{i}");
            names.Add(item.Name);
        }
        var capped = await lab.Chat.InterpretAsync(lab.OwnerId, "今天做了季度巡检");
        Assert.Equal("choose", capped.Kind);
        Assert.Equal(HouseholdChatService.MaxCandidates, capped.Candidates.Count);
        Assert.Null(capped.Item);
        Assert.False(capped.DeductConsumable);
        Assert.Equal(names.Take(HouseholdChatService.MaxCandidates), capped.Candidates.Select(item => item.Name));

        var row = await lab.Db.HouseholdItems.SingleAsync(i => i.Id == tank.Id);
        row.IsArchived = true;
        await lab.Db.SaveChangesAsync();
        var retired = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了鱼缸过滤棉");
        Assert.Equal("rejected", retired.Kind);
        Assert.Equal(HouseholdChatService.RetiredMessage, retired.Message);
        Assert.Null(retired.DraftId);
        Assert.Null(retired.Item);
        Assert.DoesNotContain(retired.Candidates, item => item.Name == "净水器PP棉");
    }

    [Fact]
    public async Task Interpret_ExactRetiredNameBeatsSharedFragments_AndExactActiveNameWins()
    {
        await using var archivedFirst = await ChatLab.CreateAsync(Morning);
        var retiredNet = await archivedFirst.CreateAsync("卧室空调滤网");
        await archivedFirst.CreateAsync("卧室空调清洗");
        var archivedRow = await archivedFirst.Db.HouseholdItems.SingleAsync(i => i.Id == retiredNet.Id);
        archivedRow.IsArchived = true;
        await archivedFirst.Db.SaveChangesAsync();

        var hidden = await archivedFirst.Chat.InterpretAsync(archivedFirst.OwnerId, "今天换了卧室 空调滤网");
        Assert.Equal("rejected", hidden.Kind);
        Assert.Equal(HouseholdChatService.RetiredMessage, hidden.Message);
        Assert.Null(hidden.DraftId);
        Assert.Empty(hidden.Candidates);

        var deletedNet = await archivedFirst.CreateAsync("卧室空调滤网");
        await archivedFirst.Items.DeleteAsync(archivedFirst.OwnerId, deletedNet.Id);
        var deleted = await archivedFirst.Chat.InterpretAsync(archivedFirst.OwnerId, "今天换了卧室空调滤网");
        Assert.Equal("rejected", deleted.Kind);
        Assert.Equal(HouseholdChatService.RetiredMessage, deleted.Message);
        Assert.Null(deleted.DraftId);

        await using var activeFirst = await ChatLab.CreateAsync(Morning);
        var live = await activeFirst.CreateAsync("卧室空调滤网");
        var oldClean = await activeFirst.CreateAsync("卧室空调清洗");
        var oldRow = await activeFirst.Db.HouseholdItems.SingleAsync(i => i.Id == oldClean.Id);
        oldRow.IsArchived = true;
        await activeFirst.Db.SaveChangesAsync();

        var shown = await activeFirst.Chat.InterpretAsync(activeFirst.OwnerId, "今天换了卧室空调滤网");
        Assert.Equal("confirm", shown.Kind);
        Assert.Equal(live.Id, shown.Item!.Id);
        Assert.DoesNotContain(shown.Candidates, item => item.Id == oldClean.Id);

        var twin = await activeFirst.CreateAsync("卧室空调滤网");
        var twinRow = await activeFirst.Db.HouseholdItems.SingleAsync(i => i.Id == twin.Id);
        twinRow.IsArchived = true;
        await activeFirst.Db.SaveChangesAsync();
        var stillLive = await activeFirst.Chat.InterpretAsync(activeFirst.OwnerId, "今天换了卧室空调滤网");
        Assert.Equal("confirm", stillLive.Kind);
        Assert.Equal(live.Id, stillLive.Item!.Id);
        Assert.DoesNotContain(stillLive.Candidates, item => item.Id == twin.Id);
    }

    [Fact]
    public async Task Confirm_WritesOnce_AndRejectsFutureExpiredForeignAndRepeat()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        var item = await lab.CreateAsync("厨房净水器 PP 棉");
        var otherId = await lab.AddUserAsync("other");
        var foreign = await lab.CreateAsync("别人的滤网", userId: otherId);

        var draft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了厨房净水器 PP 棉，花了 12");
        Assert.Equal("confirm", draft.Kind);

        var future = await Assert.ThrowsAsync<BusinessException>(() => lab.Chat.ConfirmAsync(
            lab.OwnerId,
            new ConfirmHouseholdChatRequest
            {
                DraftId = draft.DraftId!.Value,
                ItemId = item.Id,
                CompletedOn = new DateOnly(2026, 10, 9)
            },
            "future-key"));
        Assert.Equal(400, future.StatusCode);
        Assert.Equal("完成日期不能晚于今天", future.Message);
        Assert.Equal(0, await lab.Db.HouseholdCompletionRecords.CountAsync());

        var tomorrow = await lab.Chat.InterpretAsync(lab.OwnerId, "明天换了厨房净水器 PP 棉");
        Assert.Equal("rejected", tomorrow.Kind);
        Assert.Null(tomorrow.DraftId);

        var cross = await Assert.ThrowsAsync<BusinessException>(() => lab.Chat.ConfirmAsync(
            lab.OwnerId,
            new ConfirmHouseholdChatRequest { DraftId = draft.DraftId!.Value, ItemId = foreign.Id },
            "cross-key"));
        Assert.Equal(404, cross.StatusCode);
        Assert.Equal(0, await lab.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == foreign.Id));

        var missing = await Assert.ThrowsAsync<BusinessException>(() => lab.Chat.ConfirmAsync(
            lab.OwnerId,
            new ConfirmHouseholdChatRequest { DraftId = 99999, ItemId = foreign.Id },
            "missing-draft"));
        Assert.Equal(404, missing.StatusCode);

        var first = await lab.Chat.ConfirmAsync(
            lab.OwnerId,
            new ConfirmHouseholdChatRequest { DraftId = draft.DraftId!.Value, ItemId = item.Id },
            "once");
        var second = await lab.Chat.ConfirmAsync(
            lab.OwnerId,
            new ConfirmHouseholdChatRequest { DraftId = draft.DraftId!.Value, ItemId = item.Id },
            "again");
        Assert.Equal(first.Record.Id, second.Record.Id);
        Assert.Equal(12m, first.Record.Cost);
        Assert.Equal(1, await lab.Db.HouseholdCompletionRecords.CountAsync());

        var later = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了厨房净水器 PP 棉");
        lab.Clock.UtcNow = lab.Clock.UtcNow.Add(HouseholdChatService.DraftLifetime).AddSeconds(1);
        var expired = await Assert.ThrowsAsync<BusinessException>(() => lab.Chat.ConfirmAsync(
            lab.OwnerId,
            new ConfirmHouseholdChatRequest { DraftId = later.DraftId!.Value, ItemId = item.Id },
            "expired"));
        Assert.Equal(400, expired.StatusCode);
        Assert.Equal(HouseholdChatService.ExpiredMessage, expired.Message);
        Assert.Equal(1, await lab.Db.HouseholdCompletionRecords.CountAsync());
    }

    [Fact]
    public async Task Confirm_ReusesCompletionRules_ForPauseSameDayBackfillArchiveDeleteAndStock()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        var paused = await lab.CreateAsync("暂停滤网", lastDone: new DateOnly(2026, 9, 8));
        await lab.Items.SetPausedAsync(lab.OwnerId, paused.Id, true);
        var pausedDraft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了暂停滤网");
        var pausedResult = await lab.Chat.ConfirmAsync(lab.OwnerId, Confirm(pausedDraft, paused.Id), "paused");
        Assert.True(pausedResult.Item.IsPaused);
        Assert.Equal(new DateOnly(2026, 11, 8), pausedResult.Item.NextDueDate);

        var same = await lab.CreateAsync("当天滤网", lastDone: new DateOnly(2026, 10, 8));
        var sameDue = same.NextDueDate;
        var sameDraft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了当天滤网");
        var sameResult = await lab.Chat.ConfirmAsync(lab.OwnerId, Confirm(sameDraft, same.Id), "same-day");
        Assert.Equal(sameDue, sameResult.Item.NextDueDate);
        Assert.Equal(1, await lab.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == same.Id));

        var earlier = await lab.CreateAsync("补记滤网", lastDone: new DateOnly(2026, 10, 5));
        var earlierDue = earlier.NextDueDate;
        var earlierDraft = await lab.Chat.InterpretAsync(lab.OwnerId, "10月1日换了补记滤网");
        Assert.Equal(new DateOnly(2026, 10, 1), earlierDraft.CompletedOn);
        var earlierResult = await lab.Chat.ConfirmAsync(lab.OwnerId, Confirm(earlierDraft, earlier.Id), "backfill");
        Assert.Equal(earlierDue, earlierResult.Item.NextDueDate);
        Assert.Equal(new DateOnly(2026, 10, 1), earlierResult.Record.CompletedOn);

        var archived = await lab.Items.CreateAsync(lab.OwnerId, new CreateHouseholdItemRequest
        {
            Name = "护照",
            ItemType = HouseholdItemType.OneOffExpiry,
            ExpiryDate = new DateOnly(2027, 1, 1)
        });
        var archivedDraft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了护照");
        await lab.Items.CompleteAsync(lab.OwnerId, archived.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 10, 8)
        }, "manual-archive");
        var denied = await Assert.ThrowsAsync<BusinessException>(() => lab.Chat.ConfirmAsync(
            lab.OwnerId, Confirm(archivedDraft, archived.Id), "archived"));
        Assert.Equal(400, denied.StatusCode);
        Assert.Equal(HouseholdItemService.ArchivedReadOnlyMessage, denied.Message);
        Assert.Equal(1, await lab.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == archived.Id));
        var hidden = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了护照");
        Assert.Equal("rejected", hidden.Kind);
        Assert.Equal(HouseholdChatService.RetiredMessage, hidden.Message);
        Assert.Null(hidden.DraftId);

        var deleted = await lab.CreateAsync("要删的纱窗");
        var deletedDraft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了要删的纱窗");
        await lab.Items.DeleteAsync(lab.OwnerId, deleted.Id);
        var gone = await Assert.ThrowsAsync<BusinessException>(() => lab.Chat.ConfirmAsync(
            lab.OwnerId, Confirm(deletedDraft, deleted.Id), "deleted"));
        Assert.Equal(404, gone.StatusCode);
        Assert.Equal(0, await lab.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == deleted.Id));

        var consumable = await lab.Consumables.CreateAsync(lab.OwnerId, new SaveHouseholdConsumableRequest
        {
            Name = "PP 棉",
            CurrentStock = 3
        });
        var linked = await lab.CreateAsync("扣库存滤网", consumableId: consumable.Id);
        var linkedDraft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了扣库存滤网");
        Assert.True(linkedDraft.DeductConsumable);
        Assert.Equal(consumable.Id, linkedDraft.Item!.ConsumableId);
        var deducted = await lab.Chat.ConfirmAsync(lab.OwnerId, Confirm(linkedDraft, linked.Id), "deduct");
        Assert.Equal(1, deducted.ConsumableQuantityDeducted);
        Assert.Equal(2, await lab.Db.HouseholdConsumables.Where(c => c.Id == consumable.Id).Select(c => c.CurrentStock).SingleAsync());

        var skippedItem = await lab.CreateAsync("不扣库存滤网", consumableId: consumable.Id);
        var skipDraft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了不扣库存滤网");
        var skipped = await lab.Chat.ConfirmAsync(lab.OwnerId, Confirm(skipDraft, skippedItem.Id, deduct: false), "skip");
        Assert.Equal(0, skipped.ConsumableQuantityDeducted);
        Assert.Equal(2, await lab.Db.HouseholdConsumables.Where(c => c.Id == consumable.Id).Select(c => c.CurrentStock).SingleAsync());
    }

    [Fact]
    public async Task Query_StaysInsideTheHousehold_AndUnrecognizedDoesNotWrite()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        var mine = await lab.CreateAsync("净水器滤芯", lastDone: new DateOnly(2026, 9, 8));
        await lab.Items.CompleteAsync(lab.OwnerId, mine.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 20),
            Cost = 30
        });
        var otherId = await lab.AddUserAsync("neighbor");
        var theirs = await lab.CreateAsync("净水器滤芯", userId: otherId, lastDone: new DateOnly(2026, 8, 1));
        await lab.Items.CompleteAsync(otherId, theirs.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 8, 2)
        });

        var history = await lab.Chat.InterpretAsync(lab.OwnerId, "净水器滤芯什么时候换的？");
        Assert.Equal("query", history.Kind);
        Assert.Single(history.History);
        Assert.Equal(new DateOnly(2026, 9, 20), history.History[0].CompletedOn);
        Assert.Equal(mine.Id, history.History[0].ItemId);

        var upcoming = await lab.Chat.InterpretAsync(lab.OwnerId, "最近要到期的有哪些？");
        Assert.Equal("query", upcoming.Kind);
        Assert.Contains(upcoming.Upcoming, item => item.ItemId == mine.Id);
        Assert.DoesNotContain(upcoming.Upcoming, item => item.ItemId == theirs.Id);

        var invalid = await lab.Chat.InterpretAsync(lab.OwnerId, "2月30日换了厨房净水器 PP 棉");
        Assert.Equal("rejected", invalid.Kind);
        Assert.Equal(HouseholdChatPhrase.RephraseMessage, invalid.Message);
        Assert.Null(invalid.DraftId);

        var unknown = await lab.Chat.InterpretAsync(lab.OwnerId, "你好呀");
        Assert.Equal("unrecognized", unknown.Kind);
        Assert.Equal(HouseholdChatPhrase.UnrecognizedMessage, unknown.Message);
        Assert.Null(unknown.DraftId);
        Assert.Equal(2, await lab.Db.HouseholdCompletionRecords.CountAsync());
    }

    [Fact]
    public async Task Confirm_SoftDeletedCandidate_Returns404_AndWritesNothing()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        var item = await lab.CreateAsync("软删滤网");
        var before = await lab.Db.HouseholdCompletionRecords.CountAsync();
        var draft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了软删滤网");
        Assert.Equal("confirm", draft.Kind);
        Assert.Contains(draft.Candidates, candidate => candidate.Id == item.Id);

        await lab.Items.DeleteAsync(lab.OwnerId, item.Id);
        var raw = await lab.Db.HouseholdItems.IgnoreQueryFilters().SingleAsync(i => i.Id == item.Id);
        Assert.True(raw.IsDeleted);

        var gone = await Assert.ThrowsAsync<BusinessException>(() => lab.Chat.ConfirmAsync(
            lab.OwnerId, Confirm(draft, item.Id), "soft-deleted"));
        Assert.Equal(404, gone.StatusCode);
        Assert.Equal("事项不存在", gone.Message);
        Assert.Equal(before, await lab.Db.HouseholdCompletionRecords.CountAsync());
    }

    [Fact]
    public async Task SoftDeletedItem_IsHiddenFromMatchAndQuery()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        var deleted = await lab.CreateAsync("护照", lastDone: new DateOnly(2026, 9, 8));
        await lab.Items.CompleteAsync(lab.OwnerId, deleted.Id, new CompleteHouseholdItemRequest
        {
            CompletedOn = new DateOnly(2026, 9, 20)
        });
        var visible = await lab.CreateAsync("纱窗", lastDone: new DateOnly(2026, 9, 8));
        await lab.Items.DeleteAsync(lab.OwnerId, deleted.Id);
        Assert.True(await lab.Db.HouseholdItems.IgnoreQueryFilters().AnyAsync(i => i.Id == deleted.Id && i.IsDeleted));

        var named = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了护照");
        Assert.Equal("rejected", named.Kind);
        Assert.Equal(HouseholdChatService.RetiredMessage, named.Message);
        Assert.Null(named.DraftId);
        Assert.DoesNotContain(named.Candidates, candidate => candidate.Id == deleted.Id);

        var shared = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了纱窗");
        Assert.Contains(shared.Candidates, candidate => candidate.Id == visible.Id);
        Assert.DoesNotContain(shared.Candidates, candidate => candidate.Id == deleted.Id);

        var history = await lab.Chat.InterpretAsync(lab.OwnerId, "护照什么时候换的？");
        Assert.DoesNotContain(history.History, line => line.ItemId == deleted.Id);

        var upcoming = await lab.Chat.InterpretAsync(lab.OwnerId, "最近要到期的有哪些？");
        Assert.Contains(upcoming.Upcoming, line => line.ItemId == visible.Id);
        Assert.DoesNotContain(upcoming.Upcoming, line => line.ItemId == deleted.Id);
    }

    [Fact]
    public async Task Confirm_RejectsOverlongIdempotencyKey_AndKeepsTheDraftPending()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        var item = await lab.CreateAsync("厨房净水器 PP 棉");
        var draft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了厨房净水器 PP 棉");
        var tooLong = new string('k', HouseholdIdempotency.KeyMaxLength + 1);

        var rejected = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Chat.ConfirmAsync(lab.OwnerId, Confirm(draft, item.Id), tooLong));
        Assert.Equal(400, rejected.StatusCode);
        Assert.Equal(HouseholdIdempotency.KeyTooLongMessage, rejected.Message);
        var pending = await lab.Db.HouseholdChatDrafts.SingleAsync(d => d.Id == draft.DraftId);
        Assert.Null(pending.IdempotencyKey);
        Assert.Null(pending.StoredItemId);
        Assert.Equal(0, await lab.Db.HouseholdCompletionRecords.CountAsync());

        var direct = await Assert.ThrowsAsync<BusinessException>(() => lab.Items.CompleteAsync(
            lab.OwnerId,
            item.Id,
            new CompleteHouseholdItemRequest { CompletedOn = new DateOnly(2026, 10, 8) },
            tooLong));
        Assert.Equal(400, direct.StatusCode);
        Assert.Equal(HouseholdIdempotency.KeyTooLongMessage, direct.Message);
        Assert.Equal(0, await lab.Db.HouseholdCompletionRecords.CountAsync());

        var exact = new string('k', HouseholdIdempotency.KeyMaxLength);
        var done = await lab.Chat.ConfirmAsync(lab.OwnerId, Confirm(draft, item.Id), exact);
        Assert.True(done.Record.Id > 0);
        var confirmed = await lab.Db.HouseholdChatDrafts.SingleAsync(d => d.Id == draft.DraftId);
        Assert.Equal(exact, confirmed.IdempotencyKey);
        Assert.Equal(item.Id, confirmed.StoredItemId);
        Assert.Equal(1, await lab.Db.HouseholdCompletionRecords.CountAsync(r => r.IdempotencyKey == exact));
    }

    [Fact]
    public async Task Confirm_RejectsCostAboveTheSharedLimit_AndKeepsTheDraftPending()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        var item = await lab.CreateAsync("厨房净水器 PP 棉");
        var draft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了厨房净水器 PP 棉");
        var request = Confirm(draft, item.Id);
        request.Cost = HouseholdCost.MaxAmount + 0.01m;

        var rejected = await Assert.ThrowsAsync<BusinessException>(() =>
            lab.Chat.ConfirmAsync(lab.OwnerId, request, "over-cost"));

        Assert.Equal(400, rejected.StatusCode);
        Assert.Equal("费用超出范围", rejected.Message);
        var pending = await lab.Db.HouseholdChatDrafts.SingleAsync(d => d.Id == draft.DraftId);
        Assert.Null(pending.IdempotencyKey);
        Assert.Null(pending.StoredItemId);
        Assert.Equal(0, await lab.Db.HouseholdCompletionRecords.CountAsync());
    }

    [Fact]
    public async Task ParallelConfirm_WritesExactlyOneCompletion()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        var item = await lab.CreateAsync("厨房净水器 PP 棉");
        var draft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了厨房净水器 PP 棉");
        var request = Confirm(draft, item.Id);
        var first = lab.OpenChat();
        var second = lab.OpenChat();

        var outcomes = await Task.WhenAll(
            ConfirmQuietly(first, lab.OwnerId, request, "parallel-a"),
            ConfirmQuietly(second, lab.OwnerId, request, "parallel-b"));

        Assert.Equal(1, await lab.Db.HouseholdCompletionRecords.CountAsync(r => r.HouseholdItemId == item.Id));
        Assert.Contains(outcomes, error => error == null);
    }

    [Fact]
    public async Task Interpret_PurgesExpiredDrafts()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        await lab.CreateAsync("厨房净水器 PP 棉");
        var draft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了厨房净水器 PP 棉");
        var row = await lab.Db.HouseholdChatDrafts.SingleAsync(d => d.Id == draft.DraftId);
        row.ExpiresAt = Morning.UtcDateTime.AddMinutes(-1);
        await lab.Db.SaveChangesAsync();

        lab.Clock.UtcNow = Morning.AddMinutes(1);
        await lab.Chat.InterpretAsync(lab.OwnerId, "昨天换了厨房净水器 PP 棉");

        Assert.True(await lab.Db.HouseholdChatDrafts.IgnoreQueryFilters().AnyAsync(d => d.Id == draft.DraftId && d.IsDeleted));
        Assert.DoesNotContain(draft.DraftId, await lab.Db.HouseholdChatDrafts.Select(d => (int?)d.Id).ToListAsync());
    }

    [Fact]
    public async Task ListForSession_RestoresOwnDrafts_IncludingPurged_AndDoesNotLeak()
    {
        await using var lab = await ChatLab.CreateAsync(Morning);
        var session = new ChatSession { UserId = lab.OwnerId, Title = "家务" };
        lab.Db.ChatSessions.Add(session);
        await lab.Db.SaveChangesAsync();

        HouseholdChatInterpretationDto draft;
        using (HouseholdChatAmbient.Push(session.Id))
        {
            var item = await lab.CreateAsync("护照");
            draft = await lab.Chat.InterpretAsync(lab.OwnerId, "今天换了护照");
            Assert.Equal(session.Id, await lab.Db.HouseholdChatDrafts.Where(d => d.Id == draft.DraftId).Select(d => d.ChatSessionId).SingleAsync());

            var listed = await lab.Chat.ListForSessionAsync(lab.OwnerId, session.Id);
            var card = Assert.Single(listed);
            Assert.Equal(draft.DraftId, card.DraftId);
            Assert.False(card.Confirmed);
            Assert.Equal("护照", card.Item!.Name);

            await lab.Chat.ConfirmAsync(lab.OwnerId, Confirm(draft, item.Id), "listed-once");
        }

        var confirmed = Assert.Single(await lab.Chat.ListForSessionAsync(lab.OwnerId, session.Id));
        Assert.True(confirmed.Confirmed);

        lab.Clock.UtcNow = lab.Clock.UtcNow.Add(HouseholdChatService.DraftLifetime).AddSeconds(1);
        await lab.Chat.PurgeExpiredDraftsAsync();
        Assert.True(await lab.Db.HouseholdChatDrafts.IgnoreQueryFilters().AnyAsync(d => d.Id == draft.DraftId && d.IsDeleted));
        var afterPurge = Assert.Single(await lab.Chat.ListForSessionAsync(lab.OwnerId, session.Id));
        Assert.True(afterPurge.Confirmed);
        Assert.Equal(draft.DraftId, afterPurge.DraftId);

        lab.Db.HouseholdChatDrafts.Add(new HouseholdChatDraft
        {
            UserId = lab.OwnerId,
            HouseholdId = session.Id + 9000,
            ChatSessionId = session.Id,
            CompletedOn = new DateOnly(2026, 10, 8),
            CandidateItemIds = "[]",
            ExpiresAt = DateTime.SpecifyKind(Morning.AddMinutes(10).UtcDateTime, DateTimeKind.Utc)
        });
        await lab.Db.SaveChangesAsync();
        Assert.Single(await lab.Chat.ListForSessionAsync(lab.OwnerId, session.Id));

        var other = await lab.AddUserAsync("outsider");
        var denied = await Assert.ThrowsAsync<BusinessException>(() => lab.Chat.ListForSessionAsync(other, session.Id));
        Assert.Equal(404, denied.StatusCode);
        Assert.Equal("对话不存在", denied.Message);

        var otherSession = new ChatSession { UserId = other, Title = "空" };
        lab.Db.ChatSessions.Add(otherSession);
        await lab.Db.SaveChangesAsync();
        var households = await lab.Db.Households.CountAsync();
        Assert.Empty(await lab.Chat.ListForSessionAsync(other, otherSession.Id));
        Assert.Equal(households, await lab.Db.Households.CountAsync());

        var missing = await Assert.ThrowsAsync<BusinessException>(() => lab.Chat.ListForSessionAsync(lab.OwnerId, 99999));
        Assert.Equal(404, missing.StatusCode);
    }

    private static async Task<Exception?> ConfirmQuietly(
        HouseholdChatService chat, int userId, ConfirmHouseholdChatRequest request, string key)
    {
        try
        {
            await chat.ConfirmAsync(userId, request, key);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static ConfirmHouseholdChatRequest Confirm(HouseholdChatInterpretationDto draft, int itemId, bool? deduct = null) => new()
    {
        DraftId = draft.DraftId!.Value,
        ItemId = itemId,
        DeductConsumable = deduct
    };

    private sealed class ChatLab : IAsyncDisposable
    {
        private readonly MiraiTestFixture _fx;
        public MiraiNoteDbContext Db { get; }
        public MutableTimeProvider Clock { get; }
        public HouseholdItemService Items { get; }
        public HouseholdConsumableService Consumables { get; }
        public HouseholdChatService Chat { get; }
        public int OwnerId { get; }
        private readonly List<MiraiNoteDbContext> _extra = new();

        private ChatLab(
            MiraiTestFixture fx,
            MiraiNoteDbContext db,
            MutableTimeProvider clock,
            HouseholdItemService items,
            HouseholdConsumableService consumables,
            HouseholdChatService chat,
            int ownerId)
        {
            _fx = fx;
            Db = db;
            Clock = clock;
            Items = items;
            Consumables = consumables;
            Chat = chat;
            OwnerId = ownerId;
        }

        public static Task<ChatLab> CreateAsync(DateTimeOffset utcNow)
        {
            var fx = new MiraiTestFixture();
            var db = fx.CreateContext();
            var clock = new MutableTimeProvider(utcNow);
            var rules = new HouseholdCycleRules(clock);
            var access = new HouseholdAccessService(db, clock);
            var policy = HouseholdAccessPolicy.Default;
            var items = new HouseholdItemService(db, access, rules, policy);
            var consumables = new HouseholdConsumableService(db, access, policy);
            var chat = new HouseholdChatService(db, access, items, rules);
            return Task.FromResult(new ChatLab(fx, db, clock, items, consumables, chat, db.Users.Single().Id));
        }

        public Task<HouseholdItemDto> CreateAsync(
            string name,
            int? userId = null,
            DateOnly? lastDone = null,
            int? consumableId = null,
            IEnumerable<string>? aliases = null,
            string? location = null) =>
            Items.CreateAsync(userId ?? OwnerId, new CreateHouseholdItemRequest
            {
                Name = name,
                ItemType = HouseholdItemType.Recurring,
                CycleValue = 1,
                CycleUnit = HouseholdCycleUnit.Month,
                LastDoneDate = lastDone ?? new DateOnly(2026, 9, 8),
                ConsumableId = consumableId,
                Aliases = aliases?.ToList(),
                Location = location
            });

        public HouseholdChatService OpenChat()
        {
            var db = _fx.CreateContext();
            db.Database.ExecuteSqlRaw("PRAGMA busy_timeout = 5000");
            _extra.Add(db);
            var rules = new HouseholdCycleRules(Clock);
            var access = new HouseholdAccessService(db, Clock);
            var items = new HouseholdItemService(db, access, rules, HouseholdAccessPolicy.Default);
            return new HouseholdChatService(db, access, items, rules);
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
            foreach (var extra in _extra)
                await extra.DisposeAsync();
            await Db.DisposeAsync();
            _fx.Dispose();
        }
    }

    private sealed class MutableTimeProvider : TimeProvider, IHouseholdClock
    {
        public MutableTimeProvider(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
