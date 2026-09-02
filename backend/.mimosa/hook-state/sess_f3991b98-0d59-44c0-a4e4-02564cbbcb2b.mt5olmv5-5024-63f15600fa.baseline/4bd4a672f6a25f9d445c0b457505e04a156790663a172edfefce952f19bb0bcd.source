using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 定时任务实体。用户通过 AI 对话创建的、将来某时刻自动执行的任务。
/// 由 ScheduledTaskExecutionService 后台服务扫描并调用 AI Agent 执行。
/// </summary>
[Table("ScheduledTask")]
public class ScheduledTask : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>任务描述（AI 将据此执行）。</summary>
    [Required]
    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>计划执行时间（UTC）。</summary>
    public DateTime ExecuteAt { get; set; }

    /// <summary>状态：Pending=待执行，Running=执行中，Completed=已完成，Failed=失败，Cancelled=已取消。</summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    /// <summary>执行结果（AI 回复文本）。</summary>
    [MaxLength(5000)]
    public string? Result { get; set; }

    /// <summary>错误信息（执行失败时填充）。</summary>
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>完成后是否发邮件通知用户。</summary>
    public bool NotifyEmail { get; set; } = false;

    /// <summary>实际执行时间（UTC）。</summary>
    public DateTime? ExecutedAt { get; set; }
}
