using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>AgentRun 的可重放事件；Sequence 在同一 Run 内严格递增。</summary>
[Table("AgentRunEvent")]
public class AgentRunEvent : BaseEntity
{
    [Key]
    public long Id { get; set; }

    public Guid RunId { get; set; }
    public long Sequence { get; set; }

    [Required, MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    public string DataJson { get; set; } = string.Empty;

    [ForeignKey(nameof(RunId))]
    public AgentRun? Run { get; set; }
}
