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
