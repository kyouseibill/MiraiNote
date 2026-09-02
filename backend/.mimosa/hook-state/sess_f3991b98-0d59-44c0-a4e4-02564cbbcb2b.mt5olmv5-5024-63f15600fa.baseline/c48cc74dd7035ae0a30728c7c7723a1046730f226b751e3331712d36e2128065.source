using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 邮箱验证 / 密码重置 Token。
/// 独立成表是为了：① 避免污染 User 表；② 支持同一用户存在多个未过期 Token；③ 便于审计与频率限制。
/// </summary>
[Table("EmailVerifyToken")]
public class EmailVerifyToken : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>关联用户 Id。</summary>
    public int UserId { get; set; }

    /// <summary>导航属性。</summary>
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>随机生成的 Token（UUID v4）。</summary>
    [Required]
    [MaxLength(200)]
    public string Token { get; set; } = string.Empty;

    /// <summary>Token 类型：verify_email / reset_password。</summary>
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    /// <summary>过期时间（UTC）。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>是否已使用（一次性使用，使用后立即置 true）。</summary>
    public bool IsUsed { get; set; } = false;
}

/// <summary>Token 类型常量。</summary>
public static class EmailVerifyTokenType
{
    public const string VerifyEmail = "verify_email";
    public const string ResetPassword = "reset_password";
}
