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

    public WelcomeController(IWelcomeGreetingService greetingService) => _greetingService = greetingService;

    [HttpGet("greeting")]
    public async Task<ActionResult<ApiResponse<WelcomeGreetingResponse>>> GetGreeting(CancellationToken ct)
    {
        var content = await _greetingService.GetGreetingAsync(ct);
        return Ok(ApiResponse<WelcomeGreetingResponse>.Ok(new WelcomeGreetingResponse(content)));
    }
}

public sealed record WelcomeGreetingResponse(string Content);
