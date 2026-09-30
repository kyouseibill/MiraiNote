using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

public interface IHouseholdChatService
{
    Task<HouseholdChatInterpretationDto> InterpretAsync(int userId, string? utterance, CancellationToken ct = default);
    Task<CompleteHouseholdItemResult> ConfirmAsync(
        int userId, ConfirmHouseholdChatRequest request, string? idempotencyKey, CancellationToken ct = default);
}

public sealed class HouseholdChatService : IHouseholdChatService
{
    public static readonly TimeSpan DraftLifetime = TimeSpan.FromMinutes(10);
    public const string ExpiredMessage = "确认已过期，请重新说一次";
    public const string MissingKeyMessage = "缺少 Idempotency-Key";
    public const string NotCandidateMessage = "只能确认这次匹配到的事项";

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> DraftGates = new();

    private readonly MiraiNoteDbContext _db;
    private readonly IHouseholdAccessService _access;
    private readonly IHouseholdItemService _items;
    private readonly HouseholdCycleRules _rules;

    public HouseholdChatService(
        MiraiNoteDbContext db,
        IHouseholdAccessService access,
        IHouseholdItemService items,
        HouseholdCycleRules rules)
    {
        _db = db;
        _access = access;
        _items = items;
        _rules = rules;
    }

    public async Task<HouseholdChatInterpretationDto> InterpretAsync(int userId, string? utterance, CancellationToken ct = default)
    {
        var access = await _access.GetOrCreateAsync(userId, ct);
        var today = _rules.Today();
        var parsed = HouseholdChatPhrase.Parse(utterance, today);
        var text = utterance?.Trim() ?? "";

        if (parsed.Intent == HouseholdChatIntent.Unrecognized)
            return Explain("unrecognized", HouseholdChatPhrase.UnrecognizedMessage);

        if (parsed.Intent == HouseholdChatIntent.Upcoming)
            return await QueryUpcomingAsync(userId, ct);

        if (parsed.Intent == HouseholdChatIntent.History)
            return await QueryHistoryAsync(access.Household.Id, parsed.NameHint, ct);

        if (parsed.FutureDate)
            return Explain("rejected", "完成日期不能晚于今天");

        var items = await LoadItemsAsync(access.Household.Id, includeArchived: false, ct);
        var matched = BestMatches(items, text);
        if (matched.Count == 0)
        {
            return new HouseholdChatInterpretationDto
            {
                Kind = "create",
                Message = "没有匹配到事项。可以新建一个，名称已经预填。",
                SuggestedName = parsed.NameHint,
                CompletedOn = parsed.CompletedOn,
                Cost = parsed.Cost
            };
        }

        var expires = UtcNow().Add(DraftLifetime);
        var draft = new HouseholdChatDraft
        {
            UserId = userId,
            HouseholdId = access.Household.Id,
            CompletedOn = parsed.CompletedOn ?? today,
            Cost = parsed.Cost,
            CandidateItemIds = JsonSerializer.Serialize(matched.Select(item => item.Id).ToArray()),
            ExpiresAt = expires
        };
        _db.HouseholdChatDrafts.Add(draft);
        await _db.SaveChangesAsync(ct);

        var candidates = matched.Select(ToCandidate).ToList();
        return new HouseholdChatInterpretationDto
        {
            Kind = matched.Count == 1 ? "confirm" : "choose",
            Message = matched.Count == 1 ? "请确认后再写入。" : "匹配到多项，请选择一项再确认。",
            DraftId = draft.Id,
            ExpiresAt = new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)),
            CompletedOn = draft.CompletedOn,
            Cost = draft.Cost,
            DeductConsumable = candidates.Any(item => item.ConsumableId != null),
            Item = matched.Count == 1 ? candidates[0] : null,
            Candidates = candidates
        };
    }

    public async Task<CompleteHouseholdItemResult> ConfirmAsync(
        int userId, ConfirmHouseholdChatRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new BusinessException(MissingKeyMessage, 400);

        var access = await _access.GetOrCreateAsync(userId, ct);
        var draft = await _db.HouseholdChatDrafts
            .FirstOrDefaultAsync(d => d.Id == request.DraftId && d.UserId == userId && d.HouseholdId == access.Household.Id, ct);
        if (draft == null)
            throw new BusinessException("确认不存在", 404);

        var gate = DraftGates.GetOrAdd(draft.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await _db.Entry(draft).ReloadAsync(ct);
            if (!string.IsNullOrEmpty(draft.IdempotencyKey) && draft.StoredItemId is int storedId && draft.StoredCompletedOn is DateOnly storedOn)
            {
                return await _items.CompleteAsync(userId, storedId, StoredRequest(draft, storedOn), draft.IdempotencyKey, ct);
            }

            if (UtcNow() >= DateTime.SpecifyKind(draft.ExpiresAt, DateTimeKind.Utc))
                throw new BusinessException(ExpiredMessage, 400);

            var item = await _db.HouseholdItems.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == request.ItemId && i.HouseholdId == access.Household.Id, ct);
            if (item == null)
                throw new BusinessException("事项不存在", 404);
            if (!CandidateIds(draft).Contains(item.Id))
                throw new BusinessException(NotCandidateMessage, 400);
            if (item.IsArchived)
                throw new BusinessException(HouseholdItemService.ArchivedReadOnlyMessage, 400);

            var completedOn = request.CompletedOn ?? draft.CompletedOn;
            var cost = request.Cost ?? draft.Cost;
            var skip = request.DeductConsumable == false;
            var complete = new CompleteHouseholdItemRequest
            {
                CompletedOn = completedOn,
                Cost = cost,
                SkipConsumableDeduction = skip
            };
            var result = await _items.CompleteAsync(userId, item.Id, complete, idempotencyKey, ct);

            var saved = await _db.HouseholdChatDrafts.FirstAsync(d => d.Id == draft.Id, ct);
            saved.IdempotencyKey = idempotencyKey.Trim();
            saved.StoredItemId = item.Id;
            saved.StoredCompletedOn = completedOn;
            saved.StoredCost = cost;
            saved.StoredSkipDeduction = skip;
            await _db.SaveChangesAsync(ct);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<HouseholdChatInterpretationDto> QueryUpcomingAsync(int userId, CancellationToken ct)
    {
        var upcoming = await _items.UpcomingAsync(userId, null, ct);
        var lines = upcoming.Overdue.Concat(upcoming.Within7Days).Concat(upcoming.Within30Days)
            .Take(12)
            .Select(item => new HouseholdChatUpcomingLineDto
            {
                ItemId = item.Id,
                Name = item.Name,
                DueDate = item.DueDate
            })
            .ToList();
        return new HouseholdChatInterpretationDto
        {
            Kind = "query",
            Message = lines.Count == 0 ? "最近没有要到期的事项。" : $"最近要到期的有 {lines.Count} 项。",
            Upcoming = lines
        };
    }

    private async Task<HouseholdChatInterpretationDto> QueryHistoryAsync(int householdId, string? subject, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return Explain("unrecognized", "要问哪一项什么时候做过，请带上事项名称。");

        var items = await LoadItemsAsync(householdId, includeArchived: true, ct);
        var matched = BestMatches(items, subject);
        if (matched.Count == 0)
            return Explain("query", "本家庭里没有找到这项。");

        var ids = matched.Select(item => item.Id).ToArray();
        var names = matched.ToDictionary(item => item.Id, item => item.Name);
        var records = await _db.HouseholdCompletionRecords.AsNoTracking()
            .Where(r => ids.Contains(r.HouseholdItemId))
            .OrderByDescending(r => r.CompletedOn)
            .ThenByDescending(r => r.Id)
            .Take(8)
            .ToListAsync(ct);
        var lines = records.Select(record => new HouseholdChatHistoryLineDto
        {
            ItemId = record.HouseholdItemId,
            ItemName = names.GetValueOrDefault(record.HouseholdItemId, ""),
            CompletedOn = record.CompletedOn,
            Cost = record.Cost
        }).ToList();
        return new HouseholdChatInterpretationDto
        {
            Kind = "query",
            Message = lines.Count == 0 ? "还没有完成记录。" : $"找到 {lines.Count} 条完成记录。",
            History = lines
        };
    }

    private async Task<List<MatchableItem>> LoadItemsAsync(int householdId, bool includeArchived, CancellationToken ct)
    {
        var query = _db.HouseholdItems.AsNoTracking().Where(i => i.HouseholdId == householdId);
        if (!includeArchived)
            query = query.Where(i => !i.IsArchived);
        var rows = await query
            .OrderBy(i => i.Id)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Location,
                i.AliasesJson,
                i.IsPaused,
                i.NextDueDate,
                i.ConsumableId
            })
            .ToListAsync(ct);
        var consumableIds = rows.Where(row => row.ConsumableId != null).Select(row => row.ConsumableId!.Value).Distinct().ToArray();
        var consumables = consumableIds.Length == 0
            ? new Dictionary<int, (string Name, int Stock)>()
            : await _db.HouseholdConsumables.AsNoTracking()
                .Where(c => consumableIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => (Name: c.Name, Stock: c.CurrentStock), ct);

        return rows.Select(row =>
        {
            consumables.TryGetValue(row.ConsumableId ?? 0, out var consumable);
            return new MatchableItem(
                row.Id,
                row.Name,
                row.Location,
                HouseholdAliases.Deserialize(row.AliasesJson),
                row.IsPaused,
                row.NextDueDate,
                row.ConsumableId,
                row.ConsumableId == null ? null : consumable.Name,
                row.ConsumableId == null ? null : consumable.Stock);
        }).ToList();
    }

    private static List<MatchableItem> BestMatches(List<MatchableItem> items, string text)
    {
        var scored = items
            .Select(item => (item, score: HouseholdChatPhrase.Score(text, item.Name, item.Location, item.Aliases)))
            .Where(row => row.score >= 2)
            .ToList();
        if (scored.Count == 0)
            return [];
        var best = scored.Max(row => row.score);
        return scored.Where(row => row.score == best).Select(row => row.item).ToList();
    }

    private static HouseholdChatCandidateDto ToCandidate(MatchableItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Location = item.Location,
        IsPaused = item.IsPaused,
        NextDueDate = item.NextDueDate,
        ConsumableId = item.ConsumableId,
        ConsumableName = item.ConsumableName,
        ConsumableStock = item.ConsumableStock
    };

    private static CompleteHouseholdItemRequest StoredRequest(HouseholdChatDraft draft, DateOnly completedOn) => new()
    {
        CompletedOn = completedOn,
        Cost = draft.StoredCost,
        SkipConsumableDeduction = draft.StoredSkipDeduction
    };

    private static HashSet<int> CandidateIds(HouseholdChatDraft draft)
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(draft.CandidateItemIds)?.ToHashSet() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private DateTime UtcNow() => DateTime.SpecifyKind(_rules.UtcNow.UtcDateTime, DateTimeKind.Utc);

    private static HouseholdChatInterpretationDto Explain(string kind, string message) => new()
    {
        Kind = kind,
        Message = message
    };

    private sealed record MatchableItem(
        int Id,
        string Name,
        string? Location,
        List<string> Aliases,
        bool IsPaused,
        DateOnly? NextDueDate,
        int? ConsumableId,
        string? ConsumableName,
        int? ConsumableStock);
}
