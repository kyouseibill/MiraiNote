using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 用户实体。
/// Id=1 为系统超级管理员，由 Seeder 在首次启动时自动创建。
/// </summary>
[Table("User")]
public class User : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>用户名原样（仅 Trim），供页面显示。比较和唯一约束见 <see cref="NormalizedUserName"/>。</summary>
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    /// <summary>Trim + 小写后的用户名。登录、注册查重和唯一索引只用这一列。</summary>
    [Required]
    [MaxLength(50)]
    public string NormalizedUserName { get; set; } = string.Empty;

    /// <summary>BCrypt 哈希后的密码。</summary>
    [Required]
    [MaxLength(256)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>邮箱地址，入库前 Trim + 小写，全局唯一。</summary>
    [Required]
    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    /// <summary>是否为管理员。</summary>
    public bool IsAdmin { get; set; } = false;

    /// <summary>邮箱是否已验证。管理员创建的账户默认为 true，自助注册默认为 false。</summary>
    public bool IsEmailVerified { get; set; } = false;

    /// <summary>账户是否启用。管理员可禁用账户阻止登录。</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>最后登录时间（UTC）。</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Bark 设备 key。只存在数据库里；为空表示不推送。
    /// 不要写入日志、配置文件或异常消息。
    /// </summary>
    [MaxLength(64)]
    public string? BarkDeviceKey { get; set; }
}
