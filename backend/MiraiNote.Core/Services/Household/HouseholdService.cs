using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

public interface IHouseholdService
{
    Task<HouseholdDto> GetMineAsync(int userId, CancellationToken ct = default);
    Task<List<HouseholdMemberDto>> ListMembersAsync(int userId, CancellationToken ct = default);
    Task<HouseholdMemberDto> ChangeRoleAsync(int userId, int memberId, ChangeHouseholdMemberRoleRequest request, CancellationToken ct = default);
    Task RemoveMemberAsync(int userId, int memberId, CancellationToken ct = default);
    Task LeaveAsync(int userId, CancellationToken ct = default);
}

public sealed class HouseholdService : IHouseholdService
{
    /// <summary>
    /// 「用户不存在」和「已属于其他家庭」共用这一句，避免探测账号是否存在。
    /// 加入家庭只走 <see cref="HouseholdInvitationService"/>，对方确认后才成为成员。
    /// </summary>
    public const string AddMemberRejectedMessage = "邀请未能发出，请确认对方账号";
    public const string LastAdminMessage = "家庭至少需要一名管理员";

    private readonly MiraiNoteDbContext _db;
    private readonly IHouseholdAccessService _access;

    public HouseholdService(MiraiNoteDbContext db, IHouseholdAccessService access)
    {
        _db = db;
        _access = access;
    }

    public async Task<HouseholdDto> GetMineAsync(int userId, CancellationToken ct = default)
    {
        var ctx = await _access.FindAsync(userId, ct);
        if (ctx == null && await _access.HasActionableInvitationAsync(userId, ct))
        {
            return new HouseholdDto
            {
                HasHousehold = false,
                HasPendingInvitations = true,
                MyRole = HouseholdRole.Member
            };
        }

        ctx ??= await _access.GetOrCreateAsync(userId, ct);
        return new HouseholdDto
        {
            Id = ctx.Household.Id,
            Name = ctx.Household.Name,
            MyMemberId = ctx.Member.Id,
            MyRole = ctx.Member.Role,
            HasHousehold = true,
            HasPendingInvitations = await _access.HasActionableInvitationAsync(userId, ct)
        };
    }

    public async Task<List<HouseholdMemberDto>> ListMembersAsync(int userId, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var members = await _db.HouseholdMembers.AsNoTracking()
            .Where(m => m.HouseholdId == ctx.Household.Id)
            .Join(
                _db.Users.AsNoTracking(),
                m => m.UserId,
                u => u.Id,
                (m, u) => new HouseholdMemberDto
                {
                    Id = m.Id,
                    UserId = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    Role = m.Role
                })
            .OrderBy(m => m.Role)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);
        if (!ctx.IsAdmin)
        {
            foreach (var member in members)
                member.Email = null;
        }

        return members;
    }

    public async Task<HouseholdMemberDto> ChangeRoleAsync(
        int userId, int memberId, ChangeHouseholdMemberRoleRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        EnsureAdmin(ctx);
        if (!Enum.IsDefined(request.Role))
            throw new BusinessException("角色无效", 400);

        var member = await LoadMemberAsync(ctx.Household.Id, memberId, ct);
        if (member.Role == HouseholdRole.Admin && request.Role != HouseholdRole.Admin)
            await EnsureAnotherAdminAsync(ctx.Household.Id, member.Id, ct);

        member.Role = request.Role;
        await _db.SaveChangesAsync(ct);
        return await MapMemberAsync(member, ct);
    }

    public async Task RemoveMemberAsync(int userId, int memberId, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        EnsureAdmin(ctx);

        var member = await LoadMemberAsync(ctx.Household.Id, memberId, ct);
        await DetachMemberAsync(ctx.Household.Id, member, ct);
    }

    public async Task LeaveAsync(int userId, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        var member = await LoadMemberAsync(ctx.Household.Id, ctx.Member.Id, ct);
        await DetachMemberAsync(ctx.Household.Id, member, ct);
    }

    private async Task DetachMemberAsync(int householdId, HouseholdMember member, CancellationToken ct)
    {
        if (member.Role == HouseholdRole.Admin)
            await EnsureAnotherAdminAsync(householdId, member.Id, ct);

        var assigned = await _db.HouseholdItems
            .Where(i => i.HouseholdId == householdId && i.AssigneeMemberId == member.Id)
            .ToListAsync(ct);
        foreach (var item in assigned)
            item.AssigneeMemberId = null;

        var drafts = await _db.HouseholdChatDrafts
            .Where(d => d.UserId == member.UserId && d.HouseholdId == householdId && d.IdempotencyKey == null)
            .ToListAsync(ct);
        foreach (var draft in drafts)
            draft.IsDeleted = true;

        var settings = await _db.HouseholdNotificationSettings
            .Where(s => s.MemberId == member.Id)
            .ToListAsync(ct);
        foreach (var setting in settings)
            setting.IsDeleted = true;

        member.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<HouseholdMember> LoadMemberAsync(int householdId, int memberId, CancellationToken ct)
    {
        return await _db.HouseholdMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.HouseholdId == householdId, ct)
            ?? throw new BusinessException("家庭成员不存在", 404);
    }

    private async Task<HouseholdMemberDto> MapMemberAsync(HouseholdMember member, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == member.UserId, ct)
            ?? throw new BusinessException("用户不存在", 404);
        return new HouseholdMemberDto
        {
            Id = member.Id,
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = member.Role
        };
    }

    private async Task EnsureAnotherAdminAsync(int householdId, int exceptMemberId, CancellationToken ct)
    {
        var otherAdmins = await _db.HouseholdMembers.CountAsync(m =>
            m.HouseholdId == householdId && m.Role == HouseholdRole.Admin && m.Id != exceptMemberId, ct);
        if (otherAdmins == 0)
            throw new BusinessException(LastAdminMessage, 400);
    }

    private static void EnsureAdmin(HouseholdContext ctx)
    {
        if (!ctx.IsAdmin)
            throw new BusinessException("只有管理员可以管理家庭成员", 403);
    }
}
