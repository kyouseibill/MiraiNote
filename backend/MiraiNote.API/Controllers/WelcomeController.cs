using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/welcome")]
public sealed class WelcomeController : ControllerBase
{
    private readonly IWelcomeGreetingService _greetingService;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;

    public WelcomeController(
        IWelcomeGreetingService greetingService,
        ICurrentUserService currentUser,
        TimeProvider clock)
    {
        _greetingService = greetingService;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// 工作台欢迎语。日期用服务器时钟换算成 Asia/Shanghai 的今天，忽略客户端传入的 date。
    /// </summary>
    [HttpGet("greeting")]
    public async Task<ActionResult<ApiResponse<WelcomeGreetingResponse>>> GetGreeting(
        [FromQuery] string? date,
        CancellationToken ct)
    {
        _ = date;
        var greeting = await _greetingService.GetGreetingAsync(
            _currentUser.UserId, _clock.GetUtcNow(), ct);
        return Ok(ApiResponse<WelcomeGreetingResponse>.Ok(
            new WelcomeGreetingResponse(greeting.Content, greeting.FeatureNote)));
    }
}

public sealed record WelcomeGreetingResponse(string Content, string? FeatureNote);
