using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/auth/welcome-settings")]
public sealed class WelcomeSettingsController : ControllerBase
{
    private readonly IWelcomePlaceSettingsService _settings;
    private readonly ICurrentUserService _currentUser;

    public WelcomeSettingsController(IWelcomePlaceSettingsService settings, ICurrentUserService currentUser)
    {
        _settings = settings;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<WelcomeSettingsDto>>> Get(CancellationToken ct)
    {
        var result = await _settings.GetAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<WelcomeSettingsDto>.Ok(result));
    }

    [HttpPut]
    public async Task<ActionResult<ApiResponse<WelcomeSettingsDto>>> Update(
        [FromBody] UpdateWelcomeSettingsRequest request,
        CancellationToken ct)
    {
        var result = await _settings.UpdateAsync(_currentUser.UserId, request.Place, request.Nickname, ct);
        return Ok(ApiResponse<WelcomeSettingsDto>.Ok(result, "已保存"));
    }
}
