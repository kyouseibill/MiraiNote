using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services.Household;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/household")]
public class HouseholdChatController : ControllerBase
{
    private readonly IHouseholdChatService _chat;
    private readonly ICurrentUserService _currentUser;

    public HouseholdChatController(IHouseholdChatService chat, ICurrentUserService currentUser)
    {
        _chat = chat;
        _currentUser = currentUser;
    }

    [HttpGet("chat/drafts")]
    public async Task<ActionResult<ApiResponse<List<HouseholdChatInterpretationDto>>>> Drafts(
        [FromQuery] int sessionId,
        CancellationToken ct)
    {
        var result = await _chat.ListForSessionAsync(_currentUser.UserId, sessionId, ct);
        return Ok(ApiResponse<List<HouseholdChatInterpretationDto>>.Ok(result));
    }

    [HttpPost("chat/confirm")]
    public async Task<ActionResult<ApiResponse<CompleteHouseholdItemResult>>> Confirm(
        [FromBody] ConfirmHouseholdChatRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await _chat.ConfirmAsync(_currentUser.UserId, request, idempotencyKey, ct);
        return Ok(ApiResponse<CompleteHouseholdItemResult>.Ok(result, "已完成"));
    }
}
