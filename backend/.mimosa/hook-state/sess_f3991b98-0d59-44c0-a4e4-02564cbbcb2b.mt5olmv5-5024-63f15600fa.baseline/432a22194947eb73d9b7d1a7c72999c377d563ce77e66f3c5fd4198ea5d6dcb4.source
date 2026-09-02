using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 刷新 Token。
/// 存储在数据库以支持主动吊销（登出、改密、禁用账户时清空）。
/// 实际的 Token 字符串写入 HttpOnly Cookie，数据库只保留哈希值用于校验。
/// </summary>
[Table("RefreshToken")]
public class RefreshToken : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>Token 的 SHA-256 哈希（Base64）。原始 Token 仅返回给客户端，不入库。</summary>
    [Required]
    [MaxLength(200)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>过期时间（UTC）。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>是否已吊销（登出/改密/禁用账户）。</summary>
    public bool IsRevoked { get; set; } = false;
}
