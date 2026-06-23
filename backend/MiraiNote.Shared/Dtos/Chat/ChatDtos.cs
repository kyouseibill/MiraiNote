namespace MiraiNote.Shared.Dtos.Chat;

/// <summary>
/// 创建新会话请求。
/// </summary>
public class CreateSessionRequest
{
    public string Title { get; set; } = "新对话";
}

/// <summary>
/// 更新会话标题请求。
/// </summary>
public class UpdateSessionTitleRequest
{
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// 发送消息请求。
/// </summary>
public class SendMessageRequest
{
    public string Content { get; set; } = string.Empty;

    // ── Agent 模式控制参数 ──
    /// <summary>是否启用 Planner（默认 true）</summary>
    public bool EnablePlanner { get; set; } = true;
    /// <summary>是否启用 Reflector（默认 true）</summary>
    public bool EnableReflector { get; set; } = true;
    /// <summary>是否跳过危险操作确认（默认 false）</summary>
    public bool SkipConfirmation { get; set; } = false;

    /// <summary>附件内容列表（已提取的文本，由前端先上传再附加）</summary>
    public List<ChatAttachmentContent>? Attachments { get; set; }
}

/// <summary>
/// 聊天附件内容（前端上传文件后由后端解析，再随消息发送给 AI）。
/// </summary>
public class ChatAttachmentContent
{
    /// <summary>文件名（含扩展名）</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>文件类型描述（如 PDF、Word、Excel、文本、图片）</summary>
    public string FileType { get; set; } = string.Empty;
    /// <summary>提取的文本内容（图片时为占位描述）</summary>
    public string TextContent { get; set; } = string.Empty;
}

/// <summary>
/// 文件上传响应 DTO。
/// </summary>
public class ChatAttachmentResponseDto
{
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
}

/// <summary>
/// 会话列表项 DTO。
/// </summary>
public class ChatSessionDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 单条消息 DTO。
/// </summary>
public class ChatMessageDto
{
    public int Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 会话详情（含消息列表）DTO。
/// </summary>
public class ChatSessionDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<ChatMessageDto> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
