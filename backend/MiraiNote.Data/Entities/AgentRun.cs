using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 可恢复的持久化 Agent 执行单元。HTTP/SSE 连接只是它的观察者，不能决定执行生命周期。
/// </summary>
[Table("AgentRun")]
public class AgentRun : BaseEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int UserId { get; set; }
    public int SessionId { get; set; }

    /// <summary>queued/running/awaiting_confirmation/completed/failed/stopped/recoverable。</summary>
    [Required, MaxLength(32)]
    public string Status { get; set; } = "queued";

    [Required]
    public string RequestJson { get; set; } = string.Empty;

    /// <summary>已成功完成的工具调用等可恢复检查点。</summary>
    public string? CheckpointJson { get; set; }

    public int? UserMessageId { get; set; }
    public int? AssistantMessageId { get; set; }
    public string? FailureMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public DateTime? RecoverableAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(SessionId))]
    public ChatSession? Session { get; set; }

    public ICollection<AgentRunEvent> Events { get; set; } = new List<AgentRunEvent>();
}
