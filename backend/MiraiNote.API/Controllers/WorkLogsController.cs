using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;
using MiraiNote.Shared.Dtos.WorkLogs;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/worklogs")]
public class WorkLogsController : ControllerBase
{
    private readonly IWorkLogService _service;
    private readonly ICurrentUserService _currentUser;

    public WorkLogsController(IWorkLogService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<WorkLogDto>>>> List(
        [FromQuery] WorkLogListQuery query, CancellationToken ct)
    {
        var result = await _service.GetListAsync(_currentUser.UserId, query, ct);
        return Ok(ApiResponse<PagedResult<WorkLogDto>>.Ok(result));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<WorkLogDto>>> Get(int id, CancellationToken ct)
    {
        var item = await _service.GetByIdAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse<WorkLogDto>.Ok(item));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<WorkLogDto>>> Create(
        [FromBody] CreateWorkLogRequest request, CancellationToken ct)
    {
        var created = await _service.CreateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<WorkLogDto>.Ok(created, "创建成功"));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<WorkLogDto>>> Update(
        int id, [FromBody] UpdateWorkLogRequest request, CancellationToken ct)
    {
        var updated = await _service.UpdateAsync(_currentUser.UserId, id, request, ct);
        return Ok(ApiResponse<WorkLogDto>.Ok(updated, "更新成功"));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }

    [HttpGet("categories")]
    public async Task<ActionResult<ApiResponse<List<string>>>> GetCategories(CancellationToken ct)
    {
        var list = await _service.GetCategoriesAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<string>>.Ok(list));
    }
}
