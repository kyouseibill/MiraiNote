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
    private readonly IWebHostEnvironment _env;

    public WelcomeController(
        IWelcomeGreetingService greetingService,
        ICurrentUserService currentUser,
        TimeProvider clock,
        IWebHostEnvironment env)
    {
        _greetingService = greetingService;
        _currentUser = currentUser;
        _clock = clock;
        _env = env;
    }

    /// <summary>
    /// 工作台欢迎语。日期用服务器时钟换算成 Asia/Shanghai 的今天，忽略客户端传入的 date。
    /// now 只在 Development / Test 生效，Production 忽略。
    /// </summary>
    [HttpGet("greeting")]
    public async Task<ActionResult<ApiResponse<WelcomeGreetingResponse>>> GetGreeting(
        [FromQuery] string? date,
        [FromQuery] string? now,
        CancellationToken ct)
    {
        _ = date;
        var moment = _clock.GetUtcNow();
        if (WelcomeGreetingService.AllowsWelcomeNowOverride(_env)
            && WelcomeGreetingService.TryParseWelcomeNow(now, out var overridden))
        {
            moment = overridden;
        }

        var greeting = await _greetingService.GetGreetingAsync(_currentUser.UserId, moment, ct);
        return Ok(ApiResponse<WelcomeGreetingResponse>.Ok(
            new WelcomeGreetingResponse(
                greeting.Content,
                greeting.FeatureNote,
                greeting.WeatherWarning,
                greeting.News.Select(item => new WelcomeNewsItemResponse(item.Title, item.Url)).ToArray(),
                greeting.DisplayName,
                greeting.DateLine,
                greeting.WeatherBrief,
                greeting.MemoSummary,
                greeting.GreetingLine,
                greeting.Poem == null
                    ? null
                    : new WelcomePoemResponse(greeting.Poem.Text, greeting.Poem.Author, greeting.Poem.Source))));
    }
}

public sealed record WelcomeGreetingResponse(
    string Content,
    string? FeatureNote,
    string? WeatherWarning,
    IReadOnlyList<WelcomeNewsItemResponse> News,
    string DisplayName,
    string DateLine,
    string? WeatherBrief,
    string? MemoSummary,
    string? GreetingLine,
    WelcomePoemResponse? Poem);

public sealed record WelcomePoemResponse(string Text, string? Author, string? Source);

public sealed record WelcomeNewsItemResponse(string Title, string Url);
