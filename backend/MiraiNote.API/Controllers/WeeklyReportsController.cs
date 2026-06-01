using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.WeeklyReports;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/reports")]
public class WeeklyReportsController : ControllerBase
{
    private readonly IWeeklyReportService _service;
    private readonly ICurrentUserService _currentUser;

    public WeeklyReportsController(IWeeklyReportService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    /// <summary>调用 AI 生成（或重新生成）周报。</summary>
    [HttpPost("generate")]
    public async Task<ActionResult<ApiResponse<WeeklyReportDto>>> Generate(
        [FromBody] GenerateReportRequest request, CancellationToken ct)
    {
        var result = await _service.GenerateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<WeeklyReportDto>.Ok(result, "周报生成成功"));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<WeeklyReportDto>>>> List(CancellationToken ct)
    {
        var result = await _service.GetListAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<WeeklyReportDto>>.Ok(result));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApiResponse<WeeklyReportDto>>> Get(int id, CancellationToken ct)
    {
        var item = await _service.GetByIdAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse<WeeklyReportDto>.Ok(item));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiResponse<WeeklyReportDto>>> Update(
        int id, [FromBody] UpdateReportRequest request, CancellationToken ct)
    {
        var updated = await _service.UpdateAsync(_currentUser.UserId, id, request, ct);
        return Ok(ApiResponse<WeeklyReportDto>.Ok(updated, "保存成功"));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }
}

[ApiController]
[Authorize]
[Route("api/v1/report-references")]
public class WeeklyReportReferencesController : ControllerBase
{
    private readonly IWeeklyReportService _service;
    private readonly ICurrentUserService _currentUser;

    public WeeklyReportReferencesController(IWeeklyReportService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<WeeklyReportReferenceDto>>>> List(CancellationToken ct)
    {
        var result = await _service.GetReferencesAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<WeeklyReportReferenceDto>>.Ok(result));
    }

    /// <summary>上传 Excel 参考文件（multipart/form-data）。</summary>
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<WeeklyReportReferenceDto>>> Upload(
        IFormFile file,
        [FromForm] DateTime? weekStart,
        [FromForm] DateTime? weekEnd,
        [FromForm] string? remark,
        CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("请选择文件"));

        var result = await _service.UploadReferenceAsync(_currentUser.UserId, file, weekStart, weekEnd, remark, ct);
        return Ok(ApiResponse<WeeklyReportReferenceDto>.Ok(result, "上传成功"));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteReferenceAsync(_currentUser.UserId, id, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }
}
