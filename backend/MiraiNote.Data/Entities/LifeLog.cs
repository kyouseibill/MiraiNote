using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 生活记录实体。用于记录生活点滴、日记、感想等。
/// </summary>
[Table("LifeLog")]
public class LifeLog : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>记录内容，支持 Markdown。</summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>心情标签，如：开心/平静/疲惫。</summary>
    [MaxLength(50)]
    public string? Mood { get; set; }

    /// <summary>图片路径（服务器存储路径）。</summary>
    [MaxLength(500)]
    public string? ImagePath { get; set; }

    /// <summary>记录日期（事件发生日期，与 CreatedAt 不同）。</summary>
    public DateTime LogDate { get; set; }
}
