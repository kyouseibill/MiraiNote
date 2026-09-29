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
    private readonly ICurrentUserService _currentUser;

    public HouseholdController(IHouseholdService service, ICurrentUserService currentUser)
    {
        _service = service;
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
}
