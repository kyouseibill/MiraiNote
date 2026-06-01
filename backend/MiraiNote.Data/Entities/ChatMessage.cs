using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// AI 对话消息实体。属于某个 ChatSession，角色为 user 或 assistant。
/// </summary>
[Table("ChatMessage")]
public class ChatMessage : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>所属会话 Id。</summary>
    public int SessionId { get; set; }

    [ForeignKey(nameof(SessionId))]
    public ChatSession? Session { get; set; }

    /// <summary>消息角色：user / assistant。</summary>
    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = string.Empty;

    /// <summary>消息内容。</summary>
    [Required]
    public string Content { get; set; } = string.Empty;
}
