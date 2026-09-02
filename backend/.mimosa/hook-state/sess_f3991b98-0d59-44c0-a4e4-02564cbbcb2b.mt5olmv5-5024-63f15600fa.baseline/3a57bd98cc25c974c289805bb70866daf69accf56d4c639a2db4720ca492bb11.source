using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Agent;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/agent/memories")]
public class AgentMemoryController : ControllerBase
{
    private readonly IAgentMemoryService _service;
    private readonly ICurrentUserService _currentUser;

    public AgentMemoryController(IAgentMemoryService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<AgentMemoryDto>>>> GetMemories(
        [FromQuery] string? category = null, CancellationToken ct = default)
    {
        var result = await _service.GetMemoriesAsync(_currentUser.UserId, category, ct);
        return Ok(ApiResponse<List<AgentMemoryDto>>.Ok(result));
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<ApiResponse<AgentMemoryDto>>> GetByKey(string key, CancellationToken ct)
    {
        var result = await _service.GetByKeyAsync(_currentUser.UserId, key, ct);
        if (result == null) return NotFound(ApiResponse.Fail("记忆不存在"));
        return Ok(ApiResponse<AgentMemoryDto>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AgentMemoryDto>>> Create(
        [FromBody] CreateMemoryRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<AgentMemoryDto>.Ok(result, "记忆已保存"));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<AgentMemoryDto>>> Update(
        int id, [FromBody] UpdateMemoryRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(_currentUser.UserId, id, request, ct);
        return Ok(ApiResponse<AgentMemoryDto>.Ok(result, "记忆已更新"));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }

    [HttpDelete("key/{key}")]
    public async Task<ActionResult<ApiResponse>> DeleteByKey(string key, CancellationToken ct)
    {
        await _service.DeleteByKeyAsync(_currentUser.UserId, key, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }
}
