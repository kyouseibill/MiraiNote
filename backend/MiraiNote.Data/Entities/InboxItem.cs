using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 捕获收件箱条目（Mirai M1）。用户随手丢入的一段话，由 AI 分拣为 0~N 条结构化建议。
/// </summary>
[Table("InboxItems")]
public class InboxItem : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>用户原始输入（1..2000 字）。</summary>
    [Required]
    [MaxLength(2000)]
    public string Raw { get; set; } = string.Empty;

    /// <summary>来源：1=全局热键 2=今日流捕获条 3=手动 4=纠错重分拣。</summary>
    public byte Source { get; set; }

    /// <summary>状态：0=Pending 1=Triaging 2=Triaged 3=Dispatched 4=Discarded 5=Error。</summary>
    public byte Status { get; set; }

    /// <summary>
    /// 分拣结果 JSON。结构：{ items, uncertain, tzOffsetMinutes, localTime }，
    /// 其中 tzOffsetMinutes/localTime 为捕获时客户端上下文（供 dispatch 阶段 Local→UTC 换算），
    /// 对外 DTO 只暴露 items/uncertain。
    /// </summary>
    public string? AiParse { get; set; }

    /// <summary>分拣所用模型名。</summary>
    [MaxLength(50)]
    public string? AiModel { get; set; }

    /// <summary>用户纠错语（重新分拣时输入，注入 prompt）。</summary>
    [MaxLength(500)]
    public string? CorrectionNote { get; set; }

    /// <summary>分拣失败原因（Status=Error 时有值）。</summary>
    [MaxLength(500)]
    public string? Error { get; set; }

    /// <summary>分拣完成时间（UTC）。</summary>
    public DateTime? TriagedAt { get; set; }
}
