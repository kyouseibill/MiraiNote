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
    Task<HouseholdMemberDto> AddMemberAsync(int userId, AddHouseholdMemberRequest request, CancellationToken ct = default);
    Task<HouseholdMemberDto> ChangeRoleAsync(int userId, int memberId, ChangeHouseholdMemberRoleRequest request, CancellationToken ct = default);
    Task RemoveMemberAsync(int userId, int memberId, CancellationToken ct = default);
}

public sealed class HouseholdService : IHouseholdService
{
    private readonly MiraiNoteDbContext _db;
    private readonly IHouseholdAccessService _access;

    public HouseholdService(MiraiNoteDbContext db, IHouseholdAccessService access)
    {
        _db = db;
        _access = access;
    }

    public async Task<HouseholdDto> GetMineAsync(int userId, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        return new HouseholdDto
        {
            Id = ctx.Household.Id,
            Name = ctx.Household.Name,
            MyMemberId = ctx.Member.Id,
            MyRole = ctx.Member.Role
        };
    }

    public async Task<List<HouseholdMemberDto>> ListMembersAsync(int userId, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        return await _db.HouseholdMembers.AsNoTracking()
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
    }

    public async Task<HouseholdMemberDto> AddMemberAsync(int userId, AddHouseholdMemberRequest request, CancellationToken ct = default)
    {
        var ctx = await _access.GetOrCreateAsync(userId, ct);
        EnsureAdmin(ctx);

        var identifier = request.UserIdentifier?.Trim();
        if (string.IsNullOrWhiteSpace(identifier))
            throw new BusinessException("请填写用户名或邮箱", 400);

        var role = request.Role ?? HouseholdRole.Member;
        if (!Enum.IsDefined(role))
            throw new BusinessException("角色无效", 400);

        var lowered = identifier.ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Username.ToLower() == lowered || u.Email.ToLower() == lowered, ct)
            ?? throw new BusinessException("用户不存在", 404);

        if (!user.IsActive)
            throw new BusinessException("该用户未启用", 400);

        var membership = await _db.HouseholdMembers
            .FirstOrDefaultAsync(m => m.UserId == user.Id, ct);
        if (membership != null)
        {
            throw new BusinessException(
                membership.HouseholdId == ctx.Household.Id ? "该用户已是家庭成员" : "该用户已属于其他家庭",
                400);
        }

        var member = new HouseholdMember
        {
            HouseholdId = ctx.Household.Id,
            UserId = user.Id,
            Role = role
        };
        _db.HouseholdMembers.Add(member);
        await _db.SaveChangesAsync(ct);

        return new HouseholdMemberDto
        {
            Id = member.Id,
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = member.Role
        };
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
        if (member.Role == HouseholdRole.Admin)
            await EnsureAnotherAdminAsync(ctx.Household.Id, member.Id, ct);

        var assigned = await _db.HouseholdItems
            .Where(i => i.HouseholdId == ctx.Household.Id && i.AssigneeMemberId == member.Id)
            .ToListAsync(ct);
        foreach (var item in assigned)
            item.AssigneeMemberId = null;

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
            throw new BusinessException("家庭至少需要一名管理员", 400);
    }

    private static void EnsureAdmin(HouseholdContext ctx)
    {
        if (!ctx.IsAdmin)
            throw new BusinessException("只有管理员可以管理家庭成员", 403);
    }
}
