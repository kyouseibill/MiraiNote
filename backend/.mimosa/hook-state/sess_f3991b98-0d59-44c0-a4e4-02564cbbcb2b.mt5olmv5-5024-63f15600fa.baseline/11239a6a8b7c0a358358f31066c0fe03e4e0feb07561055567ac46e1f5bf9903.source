using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>用于组织长期主题相关对话、文件上下文和专属指令的项目空间。</summary>
[Table("ChatProject")]
public class ChatProject : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Color { get; set; } = "#0f766e";

    [MaxLength(10)]
    public string Icon { get; set; } = "◇";

    [MaxLength(4000)]
    public string? Instructions { get; set; }

    public ICollection<ChatSession> Sessions { get; set; } = new List<ChatSession>();
}
