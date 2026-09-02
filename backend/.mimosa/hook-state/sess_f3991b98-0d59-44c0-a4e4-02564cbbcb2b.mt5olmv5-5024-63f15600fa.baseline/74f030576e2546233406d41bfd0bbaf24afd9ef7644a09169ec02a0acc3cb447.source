namespace MiraiNote.Shared.Common;

/// <summary>
/// SMTP 邮件配置。对应 appsettings.json 中 Email 节。
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "MiraiNote";

    /// <summary>
    /// SMTP 安全模式：
    /// - true：使用 SSL/TLS（隐式 TLS，常用 465 端口）
    /// - false：使用 STARTTLS（常用 587 端口，默认）
    /// </summary>
    public bool UseSsl { get; set; } = false;
}
