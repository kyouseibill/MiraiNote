using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 欢迎语新闻已经展示给某个用户的链接。
/// 同一用户同一链接只留一行；再次展示时刷新 <see cref="ShownAt"/>。
/// 超过 7 天的行由欢迎语读取时删掉，链接可以重新进入候选。
/// 若 7 天内的已读把当前候选池滤空，挑选时改用池里最久没再展示的链接，避免欢迎语空白。
/// </summary>
[Table("WelcomeNewsSeen")]
public class WelcomeNewsSeen : BaseEntity
{
    public const int MaxUrlLength = 2000;

    [Key]
    public int Id { get; set; }

    /// <summary>所属用户 Id。</summary>
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>规范化后的新闻链接（去 fragment、主机小写、去掉末尾斜杠）。</summary>
    [Required]
    [MaxLength(MaxUrlLength)]
    public string Url { get; set; } = string.Empty;

    /// <summary>最近一次展示给该用户的时间（UTC）。</summary>
    public DateTime ShownAt { get; set; }
}
