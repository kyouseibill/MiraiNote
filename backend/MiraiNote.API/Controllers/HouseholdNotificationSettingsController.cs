using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services.Household;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/household/notification-settings")]
public class HouseholdNotificationSettingsController : ControllerBase
{
    private readonly IHouseholdNotificationSettingsService _service;
    private readonly ICurrentUserService _currentUser;

    public HouseholdNotificationSettingsController(
        IHouseholdNotificationSettingsService service,
        ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<HouseholdNotificationSettingsDto>>> Get(CancellationToken ct)
    {
        var result = await _service.GetAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<HouseholdNotificationSettingsDto>.Ok(result));
    }

    [HttpPut]
    public async Task<ActionResult<ApiResponse<HouseholdNotificationSettingsDto>>> Update(
        [FromBody] UpdateHouseholdNotificationSettingsRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<HouseholdNotificationSettingsDto>.Ok(result, "已保存"));
    }

    [HttpPost("test-bark")]
    public async Task<ActionResult<ApiResponse>> TestBark(
        [FromBody] TestHouseholdBarkRequest? request, CancellationToken ct)
    {
        await _service.SendBarkTestAsync(_currentUser.UserId, request?.BarkAddress, ct);
        return Ok(ApiResponse.Ok("测试通知已发送"));
    }

    [HttpPost("test-email")]
    public async Task<ActionResult<ApiResponse>> TestEmail(
        [FromBody] TestHouseholdEmailRequest? request, CancellationToken ct)
    {
        await _service.SendEmailTestAsync(_currentUser.UserId, request?.Email, ct);
        return Ok(ApiResponse.Ok("测试通知已发送"));
    }
}
