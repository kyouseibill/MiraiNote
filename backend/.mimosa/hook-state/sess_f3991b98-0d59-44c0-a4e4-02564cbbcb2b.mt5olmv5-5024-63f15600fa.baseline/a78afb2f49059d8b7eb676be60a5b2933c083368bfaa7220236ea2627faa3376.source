using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;
using MiraiNote.Shared.Dtos.LifeLogs;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/lifelogs")]
public class LifeLogsController : ControllerBase
{
    private readonly ILifeLogService _service;
    private readonly ICurrentUserService _currentUser;

    public LifeLogsController(ILifeLogService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<LifeLogDto>>>> List(
        [FromQuery] LifeLogListQuery query, CancellationToken ct)
    {
        var result = await _service.GetListAsync(_currentUser.UserId, query, ct);
        return Ok(ApiResponse<PagedResult<LifeLogDto>>.Ok(result));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<LifeLogDto>>> Get(int id, CancellationToken ct)
    {
        var item = await _service.GetByIdAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse<LifeLogDto>.Ok(item));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<LifeLogDto>>> Create(
        [FromBody] CreateLifeLogRequest request, CancellationToken ct)
    {
        var created = await _service.CreateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<LifeLogDto>.Ok(created, "创建成功"));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<LifeLogDto>>> Update(
        int id, [FromBody] UpdateLifeLogRequest request, CancellationToken ct)
    {
        var updated = await _service.UpdateAsync(_currentUser.UserId, id, request, ct);
        return Ok(ApiResponse<LifeLogDto>.Ok(updated, "更新成功"));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }
}
