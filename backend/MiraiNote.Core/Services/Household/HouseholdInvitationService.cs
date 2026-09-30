using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

public interface IHouseholdInvitationService
{
    Task<HouseholdInvitationDto> CreateAsync(int userId, AddHouseholdMemberRequest request, CancellationToken ct = default);
    Task<List<HouseholdInvitationDto>> ListOutgoingAsync(int userId, CancellationToken ct = default);
    Task<List<HouseholdInvitationDto>> ListIncomingAsync(int userId, CancellationToken ct = default);
    Task<HouseholdInvitationDto> RevokeAsync(int userId, int invitationId, CancellationToken ct = default);
    Task<HouseholdMemberDto> AcceptAsync(int userId, int invitationId, string? idempotencyKey, CancellationToken ct = default);
    Task<HouseholdInvitationDto> RejectAsync(int userId, int invitationId, CancellationToken ct = default);
}

public sealed class HouseholdInvitationService : IHouseholdInvitationService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    public const string NotFoundMessage = "邀请不存在";
    public const string ExpiredMessage = "邀请已过期";
    public const string HandledMessage = "邀请已处理";
    public const string AlreadyElsewhereMessage = "你已经属于其他家庭";

    private readonly MiraiNoteDbContext _db;
    private readonly IHouseholdAccessService _access;
    private readonly HouseholdCycleRules _rules;

    public HouseholdInvitationService(MiraiNoteDbContext db, IHouseholdAccessService access, HouseholdCycleRules rules)
    {
        _db = db;
        _access = access;
        _rules = rules;
    }

    public async Task<HouseholdInvitationDto> CreateAsync(int userId, AddHouseholdMemberRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        if (!ctx.IsAdmin)
            throw new BusinessException("只有管理员可以管理家庭成员", 403);

        var identifier = request.UserIdentifier?.Trim();
        if (string.IsNullOrWhiteSpace(identifier))
            throw new BusinessException("请填写用户名或邮箱", 400);

        var role = request.Role ?? HouseholdRole.Member;
        if (!Enum.IsDefined(role))
            throw new BusinessException("角色无效", 400);

        var lowered = identifier.ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Username.ToLower() == lowered || u.Email.ToLower() == lowered, ct);
        if (user == null || !user.IsActive)
            throw new BusinessException(HouseholdService.AddMemberRejectedMessage, 400);

        var membership = await _db.HouseholdMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == user.Id, ct);
        if (membership != null)
        {
            throw new BusinessException(
                membership.HouseholdId == ctx.Household.Id ? "该用户已是家庭成员" : HouseholdService.AddMemberRejectedMessage,
                400);
        }

        var expires = UtcNow().Add(Lifetime);
        var pending = await _db.HouseholdInvitations
            .FirstOrDefaultAsync(i =>
                i.HouseholdId == ctx.Household.Id
                && i.InviteeUserId == user.Id
                && i.Status == HouseholdInvitationStatus.Pending, ct);
        if (pending != null)
        {
            pending.ExpiresAt = expires;
            pending.Role = role;
            pending.InviterUserId = userId;
            await _db.SaveChangesAsync(ct);
            return await MapAsync(pending, includeEmail: true, ct);
        }

        var invitation = new HouseholdInvitation
        {
            HouseholdId = ctx.Household.Id,
            InviterUserId = userId,
            InviteeUserId = user.Id,
            Role = role,
            Status = HouseholdInvitationStatus.Pending,
            ExpiresAt = expires
        };
        _db.HouseholdInvitations.Add(invitation);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.Entry(invitation).State = EntityState.Detached;
            var raced = await _db.HouseholdInvitations.FirstOrDefaultAsync(i =>
                i.HouseholdId == ctx.Household.Id
                && i.InviteeUserId == user.Id
                && i.Status == HouseholdInvitationStatus.Pending, ct);
            if (raced == null)
                throw;
            raced.ExpiresAt = expires;
            raced.Role = role;
            raced.InviterUserId = userId;
            await _db.SaveChangesAsync(ct);
            return await MapAsync(raced, includeEmail: true, ct);
        }

        return await MapAsync(invitation, includeEmail: true, ct);
    }

    public async Task<List<HouseholdInvitationDto>> ListOutgoingAsync(int userId, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        if (!ctx.IsAdmin)
            throw new BusinessException("只有管理员可以管理家庭成员", 403);

        var rows = await _db.HouseholdInvitations.AsNoTracking()
            .Where(i => i.HouseholdId == ctx.Household.Id && i.Status == HouseholdInvitationStatus.Pending)
            .OrderBy(i => i.ExpiresAt)
            .ThenBy(i => i.Id)
            .ToListAsync(ct);
        var result = new List<HouseholdInvitationDto>();
        foreach (var row in rows)
            result.Add(await MapAsync(row, includeEmail: true, ct));
        return result;
    }

    public async Task<List<HouseholdInvitationDto>> ListIncomingAsync(int userId, CancellationToken ct = default)
    {
        var now = UtcNow();
        var rows = await _db.HouseholdInvitations.AsNoTracking()
            .Where(i => i.InviteeUserId == userId
                && i.Status == HouseholdInvitationStatus.Pending
                && i.ExpiresAt > now)
            .OrderBy(i => i.ExpiresAt)
            .ThenBy(i => i.Id)
            .ToListAsync(ct);
        var result = new List<HouseholdInvitationDto>();
        foreach (var row in rows)
            result.Add(await MapAsync(row, includeEmail: false, ct));
        return result;
    }

    public async Task<HouseholdInvitationDto> RevokeAsync(int userId, int invitationId, CancellationToken ct = default)
    {
        var invitation = await _db.HouseholdInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId, ct);
        if (invitation == null || !await IsHouseholdAdminAsync(userId, invitation.HouseholdId, ct))
            throw new BusinessException(NotFoundMessage, 404);

        if (invitation.Status == HouseholdInvitationStatus.Revoked)
            return await MapAsync(invitation, includeEmail: true, ct);
        if (invitation.Status != HouseholdInvitationStatus.Pending)
            throw new BusinessException(HandledMessage, 400);

        invitation.Status = HouseholdInvitationStatus.Revoked;
        await _db.SaveChangesAsync(ct);
        return await MapAsync(invitation, includeEmail: true, ct);
    }

    public async Task<HouseholdMemberDto> AcceptAsync(
        int userId, int invitationId, string? idempotencyKey, CancellationToken ct = default)
    {
        if (idempotencyKey is { Length: > 100 })
            throw new BusinessException("Idempotency-Key 过长", 400);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            return await AcceptCoreAsync(userId, invitationId, ct);
        });
    }

    public async Task<HouseholdInvitationDto> RejectAsync(int userId, int invitationId, CancellationToken ct = default)
    {
        var invitation = await _db.HouseholdInvitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.InviteeUserId == userId, ct);
        if (invitation == null)
            throw new BusinessException(NotFoundMessage, 404);
        if (invitation.Status == HouseholdInvitationStatus.Rejected)
            return await MapAsync(invitation, includeEmail: false, ct);
        if (invitation.Status != HouseholdInvitationStatus.Pending)
            throw new BusinessException(HandledMessage, 400);
        if (invitation.ExpiresAt <= UtcNow())
            throw new BusinessException(ExpiredMessage, 400);

        var now = UtcNow();
        var updated = await _db.HouseholdInvitations
            .Where(i => i.Id == invitationId
                && i.InviteeUserId == userId
                && i.Status == HouseholdInvitationStatus.Pending
                && i.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.Status, HouseholdInvitationStatus.Rejected), ct);
        if (updated == 0)
        {
            var again = await _db.HouseholdInvitations.AsNoTracking()
                .FirstAsync(i => i.Id == invitationId && i.InviteeUserId == userId, ct);
            if (again.Status == HouseholdInvitationStatus.Rejected)
                return await MapAsync(again, includeEmail: false, ct);
            throw new BusinessException(again.ExpiresAt <= UtcNow() ? ExpiredMessage : HandledMessage, 400);
        }

        invitation.Status = HouseholdInvitationStatus.Rejected;
        return await MapAsync(invitation, includeEmail: false, ct);
    }

    private async Task<HouseholdMemberDto> AcceptCoreAsync(int userId, int invitationId, CancellationToken ct)
    {
        var now = UtcNow();
        var invitation = await _db.HouseholdInvitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.InviteeUserId == userId, ct);
        if (invitation == null)
            throw new BusinessException(NotFoundMessage, 404);

        if (invitation.Status == HouseholdInvitationStatus.Accepted)
            return await MapAcceptedMemberAsync(invitation, ct);
        if (invitation.Status != HouseholdInvitationStatus.Pending)
            throw new BusinessException(HandledMessage, 400);
        if (invitation.ExpiresAt <= now)
            throw new BusinessException(ExpiredMessage, 400);

        var membership = await _db.HouseholdMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);
        if (membership != null
            && membership.HouseholdId != invitation.HouseholdId
            && !await IsDisposableEmptyHouseholdAsync(membership.HouseholdId, userId, ct))
            throw new BusinessException(AlreadyElsewhereMessage, 400);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.HouseholdInvitations
            .Where(i => i.Id == invitationId
                && i.InviteeUserId == userId
                && i.Status == HouseholdInvitationStatus.Pending
                && i.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.Status, HouseholdInvitationStatus.Accepted), ct);
        if (updated == 0)
        {
            await tx.RollbackAsync(ct);
            var again = await _db.HouseholdInvitations.AsNoTracking()
                .FirstAsync(i => i.Id == invitationId && i.InviteeUserId == userId, ct);
            if (again.Status == HouseholdInvitationStatus.Accepted)
                return await MapAcceptedMemberAsync(again, ct);
            throw new BusinessException(again.ExpiresAt <= UtcNow() ? ExpiredMessage : HandledMessage, 400);
        }

        if (membership != null && membership.HouseholdId != invitation.HouseholdId)
        {
            if (!await IsDisposableEmptyHouseholdAsync(membership.HouseholdId, userId, ct))
            {
                await tx.RollbackAsync(ct);
                throw new BusinessException(AlreadyElsewhereMessage, 400);
            }

            await DissolveEmptyHouseholdAsync(membership.Id, membership.HouseholdId, userId, ct);
        }

        if (membership == null || membership.HouseholdId != invitation.HouseholdId)
        {
            _db.HouseholdMembers.Add(new HouseholdMember
            {
                HouseholdId = invitation.HouseholdId,
                UserId = userId,
                Role = invitation.Role
            });
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(ct);
                throw new BusinessException(AlreadyElsewhereMessage, 400);
            }
        }

        await tx.CommitAsync(ct);
        return await MapAcceptedMemberAsync(invitation, ct);
    }

    /// <summary>
    /// 只有本人一名成员，且从未有过事项、耗材或完成记录（含已软删）。这种空家庭可以在接受邀请时解散。
    /// </summary>
    private async Task<bool> IsDisposableEmptyHouseholdAsync(int householdId, int userId, CancellationToken ct)
    {
        var otherMember = await _db.HouseholdMembers.AnyAsync(m =>
            m.HouseholdId == householdId && m.UserId != userId, ct);
        if (otherMember)
            return false;

        if (await _db.HouseholdItems.IgnoreQueryFilters().AnyAsync(i => i.HouseholdId == householdId, ct))
            return false;
        if (await _db.HouseholdConsumables.IgnoreQueryFilters().AnyAsync(c => c.HouseholdId == householdId, ct))
            return false;

        var itemIds = _db.HouseholdItems.IgnoreQueryFilters()
            .Where(i => i.HouseholdId == householdId)
            .Select(i => i.Id);
        return !await _db.HouseholdCompletionRecords.IgnoreQueryFilters()
            .AnyAsync(r => itemIds.Contains(r.HouseholdItemId), ct);
    }

    private async Task DissolveEmptyHouseholdAsync(int memberId, int householdId, int userId, CancellationToken ct)
    {
        var member = await _db.HouseholdMembers.FirstAsync(m => m.Id == memberId, ct);
        var household = await _db.Households.FirstAsync(h => h.Id == householdId, ct);
        var settings = await _db.HouseholdNotificationSettings
            .Where(s => s.MemberId == memberId)
            .ToListAsync(ct);
        var drafts = await _db.HouseholdChatDrafts
            .Where(d => d.UserId == userId && d.HouseholdId == householdId)
            .ToListAsync(ct);
        foreach (var setting in settings)
            setting.IsDeleted = true;
        foreach (var draft in drafts)
            draft.IsDeleted = true;
        member.IsDeleted = true;
        household.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<bool> IsHouseholdAdminAsync(int userId, int householdId, CancellationToken ct)
    {
        return await _db.HouseholdMembers.AsNoTracking().AnyAsync(m =>
            m.UserId == userId && m.HouseholdId == householdId && m.Role == HouseholdRole.Admin, ct);
    }

    private async Task<HouseholdMemberDto> MapAcceptedMemberAsync(HouseholdInvitation invitation, CancellationToken ct)
    {
        var member = await _db.HouseholdMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == invitation.InviteeUserId && m.HouseholdId == invitation.HouseholdId, ct)
            ?? throw new BusinessException(HandledMessage, 400);
        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == member.UserId, ct);
        return new HouseholdMemberDto
        {
            Id = member.Id,
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = member.Role
        };
    }

    private async Task<HouseholdInvitationDto> MapAsync(HouseholdInvitation invitation, bool includeEmail, CancellationToken ct)
    {
        var householdName = await _db.Households.AsNoTracking()
            .Where(h => h.Id == invitation.HouseholdId)
            .Select(h => h.Name)
            .FirstAsync(ct);
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.Id == invitation.InviteeUserId || u.Id == invitation.InviterUserId)
            .Select(u => new { u.Id, u.Username, u.Email })
            .ToListAsync(ct);
        var invitee = users.First(u => u.Id == invitation.InviteeUserId);
        var inviter = users.FirstOrDefault(u => u.Id == invitation.InviterUserId);
        var expires = DateTime.SpecifyKind(invitation.ExpiresAt, DateTimeKind.Utc);
        return new HouseholdInvitationDto
        {
            Id = invitation.Id,
            HouseholdId = invitation.HouseholdId,
            HouseholdName = householdName,
            InviteeUserId = invitation.InviteeUserId,
            InviteeUsername = invitee.Username,
            InviteeEmail = includeEmail ? invitee.Email : null,
            InviterUsername = inviter?.Username ?? "",
            Role = invitation.Role,
            Status = invitation.Status,
            ExpiresAt = new DateTimeOffset(expires),
            IsExpired = expires <= UtcNow()
        };
    }

    private DateTime UtcNow() => DateTime.SpecifyKind(_rules.UtcNow.UtcDateTime, DateTimeKind.Utc);
}
