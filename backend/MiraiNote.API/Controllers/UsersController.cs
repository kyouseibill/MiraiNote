using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IUserAdminService _userAdmin;

    public UsersController(IUserAdminService userAdmin)
    {
        _userAdmin = userAdmin;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<UserListItemDto>>>> List(
        [FromQuery] UserListQuery query, CancellationToken ct)
    {
        var result = await _userAdmin.GetUsersAsync(query, ct);
        return Ok(ApiResponse<PagedResult<UserListItemDto>>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<UserListItemDto>>> Create(
        [FromBody] AdminCreateUserRequest request, CancellationToken ct)
    {
        var created = await _userAdmin.CreateUserAsync(request, ct);
        return Ok(ApiResponse<UserListItemDto>.Ok(created, "用户已创建，初始密码已通过邮件发送"));
    }

    [HttpPut("{id:int}/status")]
    public async Task<ActionResult<ApiResponse>> UpdateStatus(
        int id, [FromBody] UpdateUserStatusRequest request, CancellationToken ct)
    {
        await _userAdmin.UpdateStatusAsync(id, request.IsActive, ct);
        return Ok(ApiResponse.Ok(request.IsActive ? "账户已启用" : "账户已禁用"));
    }
}
