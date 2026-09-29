using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services.Household;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/household")]
public class HouseholdItemsController : ControllerBase
{
    private readonly IHouseholdItemService _service;
    private readonly ICurrentUserService _currentUser;

    public HouseholdItemsController(IHouseholdItemService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("items")]
    public async Task<ActionResult<ApiResponse<List<HouseholdItemDto>>>> List(
        [FromQuery] HouseholdItemListQuery query, CancellationToken ct)
    {
        var result = await _service.ListAsync(_currentUser.UserId, query, ct);
        return Ok(ApiResponse<List<HouseholdItemDto>>.Ok(result));
    }

    [HttpGet("items/{id:int}")]
    public async Task<ActionResult<ApiResponse<HouseholdItemDto>>> Get(int id, CancellationToken ct)
    {
        var result = await _service.GetAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse<HouseholdItemDto>.Ok(result));
    }

    [HttpPost("items")]
    public async Task<ActionResult<ApiResponse<HouseholdItemDto>>> Create(
        [FromBody] CreateHouseholdItemRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<HouseholdItemDto>.Ok(result, "创建成功"));
    }

    [HttpPut("items/{id:int}")]
    public async Task<ActionResult<ApiResponse<HouseholdItemDto>>> Update(
        int id, [FromBody] UpdateHouseholdItemRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(_currentUser.UserId, id, request, ct);
        return Ok(ApiResponse<HouseholdItemDto>.Ok(result, "更新成功"));
    }

    [HttpPost("items/{id:int}/pause")]
    public async Task<ActionResult<ApiResponse<HouseholdItemDto>>> Pause(int id, CancellationToken ct)
    {
        var result = await _service.SetPausedAsync(_currentUser.UserId, id, true, ct);
        return Ok(ApiResponse<HouseholdItemDto>.Ok(result, "已暂停"));
    }

    [HttpPost("items/{id:int}/resume")]
    public async Task<ActionResult<ApiResponse<HouseholdItemDto>>> Resume(int id, CancellationToken ct)
    {
        var result = await _service.SetPausedAsync(_currentUser.UserId, id, false, ct);
        return Ok(ApiResponse<HouseholdItemDto>.Ok(result, "已恢复"));
    }

    [HttpDelete("items/{id:int}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }

    [HttpPost("items/{id:int}/complete")]
    public async Task<ActionResult<ApiResponse<CompleteHouseholdItemResult>>> Complete(
        int id,
        [FromBody] CompleteHouseholdItemRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await _service.CompleteAsync(_currentUser.UserId, id, request, idempotencyKey, ct);
        return Ok(ApiResponse<CompleteHouseholdItemResult>.Ok(result, "已完成"));
    }

    [HttpGet("items/{id:int}/history")]
    public async Task<ActionResult<ApiResponse<List<HouseholdCompletionDto>>>> History(int id, CancellationToken ct)
    {
        var result = await _service.HistoryAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse<List<HouseholdCompletionDto>>.Ok(result));
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<ApiResponse<HouseholdUpcomingDto>>> Upcoming(
        [FromQuery] HouseholdUpcomingQuery query, CancellationToken ct)
    {
        var result = await _service.UpcomingAsync(_currentUser.UserId, query, ct);
        return Ok(ApiResponse<HouseholdUpcomingDto>.Ok(result));
    }

    [HttpGet("templates")]
    public async Task<ActionResult<ApiResponse<List<HouseholdItemTemplateDto>>>> Templates(CancellationToken ct)
    {
        var result = await _service.ListTemplatesAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<HouseholdItemTemplateDto>>.Ok(result));
    }

    [HttpPost("items/from-template")]
    public async Task<ActionResult<ApiResponse<HouseholdItemDto>>> CreateFromTemplate(
        [FromBody] CreateHouseholdItemFromTemplateRequest request, CancellationToken ct)
    {
        var result = await _service.CreateFromTemplateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<HouseholdItemDto>.Ok(result, "创建成功"));
    }
}
