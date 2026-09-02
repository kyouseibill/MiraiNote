using System.Globalization;
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

    public WelcomeController(IWelcomeGreetingService greetingService, ICurrentUserService currentUser)
    {
        _greetingService = greetingService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// 首页欢迎语。可选 date=yyyy-MM-dd 表示用户本地日期；省略则用服务器本地日期。
    /// </summary>
    [HttpGet("greeting")]
    public async Task<ActionResult<ApiResponse<WelcomeGreetingResponse>>> GetGreeting(
        [FromQuery] string? date,
        CancellationToken ct)
    {
        var localDate = ParseLocalDateOrToday(date);
        var content = await _greetingService.GetGreetingAsync(_currentUser.UserId, localDate, ct);
        return Ok(ApiResponse<WelcomeGreetingResponse>.Ok(new WelcomeGreetingResponse(content)));
    }

    private static DateOnly ParseLocalDateOrToday(string? date)
    {
        if (!string.IsNullOrWhiteSpace(date)
            && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;
        return DateOnly.FromDateTime(DateTime.Now);
    }
}

public sealed record WelcomeGreetingResponse(string Content);
