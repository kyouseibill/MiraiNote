using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// AI 写操作审计日志（Mirai M1）。记录"采纳 AI 建议→写入业务表"的全过程决策。
/// </summary>
[Table("AIActionLogs")]
public class AIActionLog : BaseEntity
{
    /// <summary>ActionType：AI 建议的分发落地。</summary>
    public const string ActionTypeInboxDispatch = "inbox_dispatch";

    /// <summary>ActionType：丢弃捕获项。</summary>
    public const string ActionTypeInboxDiscard = "inbox_discard";

    /// <summary>ActionType：撤销分发。</summary>
    public const string ActionTypeInboxUndo = "inbox_undo";

    /// <summary>ActionType：晨报重生成。</summary>
    public const string ActionTypeBriefingRegenerate = "briefing_regenerate";

    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>动作类型：inbox_dispatch / inbox_discard / inbox_undo / briefing_regenerate / command_write。</summary>
    [Required]
    [MaxLength(50)]
    public string ActionType { get; set; } = string.Empty;

    /// <summary>AI/用户的原始意图（raw 文本，超长截断至 500）。</summary>
    [MaxLength(500)]
    public string? IntentDesc { get; set; }

    /// <summary>
    /// 动作目标类型。收件箱流统一为 inbox（TargetId=收件箱条目 Id，配合索引支撑 undo 查询），
    /// 实际创建的业务实体记录在 PayloadJson.createdType/createdId。
    /// </summary>
    [MaxLength(20)]
    public string? TargetType { get; set; }

    /// <summary>动作目标 Id（inbox 流为 InboxItem.Id）。</summary>
    public int? TargetId { get; set; }

    /// <summary>建议 diff 与用户 overrides、落地实体引用（JSON）。</summary>
    public string? PayloadJson { get; set; }

    /// <summary>决策：applied / ignored / discarded / undone。</summary>
    [Required]
    [MaxLength(20)]
    public string Decision { get; set; } = string.Empty;

    /// <summary>决策时间（UTC）。</summary>
    public DateTime? DecidedAt { get; set; }
}
