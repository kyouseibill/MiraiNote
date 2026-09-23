using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/skills")]
public sealed class SkillsController : ControllerBase
{
    private readonly IFileSkillService _skills;
    private readonly ICurrentUserService _currentUser;

    public SkillsController(IFileSkillService skills, ICurrentUserService currentUser)
    {
        _skills = skills;
        _currentUser = currentUser;
    }

    [HttpGet]
    public ActionResult<ApiResponse<List<SkillSummaryDto>>> List() =>
        Ok(ApiResponse<List<SkillSummaryDto>>.Ok(
            _skills.List(_currentUser.UserId).Select(Summary).ToList()));

    [HttpGet("{name}")]
    public ActionResult<ApiResponse<SkillDocument>> Get(string name) =>
        Ok(ApiResponse<SkillDocument>.Ok(_skills.Get(_currentUser.UserId, name)));

    [HttpPost]
    public ActionResult<ApiResponse<SkillDocument>> Create([FromBody] SaveSkillRequest request) =>
        Ok(ApiResponse<SkillDocument>.Ok(
            _skills.Create(_currentUser.UserId, request.Name, request.Markdown, request.AllowImplicitInvocation),
            "Skill 已创建"));

    [HttpPut("{name}")]
    public ActionResult<ApiResponse<SkillDocument>> Update(string name, [FromBody] SaveSkillRequest request) =>
        Ok(ApiResponse<SkillDocument>.Ok(
            _skills.Update(_currentUser.UserId, name, request.Markdown, request.AllowImplicitInvocation),
            "Skill 已保存"));

    [HttpPatch("{name}/enabled")]
    public ActionResult<ApiResponse<SkillDocument>> SetEnabled(string name, [FromBody] SetSkillEnabledRequest request) =>
        Ok(ApiResponse<SkillDocument>.Ok(
            _skills.SetEnabled(_currentUser.UserId, name, request.Enabled),
            request.Enabled ? "Skill 已启用" : "Skill 已停用"));

    [HttpDelete("{name}")]
    public ActionResult<ApiResponse> Delete(string name)
    {
        _skills.Delete(_currentUser.UserId, name);
        return Ok(ApiResponse.Ok("Skill 已移至工作区回收目录"));
    }

    private static SkillSummaryDto Summary(SkillDocument skill) => new()
    {
        Name = skill.Name,
        Description = skill.Description,
        Enabled = skill.Enabled,
        AllowImplicitInvocation = skill.AllowImplicitInvocation,
        Error = skill.Error
    };
}

public sealed class SkillSummaryDto
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; }
    public bool AllowImplicitInvocation { get; set; }
    public string? Error { get; set; }
}

public sealed class SaveSkillRequest
{
    public string Name { get; set; } = "";
    public string Markdown { get; set; } = "";
    public bool AllowImplicitInvocation { get; set; } = true;
}

public sealed class SetSkillEnabledRequest
{
    public bool Enabled { get; set; }
}
