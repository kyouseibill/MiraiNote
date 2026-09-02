using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 周报实体。AI 生成或手工编辑的工作周报。
/// </summary>
[Table("WeeklyReport")]
public class WeeklyReport : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>周报起始日期（周一）。</summary>
    public DateTime WeekStart { get; set; }

    /// <summary>周报结束日期（周日）。</summary>
    public DateTime WeekEnd { get; set; }

    /// <summary>周报内容，Markdown 格式。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>AI 生成时间。</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>用户是否手动编辑过。</summary>
    public bool IsEdited { get; set; } = false;
}
