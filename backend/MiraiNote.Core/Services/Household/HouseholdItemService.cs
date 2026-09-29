using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

public interface IHouseholdItemService
{
    Task<List<HouseholdItemDto>> ListAsync(int userId, HouseholdItemListQuery? query, CancellationToken ct = default);
    Task<HouseholdItemDto> GetAsync(int userId, int id, CancellationToken ct = default);
    Task<HouseholdItemDto> CreateAsync(int userId, CreateHouseholdItemRequest request, CancellationToken ct = default);
    Task<HouseholdItemDto> UpdateAsync(int userId, int id, UpdateHouseholdItemRequest request, CancellationToken ct = default);
    Task<HouseholdItemDto> SetPausedAsync(int userId, int id, bool paused, CancellationToken ct = default);
    Task DeleteAsync(int userId, int id, CancellationToken ct = default);
    Task<CompleteHouseholdItemResult> CompleteAsync(
        int userId, int id, CompleteHouseholdItemRequest request, string? idempotencyKey = null, CancellationToken ct = default);
    Task<List<HouseholdCompletionDto>> HistoryAsync(int userId, int id, CancellationToken ct = default);
    Task<HouseholdUpcomingDto> UpcomingAsync(int userId, HouseholdUpcomingQuery? query, CancellationToken ct = default);
    Task<List<HouseholdItemTemplateDto>> ListTemplatesAsync(int userId, CancellationToken ct = default);
    Task<HouseholdItemDto> CreateFromTemplateAsync(int userId, CreateHouseholdItemFromTemplateRequest request, CancellationToken ct = default);
}

public sealed class HouseholdItemService : IHouseholdItemService
{
    /// <summary>同一事项、相同提交内容在此窗口内的第二次完成会被拒绝。</summary>
    public const int DuplicateCompletionWindowSeconds = 3;

    public const string IdempotencyBodyMismatchMessage = "同一 Idempotency-Key 不能用于不同的完成请求";

    public const string BackfillRenewalMessage = "补记日期早于上次完成日期时不能同时填写新的到期日";

    /// <summary>
    /// 只在当前进程内串行化同一事项的完成。多实例部署时这把锁不跨进程，
    /// 跨实例由（用户、事项、Idempotency-Key）唯一索引兜住。空闲后从字典移除，避免事项 Id 无限堆积。
    /// </summary>
    private static readonly ConcurrentDictionary<int, CompletionGate> CompletionGates = new();

    private static readonly object CompletionGateEviction = new();

    internal static int CompletionGateCount => CompletionGates.Count;

    private readonly MiraiNoteDbContext _db;
    private readonly IHouseholdAccessService _access;
    private readonly HouseholdCycleRules _rules;
    private readonly HouseholdAccessPolicy _policy;

    public HouseholdItemService(
        MiraiNoteDbContext db,
        IHouseholdAccessService access,
        HouseholdCycleRules rules,
        HouseholdAccessPolicy policy)
    {
        _db = db;
        _access = access;
        _rules = rules;
        _policy = policy;
    }

    public async Task<List<HouseholdItemDto>> ListAsync(int userId, HouseholdItemListQuery? query, CancellationToken ct = default)
    {
        query ??= new HouseholdItemListQuery();
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var items = _db.HouseholdItems.AsNoTracking().Where(i => i.HouseholdId == ctx.Household.Id);
        if (!query.IncludePaused)
            items = items.Where(i => !i.IsPaused);
        if (query.Category is HouseholdCategory category)
            items = items.Where(i => i.Category == category);

        var list = await items
            .OrderBy(i => i.IsPaused)
            .ThenBy(i => i.NextDueDate)
            .ThenBy(i => i.Id)
            .ToListAsync(ct);
        return await MapItemsAsync(list, ct);
    }

    public async Task<HouseholdItemDto> GetAsync(int userId, int id, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var entity = await LoadAsync(ctx.Household.Id, id, tracking: false, ct);
        return await MapOneAsync(entity, ct);
    }

