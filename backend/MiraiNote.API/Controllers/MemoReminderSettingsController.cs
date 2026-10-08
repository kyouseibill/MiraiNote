using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/auth/memo-reminder-settings")]
public class MemoReminderSettingsController : ControllerBase
{
    private readonly IMemoReminderSettingsService _settings;
    private readonly ICurrentUserService _currentUser;

    public MemoReminderSettingsController(IMemoReminderSettingsService settings, ICurrentUserService currentUser)
    {
        _settings = settings;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<MemoReminderSettingsDto>>> Get(CancellationToken ct)
    {
        var result = await _settings.GetAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<MemoReminderSettingsDto>.Ok(result));
    }

    [HttpPut]
    public async Task<ActionResult<ApiResponse<MemoReminderSettingsDto>>> Update(
        [FromBody] UpdateMemoReminderSettingsRequest request, CancellationToken ct)
    {
        var result = await _settings.UpdateBarkKeyAsync(_currentUser.UserId, request.BarkKey, ct);
        return Ok(ApiResponse<MemoReminderSettingsDto>.Ok(result, "已保存"));
    }
}
