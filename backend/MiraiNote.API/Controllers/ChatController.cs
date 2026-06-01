using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Chat;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/chat")]
public class ChatController : ControllerBase
{
    private readonly IChatService _service;
    private readonly ICurrentUserService _currentUser;

    public ChatController(IChatService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<ApiResponse<List<ChatSessionDto>>>> GetSessions(CancellationToken ct)
    {
        var result = await _service.GetSessionsAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<ChatSessionDto>>.Ok(result));
    }

    [HttpGet("sessions/{sessionId:int}")]
    public async Task<ActionResult<ApiResponse<ChatSessionDetailDto>>> GetSession(int sessionId, CancellationToken ct)
    {
        var result = await _service.GetSessionAsync(_currentUser.UserId, sessionId, ct);
        return Ok(ApiResponse<ChatSessionDetailDto>.Ok(result));
    }

    [HttpPost("sessions")]
    public async Task<ActionResult<ApiResponse<ChatSessionDto>>> CreateSession(
        [FromBody] CreateSessionRequest request, CancellationToken ct)
    {
        var result = await _service.CreateSessionAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<ChatSessionDto>.Ok(result, "对话已创建"));
    }

    [HttpPut("sessions/{sessionId:int}")]
    public async Task<ActionResult<ApiResponse<ChatSessionDto>>> UpdateTitle(
        int sessionId, [FromBody] UpdateSessionTitleRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateSessionTitleAsync(_currentUser.UserId, sessionId, request, ct);
        return Ok(ApiResponse<ChatSessionDto>.Ok(result, "标题已更新"));
    }

    [HttpDelete("sessions/{sessionId:int}")]
    public async Task<ActionResult<ApiResponse>> DeleteSession(int sessionId, CancellationToken ct)
    {
        await _service.DeleteSessionAsync(_currentUser.UserId, sessionId, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }

    [HttpPost("sessions/{sessionId:int}/messages")]
    public async Task<ActionResult<ApiResponse<ChatMessageDto>>> SendMessage(
        int sessionId, [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        var result = await _service.SendMessageAsync(_currentUser.UserId, sessionId, request, ct);
        return Ok(ApiResponse<ChatMessageDto>.Ok(result));
    }
}