    public async Task<HouseholdItemDto> CreateAsync(int userId, CreateHouseholdItemRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        return await CreateCoreAsync(ctx, request, ct);
    }

    public async Task<HouseholdItemDto> UpdateAsync(int userId, int id, UpdateHouseholdItemRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var entity = await LoadAsync(ctx.Household.Id, id, tracking: true, ct);
        _policy.EnsureCanUpdateItem(ctx.IsAdmin);
        var draft = Normalize(request.Name, request.Category, request.Location, request.ModelSpec, request.ItemType,
            request.CycleValue, request.CycleUnit, request.LastDoneDate, request.ExpiryDate, request.LeadDays,
            request.AssigneeMemberId, request.ConsumableId, request.Note, request.PurchaseLink,
            request.MileageCycleKm, request.Aliases);
        await EnsureMemberOfHouseholdAsync(ctx.Household.Id, draft.AssigneeMemberId, "负责人必须是本家庭成员", ct);
        await EnsureConsumableOfHouseholdAsync(ctx.Household.Id, draft.ConsumableId, ct);
        Apply(entity, draft);
        await _db.SaveChangesAsync(ct);
        return await MapOneAsync(entity, ct);
    }

    public async Task<HouseholdItemDto> SetPausedAsync(int userId, int id, bool paused, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var entity = await LoadAsync(ctx.Household.Id, id, tracking: true, ct);
        _policy.EnsureCanPauseItem(ctx.IsAdmin);
        entity.IsPaused = paused;
        await _db.SaveChangesAsync(ct);
        return await MapOneAsync(entity, ct);
    }

