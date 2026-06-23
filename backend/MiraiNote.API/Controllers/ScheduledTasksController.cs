using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.ScheduledTasks;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public class ScheduledTasksController : ControllerBase
{
    private readonly IScheduledTaskService _taskService;

    public ScheduledTasksController(IScheduledTaskService taskService)
    {
        _taskService = taskService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<ScheduledTaskListResponse>>> List(CancellationToken ct)
    {
        var tasks = await _taskService.GetAllAsync(GetUserId(), ct);
        return Ok(ApiResponse<ScheduledTaskListResponse>.Ok(new ScheduledTaskListResponse(
            tasks.Count, tasks)));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<ScheduledTaskDto>>> Get(int id, CancellationToken ct)
    {
        var task = await _taskService.GetByIdAsync(GetUserId(), id, ct);
        if (task == null) return NotFound(ApiResponse.Fail("任务不存在"));
        return Ok(ApiResponse<ScheduledTaskDto>.Ok(task));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ScheduledTaskDto>>> Create(
        [FromBody] CreateScheduledTaskRequest request, CancellationToken ct)
    {
        try
        {
            var task = await _taskService.CreateAsync(
                GetUserId(), request.Description, request.ExecuteAt, request.NotifyEmail, ct);
            return Ok(ApiResponse<ScheduledTaskDto>.Ok(task));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse>> Cancel(int id, CancellationToken ct)
    {
        try
        {
            await _taskService.MarkCancelledAsync(GetUserId(), id, ct);
            return Ok(ApiResponse.Ok("任务已取消"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse.Fail(ex.Message));
        }
    }

    private int GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? User.FindFirst("sub");
        return claim != null && int.TryParse(claim.Value, out var id) ? id : 0;
    }
}
