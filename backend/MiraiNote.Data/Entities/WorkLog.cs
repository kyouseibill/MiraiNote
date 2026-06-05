using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 工作记录实体。用户每日工作内容沉淀，供 AI 周报生成参考。
/// </summary>
[Table("WorkLog")]
public class WorkLog : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>标题，简短描述。</summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>工作目的/目标，供 AI 周报理解价值（可选）。</summary>
    [MaxLength(500)]
    public string? Purpose { get; set; }

    /// <summary>详细内容，支持 Markdown。</summary>
    public string? Content { get; set; }

    /// <summary>标签，逗号分隔。</summary>
    [MaxLength(500)]
    public string? Tags { get; set; }

    /// <summary>项目分类。</summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>记录日期（工作发生日期，与 CreatedAt 创建时间不同）。</summary>
    public DateTime LogDate { get; set; }

    /// <summary>工作状态：0=未标记，1=进行中，2=已完成，3=已延期。</summary>
    public byte Status { get; set; } = 0;
}