    public async Task DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var entity = await LoadAsync(ctx.Household.Id, id, tracking: true, ct);
        _policy.EnsureCanDeleteItem(ctx.IsAdmin);
        entity.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<CompleteHouseholdItemResult> CompleteAsync(
        int userId, int id, CompleteHouseholdItemRequest request, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var key = NormalizeIdempotencyKey(idempotencyKey);
        var access = await _access.GetOrCreateAsync(userId, ct);
        var householdId = access.Household.Id;
        var callerMemberId = access.Member.Id;

        var gate = AcquireCompletionGate(id);
        var entered = false;
        try
        {
            await gate.Semaphore.WaitAsync(ct);
            entered = true;

            // SQL Server 启用了 EnableRetryOnFailure。用户事务必须整段放进执行策略，
            // 否则策略在已有事务上执行查询或 SaveChanges 时抛 InvalidOperationException。
            // 委托可能被重试，所以每次都清跟踪并重新加载事项和耗材。
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                return await CompleteCoreAsync(userId, householdId, callerMemberId, id, request, key, ct);
            });
        }
        finally
        {
            ReleaseCompletionGate(id, gate, entered);
        }
    }

    private async Task<CompleteHouseholdItemResult> CompleteCoreAsync(
        int userId,
        int householdId,
        int callerMemberId,
        int id,
        CompleteHouseholdItemRequest request,
        string? key,
        CancellationToken ct)
    {
        var completedOn = _rules.ResolveCompletionDate(request.CompletedOn);
        var cost = NormalizeCost(request.Cost);
        var note = HouseholdText.Clean(request.Note, HouseholdFieldLimits.Note, "备注");
        var purchaseLink = HouseholdText.CleanPurchaseLink(request.PurchaseLink);
        var photos = HouseholdPhotoRefs.Normalize(request.PhotoRefs);

        var item = await LoadAsync(householdId, id, tracking: true, ct);
        var member = await RequireMemberAsync(
            householdId,
            request.CompletedByMemberId ?? callerMemberId,
            "执行人必须是本家庭成员",
            ct);
        var bodyHash = SubmissionFingerprint(request, completedOn, member.Id, cost, note, purchaseLink, photos);

        if (key != null)
        {
            var existing = await FindByIdempotencyKeyAsync(userId, id, key, ct);
            if (existing != null)
                return await ReplayOrRejectAsync(householdId, existing, bodyHash, ct);
        }

        if (item.ItemType == HouseholdItemType.Recurring && request.NewExpiryDate != null)
            throw new BusinessException("周期型事项不能填写新的到期日", 400);
        if (request.NewExpiryDate is DateOnly renewalDate)
            _rules.EnsureRenewalExpiry(renewalDate, completedOn);
        if (item.LastDoneDate is DateOnly lastDone && completedOn < lastDone && request.NewExpiryDate != null)
            throw new BusinessException(BackfillRenewalMessage, 400);

        var username = await _db.Users.AsNoTracking()
            .Where(u => u.Id == member.UserId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(ct)
            ?? throw new BusinessException("用户不存在", 400);

        await RejectDuplicateSubmissionAsync(item.Id, bodyHash, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // 耗材必须在本次委托里重新加载。重试时不能沿用上一次跟踪到的库存。
            var deduction = await DeductAsync(householdId, item, request, ct);
            var advancesSchedule = item.LastDoneDate is not DateOnly last || completedOn >= last;
            DateOnly? renewal = null;
            if (advancesSchedule)
            {
                if (item.ItemType == HouseholdItemType.Recurring)
                {
                    if (item.CycleValue is not int cycle || item.CycleUnit is not HouseholdCycleUnit unit)
                        throw new BusinessException("周期型事项缺少周期", 400);
                    item.LastDoneDate = completedOn;
                    item.NextDueDate = HouseholdCycleRules.AddCycle(completedOn, cycle, unit);
                }
                else
                {
                    if (item.ExpiryDate == null && request.NewExpiryDate == null)
                        throw new BusinessException("一次性到期事项缺少到期日", 400);
                    if (request.NewExpiryDate is DateOnly nextExpiry)
                    {
                        item.ExpiryDate = nextExpiry;
                        item.NextDueDate = nextExpiry;
                        renewal = nextExpiry;
                    }

                    item.LastDoneDate = completedOn;
                }
            }

            var record = new HouseholdCompletionRecord
            {
                HouseholdItemId = item.Id,
                CompletedOn = completedOn,
                CompletedByMemberId = member.Id,
                CompletedByUserId = member.UserId,
                CompletedByUsername = username,
                PhotoRefs = photos.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(photos),
                Cost = cost,
                PurchaseLink = purchaseLink,
                Note = note,
                ConsumableId = deduction.ConsumableId,
                ConsumableQuantityDeducted = deduction.Actual,
                NeedsRestock = deduction.NeedsRestock,
                NewExpiryDate = renewal,
                IdempotencyUserId = key == null ? null : userId,
                IdempotencyKey = key,
                RequestBodyHash = bodyHash,
                SubmissionFingerprint = bodyHash
            };
            _db.HouseholdCompletionRecords.Add(record);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new CompleteHouseholdItemResult
            {
                Item = await MapOneAsync(item, ct),
                Record = ToCompletionDto(record),
                ConsumableQuantityDeducted = deduction.Actual,
                ConsumableStockAfter = deduction.StockAfter,
                NeedsRestock = deduction.NeedsRestock
            };
        }
        catch (DbUpdateException) when (key != null)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var raced = await FindByIdempotencyKeyAsync(userId, id, key, ct);
            if (raced != null)
                return await ReplayOrRejectAsync(householdId, raced, bodyHash, ct);
            throw;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<List<HouseholdCompletionDto>> HistoryAsync(int userId, int id, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var exists = await _db.HouseholdItems.AnyAsync(i => i.Id == id && i.HouseholdId == ctx.Household.Id, ct);
        if (!exists)
            throw new BusinessException("事项不存在", 404);

        var records = await _db.HouseholdCompletionRecords.AsNoTracking()
            .Where(r => r.HouseholdItemId == id)
            .OrderByDescending(r => r.CompletedOn)
            .ThenByDescending(r => r.Id)
            .ToListAsync(ct);
        return records.Select(ToCompletionDto).ToList();
    }

    public async Task<HouseholdUpcomingDto> UpcomingAsync(int userId, HouseholdUpcomingQuery? query, CancellationToken ct = default)
    {
        query ??= new HouseholdUpcomingQuery();
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var today = _rules.Today();
        var horizon = today.AddDays(HouseholdCycleRules.Within30DayWindow);
        var items = _db.HouseholdItems.AsNoTracking()
            .Where(i => i.HouseholdId == ctx.Household.Id
                && !i.IsPaused
                && i.NextDueDate != null
                && i.NextDueDate <= horizon);
        if (query.Category is HouseholdCategory category)
            items = items.Where(i => i.Category == category);

        var list = await items.OrderBy(i => i.NextDueDate).ThenBy(i => i.Id).ToListAsync(ct);
        var names = await LoadMemberNamesAsync(list.Select(i => i.AssigneeMemberId), ct);
        var result = new HouseholdUpcomingDto { Today = today };
        foreach (var item in list)
        {
            var due = item.NextDueDate!.Value;
            var group = HouseholdCycleRules.Classify(due, today, item.IsPaused);
            if (group == UpcomingGroup.None)
                continue;

            var (overdue, remaining) = HouseholdCycleRules.DayOffsets(due, today);
            var dto = new HouseholdUpcomingItemDto
            {
                Id = item.Id,
                Name = item.Name,
                Category = item.Category,
                ItemType = item.ItemType,
                Location = item.Location,
                DueDate = due,
                DaysOverdue = overdue,
                DaysRemaining = remaining,
                AssigneeMemberId = item.AssigneeMemberId,
                AssigneeName = item.AssigneeMemberId is int memberId && names.TryGetValue(memberId, out var name) ? name : null
            };
            switch (group)
            {
                case UpcomingGroup.Overdue:
                    result.Overdue.Add(dto);
                    break;
                case UpcomingGroup.Within7Days:
                    result.Within7Days.Add(dto);
                    break;
                case UpcomingGroup.Within30Days:
                    result.Within30Days.Add(dto);
                    break;
            }
        }

        return result;
    }

    public async Task<List<HouseholdItemTemplateDto>> ListTemplatesAsync(int userId, CancellationToken ct = default)
    {
        await _access.GetOrCreateAsync(userId, ct);
        return await _db.HouseholdItemTemplates.AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .Select(t => new HouseholdItemTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Category = t.Category,
                ItemType = t.ItemType,
                CycleValue = t.CycleValue,
                CycleUnit = t.CycleUnit,
                SortOrder = t.SortOrder
            })
            .ToListAsync(ct);
    }

    public async Task<HouseholdItemDto> CreateFromTemplateAsync(
        int userId, CreateHouseholdItemFromTemplateRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var template = await _db.HouseholdItemTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
            ?? throw new BusinessException("模板不存在", 404);

        var create = new CreateHouseholdItemRequest
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? template.Name : request.Name,
            Category = request.Category ?? template.Category,
            ItemType = template.ItemType,
            CycleValue = request.CycleValue ?? template.CycleValue,
            CycleUnit = request.CycleUnit ?? template.CycleUnit,
            LastDoneDate = request.LastDoneDate,
            ExpiryDate = request.ExpiryDate,
            Location = request.Location,
            ModelSpec = request.ModelSpec,
            LeadDays = request.LeadDays,
            AssigneeMemberId = request.AssigneeMemberId,
            ConsumableId = request.ConsumableId,
            Note = request.Note,
            PurchaseLink = request.PurchaseLink,
            MileageCycleKm = request.MileageCycleKm,
            Aliases = request.Aliases
        };
        return await CreateCoreAsync(ctx, create, ct);
    }

    private async Task<HouseholdItemDto> CreateCoreAsync(
        HouseholdContext ctx, CreateHouseholdItemRequest request, CancellationToken ct)
    {
        var draft = Normalize(request.Name, request.Category, request.Location, request.ModelSpec, request.ItemType,
            request.CycleValue, request.CycleUnit, request.LastDoneDate, request.ExpiryDate, request.LeadDays,
            request.AssigneeMemberId, request.ConsumableId, request.Note, request.PurchaseLink,
            request.MileageCycleKm, request.Aliases);
        await EnsureMemberOfHouseholdAsync(ctx.Household.Id, draft.AssigneeMemberId, "负责人必须是本家庭成员", ct);
        await EnsureConsumableOfHouseholdAsync(ctx.Household.Id, draft.ConsumableId, ct);

        var entity = new HouseholdItem
        {
            HouseholdId = ctx.Household.Id,
            IsPaused = request.IsPaused
        };
        Apply(entity, draft);
        _db.HouseholdItems.Add(entity);
        await _db.SaveChangesAsync(ct);
        return await MapOneAsync(entity, ct);
    }

    private async Task<Deduction> DeductAsync(
        int householdId, HouseholdItem item, CompleteHouseholdItemRequest request, CancellationToken ct)
    {
        var requested = 0;
        if (!request.SkipConsumableDeduction)
        {
            requested = request.ConsumableQuantity
                ?? (item.ConsumableId != null ? HouseholdCycleRules.DefaultDeductionQuantity : 0);
        }

        if (requested < 0)
            throw new BusinessException("扣减数量不能为负", 400);
        if (requested == 0)
            return new Deduction(null, 0, null, false);
        if (item.ConsumableId is not int consumableId)
            throw new BusinessException("事项未关联耗材，无法扣减库存", 400);

        var consumable = await _db.HouseholdConsumables
            .FirstOrDefaultAsync(c => c.Id == consumableId && c.HouseholdId == householdId, ct)
            ?? throw new BusinessException("关联耗材不存在", 400);

        var result = HouseholdCycleRules.DeductStock(consumable.CurrentStock, requested);
        consumable.CurrentStock = result.NewStock;
        return new Deduction(consumable.Id, result.ActualDeducted, result.NewStock, result.NeedsRestock);
    }

    private async Task<HouseholdItem> LoadAsync(int householdId, int id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? _db.HouseholdItems : _db.HouseholdItems.AsNoTracking();
        return await query.FirstOrDefaultAsync(i => i.Id == id && i.HouseholdId == householdId, ct)
            ?? throw new BusinessException("事项不存在", 404);
    }

    private async Task EnsureMemberOfHouseholdAsync(int householdId, int? memberId, string message, CancellationToken ct)
    {
        if (memberId is not int id)
            return;
        await RequireMemberAsync(householdId, id, message, ct);
    }

    private async Task<HouseholdMember> RequireMemberAsync(int householdId, int memberId, string message, CancellationToken ct)
    {
        var member = await _db.HouseholdMembers.FirstOrDefaultAsync(m => m.Id == memberId, ct)
            ?? throw new BusinessException(message, 400);
        if (member.HouseholdId != householdId)
            throw new BusinessException(message, 403);
        return member;
    }

    private async Task EnsureConsumableOfHouseholdAsync(int householdId, int? consumableId, CancellationToken ct)
    {
        if (consumableId is not int id)
            return;

        var consumable = await _db.HouseholdConsumables.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new BusinessException("耗材不存在", 400);
        if (consumable.HouseholdId != householdId)
            throw new BusinessException("不能关联其他家庭的耗材", 403);
    }

    private static void Apply(HouseholdItem entity, HouseholdItemDraft draft)
    {
        entity.Name = draft.Name;
        entity.Category = draft.Category;
        entity.Location = draft.Location;
        entity.ModelSpec = draft.ModelSpec;
        entity.ItemType = draft.ItemType;
        entity.CycleValue = draft.CycleValue;
        entity.CycleUnit = draft.CycleUnit;
        entity.LastDoneDate = draft.LastDoneDate;
        entity.ExpiryDate = draft.ExpiryDate;
        entity.NextDueDate = draft.NextDueDate;
        entity.LeadDays = draft.LeadDays;
        entity.AssigneeMemberId = draft.AssigneeMemberId;
        entity.ConsumableId = draft.ConsumableId;
        entity.Note = draft.Note;
        entity.PurchaseLink = draft.PurchaseLink;
        entity.MileageCycleKm = draft.MileageCycleKm;
        entity.AliasesJson = HouseholdAliases.Serialize(draft.Aliases);
    }

    private HouseholdItemDraft Normalize(
        string? name,
        HouseholdCategory? category,
        string? location,
        string? modelSpec,
        HouseholdItemType itemType,
        int? cycleValue,
        HouseholdCycleUnit? cycleUnit,
        DateOnly? lastDoneDate,
        DateOnly? expiryDate,
        int? leadDays,
        int? assigneeMemberId,
        int? consumableId,
        string? note,
        string? purchaseLink,
        int? mileageCycleKm,
        IEnumerable<string>? aliases) =>
        HouseholdItemDraft.Normalize(
            name, category, location, modelSpec, itemType, cycleValue, cycleUnit, lastDoneDate, expiryDate,
            leadDays, assigneeMemberId, consumableId, note, purchaseLink, mileageCycleKm, aliases, _rules.Today());

    private async Task<HouseholdItemDto> MapOneAsync(HouseholdItem entity, CancellationToken ct)
    {
        var mapped = await MapItemsAsync([entity], ct);
        return mapped[0];
    }

    private async Task<List<HouseholdItemDto>> MapItemsAsync(List<HouseholdItem> items, CancellationToken ct)
    {
        var names = await LoadMemberNamesAsync(items.Select(i => i.AssigneeMemberId), ct);
        return items.Select(item => ToDto(item, names)).ToList();
    }

    private async Task<Dictionary<int, string>> LoadMemberNamesAsync(IEnumerable<int?> memberIds, CancellationToken ct)
    {
        var ids = memberIds.Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await _db.HouseholdMembers.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Join(
                _db.Users.AsNoTracking(),
                m => m.UserId,
                u => u.Id,
                (m, u) => new { m.Id, u.Username })
            .ToDictionaryAsync(x => x.Id, x => x.Username, ct);
    }

    private static HouseholdItemDto ToDto(HouseholdItem item, IReadOnlyDictionary<int, string> names) => new()
    {
        Id = item.Id,
        HouseholdId = item.HouseholdId,
        Name = item.Name,
        Category = item.Category,
        Location = item.Location,
        ModelSpec = item.ModelSpec,
        ItemType = item.ItemType,
        CycleValue = item.CycleValue,
        CycleUnit = item.CycleUnit,
        LastDoneDate = item.LastDoneDate,
        NextDueDate = item.NextDueDate,
        ExpiryDate = item.ExpiryDate,
        LeadDays = item.LeadDays,
        AssigneeMemberId = item.AssigneeMemberId,
        AssigneeName = item.AssigneeMemberId is int memberId && names.TryGetValue(memberId, out var name) ? name : null,
        ConsumableId = item.ConsumableId,
        Note = item.Note,
        PurchaseLink = item.PurchaseLink,
        IsPaused = item.IsPaused,
        MileageCycleKm = item.MileageCycleKm,
        Aliases = HouseholdAliases.Deserialize(item.AliasesJson),
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt
    };

    private static HouseholdCompletionDto ToCompletionDto(HouseholdCompletionRecord record) => new()
    {
        Id = record.Id,
        ItemId = record.HouseholdItemId,
        CompletedOn = record.CompletedOn,
        CompletedByMemberId = record.CompletedByMemberId ?? 0,
        CompletedByUserId = record.CompletedByUserId,
        CompletedByUsername = record.CompletedByUsername,
        PhotoRefs = HouseholdPhotoRefs.Deserialize(record.PhotoRefs),
        Cost = record.Cost,
        PurchaseLink = record.PurchaseLink,
        Note = record.Note,
        ConsumableId = record.ConsumableId,
        ConsumableQuantityDeducted = record.ConsumableQuantityDeducted,
        NeedsRestock = record.NeedsRestock,
        NewExpiryDate = record.NewExpiryDate,
        CreatedAt = record.CreatedAt
    };

    private static decimal? NormalizeCost(decimal? cost)
    {
        if (cost is null)
            return null;
        if (cost < 0)
            throw new BusinessException("费用不能为负", 400);
        if (cost > 999999999.99m)
            throw new BusinessException("费用超出范围", 400);
        return decimal.Round(cost.Value, 2, MidpointRounding.AwayFromZero);
    }

    private async Task RejectDuplicateSubmissionAsync(int itemId, string fingerprint, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-DuplicateCompletionWindowSeconds);
        var recent = await _db.HouseholdCompletionRecords.AsNoTracking()
            .Where(r => r.HouseholdItemId == itemId && r.CreatedAt >= cutoff)
            .OrderByDescending(r => r.Id)
            .Select(r => r.SubmissionFingerprint)
            .FirstOrDefaultAsync(ct);
        if (recent == fingerprint)
            throw new BusinessException("请勿重复提交", 409);
    }

    private async Task<HouseholdCompletionRecord?> FindByIdempotencyKeyAsync(
        int userId, int itemId, string key, CancellationToken ct) =>
        await _db.HouseholdCompletionRecords.AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.HouseholdItemId == itemId && r.IdempotencyUserId == userId && r.IdempotencyKey == key, ct);

    private async Task<CompleteHouseholdItemResult> ReplayOrRejectAsync(
        int householdId, HouseholdCompletionRecord record, string bodyHash, CancellationToken ct)
    {
        if (!string.Equals(record.RequestBodyHash, bodyHash, StringComparison.Ordinal))
            throw new BusinessException(IdempotencyBodyMismatchMessage, 422);
        return await ReplayAsync(householdId, record, ct);
    }

    private async Task<CompleteHouseholdItemResult> ReplayAsync(
        int householdId, HouseholdCompletionRecord record, CancellationToken ct)
    {
        var item = await LoadAsync(householdId, record.HouseholdItemId, tracking: false, ct);
        int? stockAfter = null;
        if (record.ConsumableId is int consumableId)
        {
            stockAfter = await _db.HouseholdConsumables.AsNoTracking()
                .Where(c => c.Id == consumableId && c.HouseholdId == householdId)
                .Select(c => (int?)c.CurrentStock)
                .FirstOrDefaultAsync(ct);
        }

        return new CompleteHouseholdItemResult
        {
            Item = await MapOneAsync(item, ct),
            Record = ToCompletionDto(record),
            ConsumableQuantityDeducted = record.ConsumableQuantityDeducted,
            ConsumableStockAfter = stockAfter,
            NeedsRestock = record.NeedsRestock
        };
    }

    private static string? NormalizeIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var trimmed = key.Trim();
        if (trimmed.Length > 100)
            throw new BusinessException("Idempotency-Key 最长 100 个字符", 400);
        return trimmed;
    }

    private static string SubmissionFingerprint(
        CompleteHouseholdItemRequest request,
        DateOnly completedOn,
        int memberId,
        decimal? cost,
        string? note,
        string? purchaseLink,
        IReadOnlyList<string> photos)
    {
        var raw = string.Join('\u001f',
            completedOn.ToString("yyyy-MM-dd"),
            memberId.ToString(),
            request.SkipConsumableDeduction ? "1" : "0",
            request.ConsumableQuantity?.ToString() ?? "",
            request.NewExpiryDate?.ToString("yyyy-MM-dd") ?? "",
            cost?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            note ?? "",
            purchaseLink ?? "",
            string.Join('\u001e', photos));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private readonly record struct Deduction(int? ConsumableId, int Actual, int? StockAfter, bool NeedsRestock);

    private sealed class CompletionGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount;
    }

    private static CompletionGate AcquireCompletionGate(int itemId)
    {
        lock (CompletionGateEviction)
        {
            var gate = CompletionGates.GetOrAdd(itemId, static _ => new CompletionGate());
            gate.RefCount++;
            return gate;
        }
    }

    private static void ReleaseCompletionGate(int itemId, CompletionGate gate, bool entered)
    {
        if (entered)
            gate.Semaphore.Release();

        lock (CompletionGateEviction)
        {
            gate.RefCount--;
            if (gate.RefCount == 0)
                CompletionGates.TryRemove(new KeyValuePair<int, CompletionGate>(itemId, gate));
        }
    }
}
