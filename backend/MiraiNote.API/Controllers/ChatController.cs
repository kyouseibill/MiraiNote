using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Agent;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Chat;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/chat")]
public class ChatController : ControllerBase
{
    private readonly IChatService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly ChatFileParserService _fileParser;
    private readonly ILogger<ChatController> _logger;

    // 确认状态：key = sessionId，value = TaskCompletionSource
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _pendingConfirms = new();
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _temporaryPendingConfirms = new();

    public ChatController(
        IChatService service,
        ICurrentUserService currentUser,
        ChatFileParserService fileParser,
        ILogger<ChatController> logger)
    {
        _service = service;
        _currentUser = currentUser;
        _fileParser = fileParser;
        _logger = logger;
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<ApiResponse<List<ChatSessionDto>>>> GetSessions(
        [FromQuery] int? projectId,
        CancellationToken ct)
    {
        var result = await _service.GetSessionsAsync(_currentUser.UserId, projectId, ct);
        return Ok(ApiResponse<List<ChatSessionDto>>.Ok(result));
    }

    [HttpGet("sessions/search")]
    public async Task<ActionResult<ApiResponse<List<ChatSessionDto>>>> SearchSessions(
        [FromQuery] string query,
        [FromQuery] int? projectId,
        CancellationToken ct)
    {
        var result = await _service.SearchSessionsAsync(_currentUser.UserId, query, projectId, ct);
        return Ok(ApiResponse<List<ChatSessionDto>>.Ok(result));
    }

    [HttpGet("projects")]
    public async Task<ActionResult<ApiResponse<List<ChatProjectDto>>>> GetProjects(CancellationToken ct)
    {
        var result = await _service.GetProjectsAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<ChatProjectDto>>.Ok(result));
    }

