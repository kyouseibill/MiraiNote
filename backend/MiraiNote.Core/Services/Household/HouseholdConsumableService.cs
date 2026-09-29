using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

public interface IHouseholdConsumableService
{
    Task<List<HouseholdConsumableDto>> ListAsync(int userId, CancellationToken ct = default);
    Task<HouseholdConsumableDto> GetAsync(int userId, int id, CancellationToken ct = default);
    Task<HouseholdConsumableDto> CreateAsync(int userId, SaveHouseholdConsumableRequest request, CancellationToken ct = default);
    Task<HouseholdConsumableDto> UpdateAsync(int userId, int id, SaveHouseholdConsumableRequest request, CancellationToken ct = default);
    Task DeleteAsync(int userId, int id, CancellationToken ct = default);
    Task<HouseholdConsumableDto> RestockAsync(int userId, int id, RestockHouseholdConsumableRequest request, CancellationToken ct = default);
}

public sealed class HouseholdConsumableService : IHouseholdConsumableService
{
    public const string LinkedItemsMessagePrefix = "仍有事项关联该耗材，无法删除：";

    private readonly MiraiNoteDbContext _db;
    private readonly IHouseholdAccessService _access;
    private readonly HouseholdAccessPolicy _policy;

    public HouseholdConsumableService(MiraiNoteDbContext db, IHouseholdAccessService access, HouseholdAccessPolicy policy)
    {
        _db = db;
        _access = access;
        _policy = policy;
    }

    public async Task<List<HouseholdConsumableDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var items = await _db.HouseholdConsumables.AsNoTracking()
            .Where(c => c.HouseholdId == ctx.Household.Id)
            .OrderBy(c => c.Name)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);
        return items.Select(ToDto).ToList();
    }

    public async Task<HouseholdConsumableDto> GetAsync(int userId, int id, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var entity = await LoadAsync(ctx.Household.Id, id, tracking: false, ct);
        return ToDto(entity);
    }

    public async Task<HouseholdConsumableDto> CreateAsync(int userId, SaveHouseholdConsumableRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var entity = new HouseholdConsumable { HouseholdId = ctx.Household.Id };
        Apply(entity, request, previousStock: 0);
        _db.HouseholdConsumables.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<HouseholdConsumableDto> UpdateAsync(int userId, int id, SaveHouseholdConsumableRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var entity = await LoadAsync(ctx.Household.Id, id, tracking: true, ct);
        ApplyFields(entity, request);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var entity = await LoadAsync(ctx.Household.Id, id, tracking: true, ct);
        _policy.EnsureCanDeleteConsumable(ctx.IsAdmin);
        var names = await _db.HouseholdItems.AsNoTracking()
            .Where(i => i.HouseholdId == ctx.Household.Id && i.ConsumableId == entity.Id)
            .OrderBy(i => i.Name)
            .ThenBy(i => i.Id)
            .Select(i => i.Name)
            .ToListAsync(ct);
        if (names.Count > 0)
            throw new BusinessException(LinkedItemsMessagePrefix + string.Join("、", names), 400);

        entity.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<HouseholdConsumableDto> RestockAsync(
        int userId, int id, RestockHouseholdConsumableRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        if (request.Quantity <= 0)
            throw new BusinessException("补货数量必须大于 0", 400);

        var entity = await LoadAsync(ctx.Household.Id, id, tracking: true, ct);
        if (entity.CurrentStock > int.MaxValue - request.Quantity)
            throw new BusinessException("库存超出范围", 400);

        entity.CurrentStock += request.Quantity;
        entity.LowStockReminderSent = false;
        await ClearConsumableRemindersAsync(entity.Id, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    private async Task ClearConsumableRemindersAsync(int consumableId, CancellationToken ct)
    {
        var logs = await _db.HouseholdConsumableReminders
            .Where(r => r.ConsumableId == consumableId)
            .ToListAsync(ct);
        foreach (var log in logs)
            log.IsDeleted = true;
    }

    private async Task<HouseholdConsumable> LoadAsync(int householdId, int id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? _db.HouseholdConsumables : _db.HouseholdConsumables.AsNoTracking();
        return await query.FirstOrDefaultAsync(c => c.Id == id && c.HouseholdId == householdId, ct)
            ?? throw new BusinessException("耗材不存在", 404);
    }

    private static void Apply(HouseholdConsumable entity, SaveHouseholdConsumableRequest request, int previousStock)
    {
        if (request.CurrentStock < 0)
            throw new BusinessException("库存不能为负", 400);

        ApplyFields(entity, request);
        entity.CurrentStock = request.CurrentStock;
        if (entity.CurrentStock > previousStock)
            entity.LowStockReminderSent = false;
    }

    /// <summary>更新不写库存。库存只通过补货和完成扣减变更。</summary>
    private static void ApplyFields(HouseholdConsumable entity, SaveHouseholdConsumableRequest request)
    {
        var threshold = request.RestockThreshold ?? HouseholdCycleRules.DefaultRestockThreshold;
        if (threshold < 0)
            throw new BusinessException("补货阈值不能为负", 400);

        entity.Name = HouseholdText.Require(request.Name, HouseholdFieldLimits.Name, "名称");
        entity.SpecModel = HouseholdText.Clean(request.SpecModel, HouseholdFieldLimits.ModelSpec, "规格型号");
        entity.RestockThreshold = threshold;
        entity.Unit = HouseholdText.Clean(request.Unit, HouseholdFieldLimits.Unit, "单位");
        entity.PurchaseLink = HouseholdText.CleanPurchaseLink(request.PurchaseLink);
        entity.Note = HouseholdText.Clean(request.Note, HouseholdFieldLimits.Note, "备注");
    }

    private static HouseholdConsumableDto ToDto(HouseholdConsumable entity) => new()
    {
        Id = entity.Id,
        HouseholdId = entity.HouseholdId,
        Name = entity.Name,
        SpecModel = entity.SpecModel,
        CurrentStock = entity.CurrentStock,
        RestockThreshold = entity.RestockThreshold,
        IsLowStock = entity.CurrentStock <= entity.RestockThreshold,
        LowStockReminderSent = entity.LowStockReminderSent,
        Unit = entity.Unit,
        PurchaseLink = entity.PurchaseLink,
        Note = entity.Note,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
