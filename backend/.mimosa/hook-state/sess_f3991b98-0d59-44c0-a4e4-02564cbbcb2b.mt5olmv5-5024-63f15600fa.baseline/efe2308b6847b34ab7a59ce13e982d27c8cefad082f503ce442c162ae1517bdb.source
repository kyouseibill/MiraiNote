using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 备忘录实体。工作与生活共用一张表，通过 <see cref="Section"/> 字段区分。
/// </summary>
[Table("Memo")]
public class Memo : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>所属板块：work（工作）/ life（生活）。</summary>
    [Required]
    [MaxLength(20)]
    public string Section { get; set; } = "work";

    /// <summary>备忘内容。</summary>
    [Required]
    [MaxLength(1000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>提醒时间（UTC，可选）。</summary>
    public DateTime? RemindAt { get; set; }

    /// <summary>
    /// 提醒方式位标志：0=不提醒，1=弹窗，2=邮件，3=弹窗+邮件。
    /// </summary>
    public byte RemindMethods { get; set; } = 0;

    /// <summary>邮件提醒是否已发送（避免重复发送）。</summary>
    public bool EmailReminderSent { get; set; } = false;

    /// <summary>弹窗提醒是否已被用户确认（避免重复弹出）。</summary>
    public bool PopupAcknowledged { get; set; } = false;

    /// <summary>最近一次提醒发出/弹出时间（UTC）。</summary>
    public DateTime? RemindedAt { get; set; }

    /// <summary>优先级：1=低 2=中 3=高，默认 2。</summary>
    public byte Priority { get; set; } = 2;

    /// <summary>是否置顶。</summary>
    public bool IsPinned { get; set; } = false;

    /// <summary>是否已完成。</summary>
    public bool IsDone { get; set; } = false;

    /// <summary>是否已归档。</summary>
    public bool IsArchived { get; set; } = false;
}
