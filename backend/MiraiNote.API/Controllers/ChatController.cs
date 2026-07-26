using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
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

    // 确认状态：key = sessionId，value = TaskCompletionSource
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _pendingConfirms = new();

    public ChatController(IChatService service, ICurrentUserService currentUser, ChatFileParserService fileParser)
    {
        _service = service;
        _currentUser = currentUser;
        _fileParser = fileParser;
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<ApiResponse<List<ChatSessionDto>>>> GetSessions(CancellationToken ct)
    {
        var result = await _service.GetSessionsAsync(_currentUser.UserId, ct);
        return Ok(ApiResponse<List<ChatSessionDto>>.Ok(result));
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
        // 设置 SSE 响应头
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // 禁用 Nginx 缓冲

        var userId = _currentUser.UserId;

        await _service.SendMessageStreamAsync(
            userId,
            sessionId,
            request,
            async (eventType, data) =>
            {
                await Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            },
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
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var userId = _currentUser.UserId;

        // 为本次 Agent 会话创建确认信号
        var confirmTcs = new TaskCompletionSource<bool>();
        _pendingConfirms[sessionId] = confirmTcs;

        try
        {
            await _service.SendMessageAgentStreamAsync(
                userId,
                sessionId,
                request,
                async (eventType, data) =>
                {
                    await Response.WriteAsync($"event: {eventType}\ndata: {data}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                },
                async () =>
                {
                    // 等待前端确认（带超时 120s）
                    var completed = await Task.WhenAny(confirmTcs.Task, Task.Delay(120_000));
                    return completed == confirmTcs.Task && await confirmTcs.Task;
                },
                ct);
        }
        finally
        {
            _pendingConfirms.TryRemove(sessionId, out _);
        }
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
