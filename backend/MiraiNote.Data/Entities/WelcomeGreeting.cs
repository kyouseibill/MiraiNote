using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// Dashboard 欢迎语文案池（全局，非用户维度）。由 SQL 脚本维护；应用按 SortOrder 加载并缓存。
/// </summary>
[Table("WelcomeGreeting")]
public class WelcomeGreeting : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>欢迎语正文（单行，最长 60）。</summary>
    [Required]
    [MaxLength(60)]
    public string Content { get; set; } = string.Empty;

    /// <summary>是否启用；禁用后不进入选句池。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>排序权重，越小越靠前；同值再按 Id。</summary>
    public int SortOrder { get; set; }
}
