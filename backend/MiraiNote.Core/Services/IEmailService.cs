namespace MiraiNote.Core.Services;

/// <summary>
/// 邮件服务接口。Step 4 由 MailKit 实现，目前提供占位实现（仅日志）以便认证模块端到端流转。
/// </summary>
public interface IEmailService
{
    Task SendVerifyEmailAsync(string toEmail, string username, string verifyLink, CancellationToken ct = default);
    Task SendAccountCreatedAsync(string toEmail, string username, string initialPassword, string loginLink, CancellationToken ct = default);
    Task SendResetPasswordAsync(string toEmail, string username, string resetLink, CancellationToken ct = default);
    Task SendPasswordChangedAsync(string toEmail, string username, CancellationToken ct = default);

    /// <summary>发送备忘提醒邮件。</summary>
    /// <param name="remindAtLocal">提醒时间（已转换为本地展示时区，如 UTC+8）。</param>
    /// <param name="section">work 或 life，用于邮件标题。</param>
    Task SendMemoReminderAsync(string toEmail, string username, string content, DateTime remindAtLocal, string section, CancellationToken ct = default);
}
