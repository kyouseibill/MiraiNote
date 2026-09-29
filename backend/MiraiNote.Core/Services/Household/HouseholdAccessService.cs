using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;
using HouseholdEntity = MiraiNote.Data.Entities.Household;

namespace MiraiNote.Core.Services.Household;

public sealed class HouseholdContext
{
    public required HouseholdEntity Household { get; init; }
    public required HouseholdMember Member { get; init; }
    public bool IsAdmin => Member.Role == HouseholdRole.Admin;
}

public interface IHouseholdAccessService
{
    /// <summary>取得当前用户的家庭。还没有家庭时自动创建一个，并把该用户设为管理员。</summary>
    Task<HouseholdContext> GetOrCreateAsync(int userId, CancellationToken ct = default);
}

public sealed class HouseholdAccessService : IHouseholdAccessService
{
    private readonly MiraiNoteDbContext _db;

    public HouseholdAccessService(MiraiNoteDbContext db)
    {
        _db = db;
    }

    public async Task<HouseholdContext> GetOrCreateAsync(int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new BusinessException("未登录", 401);

        var existing = await FindAsync(userId, ct);
        if (existing != null)
            return existing;

        var household = new HouseholdEntity { Name = HouseholdEntity.DefaultName };
        var member = new HouseholdMember
        {
            Household = household,
            UserId = userId,
            Role = HouseholdRole.Admin
        };
        _db.Households.Add(household);
        _db.HouseholdMembers.Add(member);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.Entry(household).State = EntityState.Detached;
            _db.Entry(member).State = EntityState.Detached;
            var raced = await FindAsync(userId, ct);
            if (raced != null)
                return raced;
            throw;
        }

        return new HouseholdContext { Household = household, Member = member };
    }

    private async Task<HouseholdContext?> FindAsync(int userId, CancellationToken ct)
    {
        var member = await _db.HouseholdMembers
            .Include(m => m.Household)
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);

        if (member?.Household == null)
            return null;

        return new HouseholdContext { Household = member.Household, Member = member };
    }
}
