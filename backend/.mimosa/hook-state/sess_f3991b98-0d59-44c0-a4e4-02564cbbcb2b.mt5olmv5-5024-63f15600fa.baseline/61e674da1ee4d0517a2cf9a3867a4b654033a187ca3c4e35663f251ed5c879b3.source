using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.Mirai;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;
using MiraiNote.Shared.Dtos.Mirai;

namespace MiraiNote.API.Controllers;

/// <summary>
/// Mirai M1 捕获收件箱 / 晨报 / 今日流 / AI 统计（契约 §2.1–2.8、2.11）。
/// 错误码约定：400 校验失败 / 404 不存在 / 409 状态冲突 / 422 建议不合法 / 429 超限额，
/// 由 Service 抛 BusinessException、全局异常中间件统一映射。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/mirai")]
public class MiraiController : ControllerBase
{
    private readonly IInboxTriageService _inboxService;
    private readonly IBriefingService _briefingService;
    private readonly IDayOverviewService _dayOverviewService;
    private readonly IMiraiStatsService _statsService;
    private readonly ICurrentUserService _currentUser;

    public MiraiController(
        IInboxTriageService inboxService,
        IBriefingService briefingService,
        IDayOverviewService dayOverviewService,
        IMiraiStatsService statsService,
        ICurrentUserService currentUser)
    {
        _inboxService = inboxService;
        _briefingService = briefingService;
        _dayOverviewService = dayOverviewService;
        _statsService = statsService;
        _currentUser = currentUser;
    }

    // ===== 2.1 创建捕获项并同步分拣 =====

    [HttpPost("inbox")]
    public async Task<ActionResult<ApiResponse<InboxItemDto>>> CreateInboxItem(
        [FromBody] CreateInboxItemRequest request, CancellationToken ct)
    {
        var result = await _inboxService.CreateAndTriageAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<InboxItemDto>.Ok(result));
    }

    // ===== 2.2 收件箱列表 =====

    [HttpGet("inbox")]
    public async Task<ActionResult<ApiResponse<PagedResult<InboxItemDto>>>> GetInbox(
        [FromQuery] int? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _inboxService.GetListAsync(_currentUser.UserId, status, page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<InboxItemDto>>.Ok(result));
    }

    // ===== 2.3 纠错重分拣 =====

    [HttpPost("inbox/{inboxItemId:int}/retriage")]
    public async Task<ActionResult<ApiResponse<InboxItemDto>>> Retriage(
        int inboxItemId, [FromBody] RetriageRequest request, CancellationToken ct)
    {
        var result = await _inboxService.RetriageAsync(_currentUser.UserId, inboxItemId, request, ct);
        return Ok(ApiResponse<InboxItemDto>.Ok(result));
    }

    // ===== 2.4 确认分发 =====

    [HttpPost("inbox/{inboxItemId:int}/dispatch")]
    public async Task<ActionResult<ApiResponse<DispatchResultDto>>> Dispatch(
        int inboxItemId, [FromBody] DispatchRequest request, CancellationToken ct)
    {
        var result = await _inboxService.DispatchAsync(_currentUser.UserId, inboxItemId, request, ct);
        return Ok(ApiResponse<DispatchResultDto>.Ok(result));
    }

    // ===== 2.5 丢弃 =====

    [HttpPost("inbox/{inboxItemId:int}/discard")]
    public async Task<IActionResult> Discard(int inboxItemId, CancellationToken ct)
    {
        await _inboxService.DiscardAsync(_currentUser.UserId, inboxItemId, ct);
        return NoContent();
    }

    // ===== 2.6 撤销分发 =====

    [HttpPost("inbox/{inboxItemId:int}/undo")]
    public async Task<IActionResult> Undo(int inboxItemId, CancellationToken ct)
    {
        await _inboxService.UndoAsync(_currentUser.UserId, inboxItemId, ct);
        return NoContent();
    }

    // ===== 2.7 今日流聚合 =====

    [HttpGet("day/overview")]
    public async Task<ActionResult<ApiResponse<DayOverviewDto>>> GetDayOverview(
        [FromQuery] string? date,
        [FromQuery] int tzOffsetMinutes = 0,
        CancellationToken ct = default)
    {
        var localDate = ParseDateOr400(date);
        var result = await _dayOverviewService.GetOverviewAsync(
            _currentUser.UserId, localDate, tzOffsetMinutes, ct);
        return Ok(ApiResponse<DayOverviewDto>.Ok(result));
    }

    // ===== 2.8 晨报重生成 =====

    [HttpPost("briefing/regenerate")]
    public async Task<ActionResult<ApiResponse<BriefingDto>>> RegenerateBriefing(
        [FromBody] RegenerateBriefingRequest request,
        [FromQuery] int tzOffsetMinutes = 0,
        CancellationToken ct = default)
    {
        var localDate = ParseDateOr400(request.Date);
        var result = await _briefingService.RegenerateAsync(
            _currentUser.UserId, localDate, tzOffsetMinutes, ct);
        return Ok(ApiResponse<BriefingDto>.Ok(result));
    }

    // ===== 2.11 AI 调用统计 =====

    [HttpGet("stats/ai-actions")]
    public async Task<ActionResult<ApiResponse<AiActionStatsDto>>> GetAiActionStats(CancellationToken ct)
    {
        var result = await _statsService.GetAiActionStatsAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<AiActionStatsDto>.Ok(result));
    }

    // ===== 成品导出文件鉴权下载（设计 §3.5）=====

    /// <summary>
    /// export_file 工具产出的成品文档下载（exports\{userId}\yyyy\MM\ 布局，
    /// 静态目录之外需鉴权，且仅能下载本人导出）。
    /// </summary>
    [HttpGet("exports/{*relativePath}")]
    public IActionResult DownloadExport(
        string relativePath,
        [FromServices] IOptions<FileSystemOptions> fsOptions)
    {
        var root = Path.GetFullPath(MiraiFileStorage.ExportsRoot(fsOptions.Value));
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || !System.IO.File.Exists(candidate))
        {
            return NotFound(ApiResponse.Fail("文件不存在"));
        }

        // 相对路径首段为导出归属用户 Id，仅允许本人下载
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !int.TryParse(segments[0], out var ownerUserId)
            || ownerUserId != _currentUser.UserId)
        {
            return NotFound(ApiResponse.Fail("文件不存在"));
        }

        var contentType = GetContentType(candidate);
        return PhysicalFile(candidate, contentType, Path.GetFileName(candidate));
    }

    // ===== 辅助 =====

    private static DateOnly ParseDateOr400(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)
            || !DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", out var parsed))
            throw new BusinessException("date 必传且格式须为 yyyy-MM-dd", 400);
        return parsed;
    }

    private static string GetContentType(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".json" => MediaTypeNames.Application.Json,
        ".csv" => "text/csv",
        ".md" or ".markdown" or ".txt" => MediaTypeNames.Text.Plain,
        _ => MediaTypeNames.Application.Octet
    };
}
