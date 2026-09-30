using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services.Household;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/household")]
public class HouseholdController : ControllerBase
{
    private readonly IHouseholdService _service;
    private readonly IHouseholdInvitationService _invitations;
    private readonly ICurrentUserService _currentUser;

    public HouseholdController(
        IHouseholdService service,
        IHouseholdInvitationService invitations,
        ICurrentUserService currentUser)
    {
        _service = service;
        _invitations = invitations;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<HouseholdDto>>> GetMine(CancellationToken ct)
    {
        var result = await _service.GetMineAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<HouseholdDto>.Ok(result));
    }

    [HttpGet("members")]
    public async Task<ActionResult<ApiResponse<List<HouseholdMemberDto>>>> ListMembers(CancellationToken ct)
    {
        var result = await _service.ListMembersAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<HouseholdMemberDto>>.Ok(result));
    }

    [HttpPost("members")]
    public async Task<ActionResult<ApiResponse<HouseholdMemberDto>>> AddMember(
        [FromBody] AddHouseholdMemberRequest request, CancellationToken ct)
    {
        var result = await _service.AddMemberAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<HouseholdMemberDto>.Ok(result, "已添加"));
    }

    [HttpPut("members/{memberId:int}/role")]
    public async Task<ActionResult<ApiResponse<HouseholdMemberDto>>> ChangeRole(
        int memberId, [FromBody] ChangeHouseholdMemberRoleRequest request, CancellationToken ct)
    {
        var result = await _service.ChangeRoleAsync(_currentUser.UserId, memberId, request, ct);
        return Ok(ApiResponse<HouseholdMemberDto>.Ok(result, "已更新"));
    }

    [HttpDelete("members/{memberId:int}")]
    public async Task<ActionResult<ApiResponse>> RemoveMember(int memberId, CancellationToken ct)
    {
        await _service.RemoveMemberAsync(_currentUser.UserId, memberId, ct);
        return Ok(ApiResponse.Ok("已移出家庭"));
    }

    [HttpPost("leave")]
    public async Task<ActionResult<ApiResponse>> Leave(CancellationToken ct)
    {
        await _service.LeaveAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse.Ok("已退出家庭"));
    }

    [HttpPost("invitations")]
    public async Task<ActionResult<ApiResponse<HouseholdInvitationDto>>> CreateInvitation(
        [FromBody] AddHouseholdMemberRequest request, CancellationToken ct)
    {
        var result = await _invitations.CreateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<HouseholdInvitationDto>.Ok(result, "已发出邀请"));
    }

    [HttpGet("invitations")]
    public async Task<ActionResult<ApiResponse<List<HouseholdInvitationDto>>>> ListOutgoing(CancellationToken ct)
    {
        var result = await _invitations.ListOutgoingAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<HouseholdInvitationDto>>.Ok(result));
    }

    [HttpGet("invitations/incoming")]
    public async Task<ActionResult<ApiResponse<List<HouseholdInvitationDto>>>> ListIncoming(CancellationToken ct)
    {
        var result = await _invitations.ListIncomingAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<HouseholdInvitationDto>>.Ok(result));
    }

    [HttpDelete("invitations/{id:int}")]
    public async Task<ActionResult<ApiResponse<HouseholdInvitationDto>>> RevokeInvitation(int id, CancellationToken ct)
    {
        var result = await _invitations.RevokeAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse<HouseholdInvitationDto>.Ok(result, "已撤回"));
    }

    [HttpPost("invitations/{id:int}/accept")]
    public async Task<ActionResult<ApiResponse<HouseholdMemberDto>>> AcceptInvitation(
        int id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await _invitations.AcceptAsync(_currentUser.UserId, id, idempotencyKey, ct);
        return Ok(ApiResponse<HouseholdMemberDto>.Ok(result, "已加入家庭"));
    }

    [HttpPost("invitations/{id:int}/reject")]
    public async Task<ActionResult<ApiResponse<HouseholdInvitationDto>>> RejectInvitation(int id, CancellationToken ct)
    {
        var result = await _invitations.RejectAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse<HouseholdInvitationDto>.Ok(result, "已拒绝"));
    }
}
