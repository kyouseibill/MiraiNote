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
    /// <summary>当前成员关系。没有家庭时返回 null，不创建。</summary>
    Task<HouseholdContext?> FindAsync(int userId, CancellationToken ct = default);

    /// <summary>有未过期、未撤回的待处理邀请。这种用户不能自动建家庭。</summary>
    Task<bool> HasActionableInvitationAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// 取得当前用户的家庭。还没有家庭、且没有待处理邀请时自动创建一个，并把该用户设为管理员。
    /// 有待处理邀请时不创建，调用方应先让用户接受或拒绝。
    /// </summary>
    Task<HouseholdContext> GetOrCreateAsync(int userId, CancellationToken ct = default);
}

public sealed class HouseholdAccessService : IHouseholdAccessService
{
    public const string PendingInvitationMessage = "请先处理家庭邀请";

    private readonly MiraiNoteDbContext _db;
    private readonly IHouseholdClock _clock;

    public HouseholdAccessService(MiraiNoteDbContext db, IHouseholdClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task<HouseholdContext?> FindAsync(int userId, CancellationToken ct = default) =>
        FindExistingAsync(userId, ct);

    public async Task<bool> HasActionableInvitationAsync(int userId, CancellationToken ct = default)
    {
        var now = UtcNow();
        return await _db.HouseholdInvitations.AsNoTracking().AnyAsync(i =>
            i.InviteeUserId == userId
            && i.Status == HouseholdInvitationStatus.Pending
            && i.ExpiresAt > now, ct);
    }

    public async Task<HouseholdContext> GetOrCreateAsync(int userId, CancellationToken ct = default)
    {
        if (userId <= 0)
            throw new BusinessException("未登录", 401);

        var existing = await FindExistingAsync(userId, ct);
        if (existing != null)
            return existing;

        if (await HasActionableInvitationAsync(userId, ct))
            throw new BusinessException(PendingInvitationMessage, 409);

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

    private async Task<HouseholdContext?> FindExistingAsync(int userId, CancellationToken ct)
    {
        var member = await _db.HouseholdMembers
            .Include(m => m.Household)
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);

        if (member?.Household == null)
            return null;

        return new HouseholdContext { Household = member.Household, Member = member };
    }

    private DateTime UtcNow() => DateTime.SpecifyKind(_clock.UtcNow.UtcDateTime, DateTimeKind.Utc);
}
