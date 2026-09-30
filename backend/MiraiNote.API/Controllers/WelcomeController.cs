using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.Household;
using MiraiNote.Shared.Common;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/welcome")]
public sealed class WelcomeController : ControllerBase
{
    private readonly IWelcomeGreetingService _greetingService;
    private readonly ICurrentUserService _currentUser;

    public WelcomeController(IWelcomeGreetingService greetingService, ICurrentUserService currentUser)
    {
        _greetingService = greetingService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// 首页欢迎语。日期固定为服务器的 Asia/Shanghai 今天，忽略客户端传入的 date。
    /// exclude 只给以后的「换一句」用：从文案池挑选且不调模型。正常首页不要传。
    /// </summary>
    [HttpGet("greeting")]
    public async Task<ActionResult<ApiResponse<WelcomeGreetingResponse>>> GetGreeting(
        [FromQuery] string? date,
        [FromQuery] string? exclude,
        CancellationToken ct)
    {
        _ = date;
        var localDate = ShanghaiClock.ToShanghaiDate(DateTimeOffset.UtcNow);
        var content = await _greetingService.GetGreetingAsync(
            _currentUser.UserId, localDate, exclude, ct);
        return Ok(ApiResponse<WelcomeGreetingResponse>.Ok(new WelcomeGreetingResponse(content)));
    }
}

public sealed record WelcomeGreetingResponse(string Content);
