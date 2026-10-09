using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Welcome;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/welcome-phrases")]
public sealed class WelcomePhrasesAdminController : ControllerBase
{
    private readonly IWelcomePhraseAdminService _phrases;

    public WelcomePhrasesAdminController(IWelcomePhraseAdminService phrases)
    {
        _phrases = phrases;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WelcomePhraseDto>>>> List(
        [FromQuery] string? kind, CancellationToken ct)
    {
        var rows = await _phrases.ListAsync(kind, ct);
        return Ok(ApiResponse<IReadOnlyList<WelcomePhraseDto>>.Ok(rows));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WelcomePhraseDto>>> Create(
        [FromBody] WelcomePhraseWriteRequest request, CancellationToken ct)
    {
        var created = await _phrases.CreateAsync(request, ct);
        return Ok(ApiResponse<WelcomePhraseDto>.Ok(created, "已添加"));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<WelcomePhraseDto>>> Update(
        int id, [FromBody] WelcomePhraseWriteRequest request, CancellationToken ct)
    {
        var updated = await _phrases.UpdateAsync(id, request, ct);
        return Ok(ApiResponse<WelcomePhraseDto>.Ok(updated, "已保存"));
    }

    [HttpPatch("{id:int}/enabled")]
    [HttpPut("{id:int}/enabled")]
    public async Task<ActionResult<ApiResponse<WelcomePhraseDto>>> SetEnabled(
        int id, [FromBody] WelcomePhraseEnabledRequest request, CancellationToken ct)
    {
        var updated = await _phrases.SetEnabledAsync(id, request.IsEnabled, ct);
        return Ok(ApiResponse<WelcomePhraseDto>.Ok(updated, request.IsEnabled ? "已启用" : "已停用"));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _phrases.DeleteAsync(id, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }
}
