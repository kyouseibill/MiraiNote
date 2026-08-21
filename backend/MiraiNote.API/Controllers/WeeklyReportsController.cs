using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
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
    private readonly ILogger<WeeklyReportsController> _logger;

    public WeeklyReportsController(
        IWeeklyReportService service,
        ICurrentUserService currentUser,
        ILogger<WeeklyReportsController> logger)
    {
        _service = service;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>调用 AI 生成（或重新生成）周报。</summary>
    [HttpPost("generate")]
    public async Task<ActionResult<ApiResponse<WeeklyReportDto>>> Generate(
        [FromBody] GenerateReportRequest request, CancellationToken ct)
    {
        var result = await _service.GenerateAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<WeeklyReportDto>.Ok(result, "周报生成成功"));
    }

    /// <summary>
    /// 流式生成周报（SSE）。不返回 JSON，通过 Server-Sent Events 逐事件推送：
    ///   - event: token     → AI 输出的增量文本
    ///   - event: heartbeat → 连接保活
    ///   - event: done      → 生成完成并已保存，携带周报字段
    ///   - event: error     → 出错
    /// </summary>
    [HttpPost("generate/stream")]
    public async Task GenerateStream(
        [FromBody] GenerateReportRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        await RunSseStreamAsync(
            async (callback, streamCt) =>
            {
                var report = await _service.GenerateStreamAsync(
                    userId,
                    request,
                    token => callback("token", JsonSerializer.Serialize(new { content = token })),
                    streamCt);

                await callback("done", JsonSerializer.Serialize(new
                {
                    reportId = report.Id,
                    weekStart = report.WeekStart,
                    weekEnd = report.WeekEnd,
                    content = report.Content,
                    generatedAt = report.GeneratedAt,
                    isEdited = report.IsEdited,
                    createdAt = report.CreatedAt,
                    updatedAt = report.UpdatedAt
                }));
            },
            ct);
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

    // ===== SSE 辅助（照 ChatController 的写出模式） =====

    private void ConfigureSseResponse()
    {
        Response.Headers["Content-Type"] = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-cache, no-transform";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    }

    private async Task RunSseStreamAsync(
        Func<ChatStreamCallback, CancellationToken, Task> streamAction,
        CancellationToken requestCt)
    {
        ConfigureSseResponse();

        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
        using var writeGate = new SemaphoreSlim(1, 1);
        var stopwatch = Stopwatch.StartNew();
        var terminalSent = 0;

        async Task WriteFrameAsync(string frame)
        {
            await writeGate.WaitAsync(streamCts.Token);
            try
            {
                await Response.WriteAsync(frame, streamCts.Token);
                await Response.Body.FlushAsync(streamCts.Token);
            }
            finally
            {
                writeGate.Release();
            }
        }

        ChatStreamCallback callback = (eventType, data) =>
        {
            if (eventType is "done" or "error")
                Interlocked.Exchange(ref terminalSent, 1);
            return WriteFrameAsync($"event: {eventType}\ndata: {data}\n\n");
        };

        async Task SendHeartbeatsAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
                while (await timer.WaitForNextTickAsync(streamCts.Token))
                {
                    var elapsedSeconds = Math.Max(1, (int)stopwatch.Elapsed.TotalSeconds);
                    await callback("heartbeat", JsonSerializer.Serialize(new
                    {
                        elapsedSeconds,
                        message = $"周报仍在生成，连接正常（{elapsedSeconds} 秒）"
                    }));
                }
            }
            catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
            {
                // 请求结束或用户主动停止。
            }
            catch (IOException)
            {
                // 客户端或中间代理已经断开，通知业务流程尽快停止。
                streamCts.Cancel();
            }
        }

        // 立即提交响应头，避免上游在 AI 返回首个 token 前把请求当成空闲连接。
        await WriteFrameAsync(": connected\n\n");
        var heartbeatTask = SendHeartbeatsAsync();

        try
        {
            await streamAction(callback, streamCts.Token);
        }
        catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
        {
            // 用户停止、浏览器离开或心跳检测到连接已断开。
        }
        catch (IOException) when (requestCt.IsCancellationRequested || streamCts.IsCancellationRequested)
        {
            // 响应连接已关闭，无需再向已断开的客户端写错误消息。
        }
        catch (BusinessException ex)
        {
            _logger.LogWarning("周报流业务异常 ({StatusCode}): {Message}", ex.StatusCode, ex.Message);
            if (Volatile.Read(ref terminalSent) == 0)
                await TryWriteStreamErrorAsync(callback, ex.Message, streamCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "周报流执行失败 {Path}", HttpContext.Request.Path);
            if (Volatile.Read(ref terminalSent) == 0)
            {
                var message = ex switch
                {
                    HttpRequestException => "AI 服务连接中断，请稍后重试",
                    TaskCanceledException => "AI 服务响应超时，请稍后重试",
                    JsonException => "AI 服务返回了无法解析的内容，请重试",
                    _ => "周报生成失败，请稍后重试"
                };
                await TryWriteStreamErrorAsync(callback, message, streamCts.Token);
            }
        }
        finally
        {
            streamCts.Cancel();
            await heartbeatTask;
        }
    }

    private static async Task TryWriteStreamErrorAsync(
        ChatStreamCallback callback,
        string message,
        CancellationToken ct)
    {
        try
        {
            await callback("error", JsonSerializer.Serialize(new { message }));
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // 客户端已断开，无法继续发送错误事件。
        }
        catch (IOException)
        {
            // 客户端已断开，无法继续发送错误事件。
        }
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
