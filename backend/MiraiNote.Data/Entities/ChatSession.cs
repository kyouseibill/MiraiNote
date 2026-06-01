using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// AI 对话会话实体。每个 Session 代表一个独立对话上下文。
/// </summary>
[Table("ChatSession")]
public class ChatSession : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>会话标题（可由用户修改或 AI 自动生成首消息摘要）。</summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>该会话下的所有消息。</summary>
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
