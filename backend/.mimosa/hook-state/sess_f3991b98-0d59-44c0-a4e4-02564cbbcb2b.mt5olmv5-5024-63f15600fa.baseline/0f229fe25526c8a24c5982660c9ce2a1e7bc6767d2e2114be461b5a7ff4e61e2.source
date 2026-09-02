using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 周报参考文件实体。上传的历史 Excel 周报，供 AI 生成周报时参考格式与历史内容。
/// </summary>
[Table("WeeklyReportReference")]
public class WeeklyReportReference : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>原始文件名。</summary>
    [Required]
    [MaxLength(200)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>服务器存储路径。</summary>
    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>解析出的纯文本，供 AI Prompt 注入使用。</summary>
    public string ParsedText { get; set; } = string.Empty;

    /// <summary>可选：标注对应周次起始日期。</summary>
    public DateTime? WeekStart { get; set; }

    /// <summary>可选：标注对应周次结束日期。</summary>
    public DateTime? WeekEnd { get; set; }

    /// <summary>备注，如"2025年Q4模板"。</summary>
    [MaxLength(200)]
    public string? Remark { get; set; }
}
