using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 每日晨报缓存（Mirai M1）。每用户每日最多一条未删除记录（过滤唯一索引保证）。
/// </summary>
[Table("DailyBriefings")]
public class DailyBriefing : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>晨报日期（客户端本地日期）。</summary>
    public DateOnly BriefDate { get; set; }

    /// <summary>晨报 Markdown 正文（占位生成期间为空字符串）。</summary>
    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>引用来源清单 JSON：List&lt;SourceRef&gt;（type/id/title），供前端渲染溯源 chips。</summary>
    public string? SourcesJson { get; set; }

    /// <summary>生成所用模型名。</summary>
    [MaxLength(50)]
    public string? Model { get; set; }

    /// <summary>生成完成时间（UTC）。</summary>
    public DateTime GeneratedAt { get; set; }
}
