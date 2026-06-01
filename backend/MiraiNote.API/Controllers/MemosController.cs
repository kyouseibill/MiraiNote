using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;
using MiraiNote.Shared.Dtos.Memos;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/memos")]
public class MemosController : ControllerBase
{
    private readonly IMemoService _service;
    private readonly ICurrentUserService _currentUser;

    public MemosController(IMemoService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<MemoDto>>>> List(
        [FromQuery] MemoListQuery query, CancellationToken ct)
    {
        var result = await _service.GetListAsync(_currentUser.UserId, query, ct);
        return Ok(ApiResponse<PagedResult<MemoDto>>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<MemoDto>>> Create(
        [FromBody] CreateMemoRequest request, CancellationToken ct)
    {
        var created = await _service.CreateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<MemoDto>.Ok(created, "创建成功"));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<MemoDto>>> Update(
        int id, [FromBody] UpdateMemoRequest request, CancellationToken ct)
    {
        var updated = await _service.UpdateAsync(_currentUser.UserId, id, request, ct);
        return Ok(ApiResponse<MemoDto>.Ok(updated, "更新成功"));
    }

    /// <summary>状态切换（完成 / 置顶 / 归档）。</summary>
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<ApiResponse<MemoDto>>> PatchStatus(
        int id, [FromBody] PatchMemoStatusRequest request, CancellationToken ct)
    {
        var updated = await _service.PatchStatusAsync(_currentUser.UserId, id, request, ct);
        return Ok(ApiResponse<MemoDto>.Ok(updated));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }

    /// <summary>查询当前用户已到提醒时间、需弹窗且未确认的备忘。</summary>
    [HttpGet("due-popups")]
    public async Task<ActionResult<ApiResponse<List<MemoDto>>>> DuePopups(CancellationToken ct)
    {
        var list = await _service.GetDuePopupsAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<MemoDto>>.Ok(list));
    }

    /// <summary>用户在前端关闭/确认弹窗。</summary>
    [HttpPatch("{id:int}/acknowledge-popup")]
    public async Task<ActionResult<ApiResponse>> AcknowledgePopup(int id, CancellationToken ct)
    {
        await _service.AcknowledgePopupAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse.Ok());
    }
}
