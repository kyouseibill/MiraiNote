using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services.Household;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.API.Controllers;

/// <summary>
/// 测试时钟。功能关闭（默认，或 Production）时直接 404，不先要求登录。
/// 功能打开时只有系统管理员（User.IsAdmin）可以读、拨、复位。
/// </summary>
[ApiController]
[Route("api/v1/household/test-clock")]
public class HouseholdTestClockController : ControllerBase
{
    private readonly IHouseholdTestClockService _clock;
    private readonly ICurrentUserService _currentUser;

    public HouseholdTestClockController(IHouseholdTestClockService clock, ICurrentUserService currentUser)
    {
        _clock = clock;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<HouseholdTestClockDto>>> Get(CancellationToken ct)
    {
        var result = await _clock.GetAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<HouseholdTestClockDto>.Ok(result));
    }

    [HttpPut]
    public async Task<ActionResult<ApiResponse<HouseholdTestClockDto>>> Set(
        [FromBody] SetHouseholdTestClockRequest request, CancellationToken ct)
    {
        var result = await _clock.SetAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<HouseholdTestClockDto>.Ok(result, "已调整测试时钟"));
    }

    [HttpDelete]
    public async Task<ActionResult<ApiResponse<HouseholdTestClockDto>>> Reset(CancellationToken ct)
    {
        var result = await _clock.ResetAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<HouseholdTestClockDto>.Ok(result, "已恢复系统时钟"));
    }
}
