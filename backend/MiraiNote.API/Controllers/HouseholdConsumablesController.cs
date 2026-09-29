using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services.Household;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/household/consumables")]
public class HouseholdConsumablesController : ControllerBase
{
    private readonly IHouseholdConsumableService _service;
    private readonly ICurrentUserService _currentUser;

    public HouseholdConsumablesController(IHouseholdConsumableService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<HouseholdConsumableDto>>>> List(CancellationToken ct)
    {
        var result = await _service.ListAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<HouseholdConsumableDto>>.Ok(result));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<HouseholdConsumableDto>>> Get(int id, CancellationToken ct)
    {
        var result = await _service.GetAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse<HouseholdConsumableDto>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<HouseholdConsumableDto>>> Create(
        [FromBody] SaveHouseholdConsumableRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<HouseholdConsumableDto>.Ok(result, "创建成功"));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<HouseholdConsumableDto>>> Update(
        int id, [FromBody] SaveHouseholdConsumableRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(_currentUser.UserId, id, request, ct);
        return Ok(ApiResponse<HouseholdConsumableDto>.Ok(result, "更新成功"));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }

    [HttpPost("{id:int}/restock")]
    public async Task<ActionResult<ApiResponse<HouseholdConsumableDto>>> Restock(
        int id, [FromBody] RestockHouseholdConsumableRequest request, CancellationToken ct)
    {
        var result = await _service.RestockAsync(_currentUser.UserId, id, request, ct);
        return Ok(ApiResponse<HouseholdConsumableDto>.Ok(result, "已补货"));
    }
}