    [HttpPost("projects")]
    public async Task<ActionResult<ApiResponse<ChatProjectDto>>> CreateProject(
        [FromBody] CreateChatProjectRequest request,
        CancellationToken ct)
    {
        var result = await _service.CreateProjectAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<ChatProjectDto>.Ok(result, "项目已创建"));
    }

    [HttpPut("projects/{projectId:int}")]
    public async Task<ActionResult<ApiResponse<ChatProjectDto>>> UpdateProject(
        int projectId,
        [FromBody] UpdateChatProjectRequest request,
        CancellationToken ct)
    {
        var result = await _service.UpdateProjectAsync(_currentUser.UserId, projectId, request, ct);
        return Ok(ApiResponse<ChatProjectDto>.Ok(result, "项目已更新"));
    }

    [HttpDelete("projects/{projectId:int}")]
    public async Task<ActionResult<ApiResponse>> DeleteProject(int projectId, CancellationToken ct)
    {
        await _service.DeleteProjectAsync(_currentUser.UserId, projectId, ct);
        return Ok(ApiResponse.Ok("项目已删除"));
    }

    /// <summary>
    /// 获取已归档的会话列表（仅标题与时间，不含消息内容）。仅在归档管理面板中使用。
    /// </summary>
    [HttpGet("sessions/archived")]
    public async Task<ActionResult<ApiResponse<List<ChatSessionDto>>>> GetArchivedSessions(CancellationToken ct)
    {
        var result = await _service.GetArchivedSessionsAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<ChatSessionDto>>.Ok(result));
    }

    [HttpGet("sessions/{sessionId:int}")]
    public async Task<ActionResult<ApiResponse<ChatSessionDetailDto>>> GetSession(int sessionId, CancellationToken ct)
    {
        var result = await _service.GetSessionAsync(_currentUser.UserId, sessionId, ct);
        return Ok(ApiResponse<ChatSessionDetailDto>.Ok(result));
    }

    [HttpPost("sessions")]
    public async Task<ActionResult<ApiResponse<ChatSessionDto>>> CreateSession(
        [FromBody] CreateSessionRequest request, CancellationToken ct)
    {
        var result = await _service.CreateSessionAsync(_currentUser.UserId, request, ct);
        return Ok(ApiResponse<ChatSessionDto>.Ok(result, "对话已创建"));
    }

    [HttpPut("sessions/{sessionId:int}")]
    public async Task<ActionResult<ApiResponse<ChatSessionDto>>> UpdateTitle(
        int sessionId, [FromBody] UpdateSessionTitleRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateSessionTitleAsync(_currentUser.UserId, sessionId, request, ct);
        return Ok(ApiResponse<ChatSessionDto>.Ok(result, "标题已更新"));
    }

    [HttpDelete("sessions/{sessionId:int}")]
    public async Task<ActionResult<ApiResponse>> DeleteSession(int sessionId, CancellationToken ct)
    {
        await _service.DeleteSessionAsync(_currentUser.UserId, sessionId, ct);
        return Ok(ApiResponse.Ok("已删除"));
    }

    [HttpPost("sessions/{sessionId:int}/archive")]
    public async Task<ActionResult<ApiResponse>> ArchiveSession(int sessionId, CancellationToken ct)
    {
        await _service.ArchiveSessionAsync(_currentUser.UserId, sessionId, ct);
        return Ok(ApiResponse.Ok("已归档"));
    }

    /// <summary>
    /// 还原归档会话，使其重新出现在常规列表中。
    /// </summary>
    [HttpPost("sessions/{sessionId:int}/unarchive")]
    public async Task<ActionResult<ApiResponse>> UnarchiveSession(int sessionId, CancellationToken ct)
    {
        await _service.UnarchiveSessionAsync(_currentUser.UserId, sessionId, ct);
        return Ok(ApiResponse.Ok("已还原"));
    }

    [HttpPost("sessions/{sessionId:int}/pin")]
    public async Task<ActionResult<ApiResponse<ChatSessionDto>>> SetPinned(
        int sessionId,
        [FromBody] SetSessionPinnedRequest request,
        CancellationToken ct)
    {
        var result = await _service.SetSessionPinnedAsync(_currentUser.UserId, sessionId, request.IsPinned, ct);
        return Ok(ApiResponse<ChatSessionDto>.Ok(result));
    }

    [HttpPost("sessions/{sessionId:int}/project")]
    public async Task<ActionResult<ApiResponse<ChatSessionDto>>> AssignProject(
        int sessionId,
        [FromBody] AssignSessionProjectRequest request,
        CancellationToken ct)
    {
        var result = await _service.AssignSessionProjectAsync(_currentUser.UserId, sessionId, request.ProjectId, ct);
        return Ok(ApiResponse<ChatSessionDto>.Ok(result));
    }

    [HttpPost("sessions/{sessionId:int}/branch")]
    public async Task<ActionResult<ApiResponse<ChatSessionDetailDto>>> BranchSession(
        int sessionId,
        [FromBody] BranchSessionRequest request,
        CancellationToken ct)
    {
        var result = await _service.BranchSessionAsync(_currentUser.UserId, sessionId, request, ct);
        return Ok(ApiResponse<ChatSessionDetailDto>.Ok(result, "分支对话已创建"));
    }

    [HttpPost("sessions/{sessionId:int}/messages")]
    public async Task<ActionResult<ApiResponse<ChatMessageDto>>> SendMessage(
        int sessionId, [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        var result = await _service.SendMessageAsync(_currentUser.UserId, sessionId, request, ct);
        return Ok(ApiResponse<ChatMessageDto>.Ok(result));
    }

    /// <summary>
    /// 流式发送消息（SSE）。与普通发送不同，此端点不返回 JSON，
    /// 而是通过 Server-Sent Events 逐事件推送：
    ///   - event: user_msg    → 用户消息已持久化
    ///   - event: token       → AI 回复的文本片段
    ///   - event: tool_call   → AI 正在调用工具
    ///   - event: tool_result → 工具执行完成
    ///   - event: done        → 全部完成
    ///   - event: error       → 出错
    /// </summary>
    [HttpPost("sessions/{sessionId:int}/messages/stream")]
    public async Task SendMessageStream(
        int sessionId,
        [FromBody] SendMessageRequest request,
        CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        await RunSseStreamAsync(
            (callback, streamCt) => _service.SendMessageStreamAsync(
                userId,
                sessionId,
                request,
                callback,
                streamCt),
            ct);
    }

    /// <summary>
    /// 临时聊天流式接口。历史由客户端携带，服务端不创建会话、不保存消息。
    /// </summary>
    [HttpPost("temporary/messages/stream")]
    public async Task SendTemporaryMessageStream(
        [FromBody] TemporaryChatRequest request,
        CancellationToken ct)
    {
        await RunSseStreamAsync(
            (callback, streamCt) => _service.SendTemporaryMessageStreamAsync(
                _currentUser.UserId,
                request,
                callback,
                streamCt),
            ct);
    }

    /// <summary>
    /// Agent 模式流式发送消息（SSE）。
    /// 包含 Plan → Execute → Reflect → Confirm 完整流程。
    /// 事件类型：user_msg、token、tool_call、tool_result、plan、reflection、confirm、context、done、error。
    /// </summary>
    [HttpPost("sessions/{sessionId:int}/messages/agent/stream")]
    public async Task SendMessageAgentStream(
        int sessionId,
        [FromBody] SendMessageRequest request,
        CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        // 为本次 Agent 会话创建确认信号
        var confirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingConfirms[sessionId] = confirmTcs;

        try
        {
            await RunSseStreamAsync(
                (callback, streamCt) => _service.SendMessageAgentStreamAsync(
                    userId,
                    sessionId,
                    request,
                    callback,
                    async () =>
                    {
                        // 等待前端确认（带超时 120s）
                        var completed = await Task.WhenAny(
                            confirmTcs.Task,
                            Task.Delay(120_000, streamCt));
                        return completed == confirmTcs.Task && await confirmTcs.Task;
                    },
                    streamCt),
                ct);
        }
        finally
        {
            _pendingConfirms.TryRemove(sessionId, out _);
        }
    }

    /// <summary>
    /// 临时 Agent 聊天流式接口。temporaryId 只用于本次危险操作确认，不会持久化。
    /// </summary>
    [HttpPost("temporary/{temporaryId}/messages/agent/stream")]
    public async Task SendTemporaryMessageAgentStream(
        string temporaryId,
        [FromBody] TemporaryChatRequest request,
        CancellationToken ct)
    {
        var confirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _temporaryPendingConfirms[temporaryId] = confirmTcs;

        try
        {
            await RunSseStreamAsync(
                (callback, streamCt) => _service.SendTemporaryMessageAgentStreamAsync(
                    _currentUser.UserId,
                    request,
                    callback,
                    async () =>
                    {
                        var completed = await Task.WhenAny(
                            confirmTcs.Task,
                            Task.Delay(120_000, streamCt));
                        return completed == confirmTcs.Task && await confirmTcs.Task;
                    },
                    streamCt),
                ct);
        }
        finally
        {
            _temporaryPendingConfirms.TryRemove(temporaryId, out _);
        }
    }

    [HttpPost("temporary/{temporaryId}/confirm")]
    public ActionResult ConfirmTemporaryToolCall(
        string temporaryId,
        [FromBody] AgentConfirmRequest request)
    {
        if (_temporaryPendingConfirms.TryGetValue(temporaryId, out var tcs))
        {
            tcs.TrySetResult(request.Confirmed);
            return Ok(ApiResponse.Ok(request.Confirmed ? "已确认" : "已取消"));
        }
        return NotFound(ApiResponse.Fail("没有待确认的操作"));
    }

    /// <summary>
    /// 确认/取消 Agent 危险操作。
    /// POST Body: { "confirmed": true/false }
    /// </summary>
    [HttpPost("sessions/{sessionId:int}/confirm")]
    public ActionResult ConfirmToolCall(
        int sessionId,
        [FromBody] AgentConfirmRequest request)
    {
        if (_pendingConfirms.TryGetValue(sessionId, out var tcs))
        {
            tcs.TrySetResult(request.Confirmed);
            return Ok(ApiResponse.Ok(request.Confirmed ? "已确认" : "已取消"));
        }
        return NotFound(ApiResponse.Fail("没有待确认的操作"));
    }

    /// <summary>
    /// Agent 确认请求 DTO。
    /// </summary>
    public class AgentConfirmRequest
    {
        public bool Confirmed { get; set; }
    }

    private void ConfigureSseResponse()
    {
        Response.Headers["Content-Type"] = "text/event-stream";
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
                        message = $"任务仍在处理，连接正常（{elapsedSeconds} 秒）"
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
            _logger.LogWarning("聊天流业务异常 ({StatusCode}): {Message}", ex.StatusCode, ex.Message);
            if (Volatile.Read(ref terminalSent) == 0)
                await TryWriteStreamErrorAsync(callback, ex.Message, streamCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "聊天流执行失败 {Path}", HttpContext.Request.Path);
            if (Volatile.Read(ref terminalSent) == 0)
            {
                var message = ex switch
                {
                    HttpRequestException => "AI 服务连接中断，请稍后重试",
                    TaskCanceledException => "AI 服务响应超时，请稍后重试",
                    JsonException => "AI 服务返回了无法解析的内容，请重试",
                    _ => "任务执行失败，请稍后重试"
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

    /// <summary>
    /// 上传聊天附件并提取文本内容。
    /// 支持 PDF / Word(.docx) / Excel(.xlsx/.xls) / 纯文本 / 代码 / 图片。
    /// 返回提取的文本内容供前端随消息一起发给 AI。
    /// 单文件限 20MB。
    /// </summary>
    [HttpPost("attachments")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 25 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<ChatAttachmentResponseDto>>> UploadAttachment(
        [FromForm] IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse.Fail("请选择文件"));

        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(ApiResponse.Fail("文件大小不能超过 20MB"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 文档
            ".pdf", ".docx", ".xlsx", ".xls",
            // 文本/代码
            ".txt", ".md", ".csv", ".json", ".xml", ".html", ".htm", ".yaml", ".yml",
            ".toml", ".ini", ".env", ".log", ".sql", ".ts", ".js", ".jsx", ".tsx",
            ".py", ".cs", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".php",
            ".rb", ".sh", ".bat", ".ps1", ".vue", ".css", ".scss", ".less",
            ".conf", ".config", ".csproj", ".sln",
            // 图片
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg",
            ".tiff", ".tif", ".avif"
        };

        if (!allowedExtensions.Contains(ext))
            return BadRequest(ApiResponse.Fail($"不支持的文件类型：{ext}"));

        // 确定文件类型描述
        var fileType = ext switch
        {
            ".pdf" => "PDF",
            ".docx" => "Word",
            ".xlsx" or ".xls" => "Excel",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp"
                or ".svg" or ".tiff" or ".tif" or ".avif" => "图片",
            _ => "文本"
        };

        await using var stream = file.OpenReadStream();
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var textContent = await _fileParser.ExtractTextAsync(buffer, file.FileName, ct);
        var mimeType = GetMimeType(ext);
        var isImage = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        string? dataUrl = null;

        var result = new ChatAttachmentResponseDto
        {
            FileName = file.FileName,
            FileType = fileType,
            TextContent = textContent,
            FileSizeBytes = file.Length,
            MimeType = mimeType,
            DataUrl = dataUrl,
            IsImage = isImage
        };

        return Ok(ApiResponse<ChatAttachmentResponseDto>.Ok(result, "文件已解析"));
    }

    private static string GetMimeType(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".tiff" or ".tif" => "image/tiff",
        ".avif" => "image/avif",
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".json" => "application/json",
        ".csv" => "text/csv",
        ".html" or ".htm" => "text/html",
        ".xml" => "application/xml",
        _ => "text/plain"
    };
}
