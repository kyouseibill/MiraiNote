using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services;

/// <summary>
/// 基于 MailKit 的 SMTP 邮件发送实现。
/// 模板均为简洁的 HTML，含 MiraiNote 品牌与高亮按钮链接。
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    // ===== 公共方法 =====

    public Task SendVerifyEmailAsync(string toEmail, string username, string verifyLink, CancellationToken ct = default)
    {
        const string subject = "欢迎使用未来ノート，请验证您的邮箱";
        var html = WrapTemplate(
            title: "欢迎使用未来ノート",
            greeting: $"你好，{Escape(username)}：",
            body: "感谢注册未来ノート！请点击下方按钮完成邮箱验证，链接有效期为 <strong>24 小时</strong>。",
            buttonText: "验证邮箱",
            buttonLink: verifyLink,
            footer: "若按钮无法点击，请将以下链接复制到浏览器：<br/>" + Escape(verifyLink));
        return SendAsync(toEmail, subject, html, ct);
    }

    public Task SendAccountCreatedAsync(string toEmail, string username, string initialPassword, string loginLink, CancellationToken ct = default)
    {
        const string subject = "您的未来ノート账户已创建";
        var html = WrapTemplate(
            title: "账户已创建",
            greeting: $"你好，{Escape(username)}：",
            body: $"管理员已为您创建未来ノート账户。请使用以下凭证登录后<strong>尽快修改初始密码</strong>。<br/><br/>" +
                  $"<div style='background:#f3f4f6;padding:12px 16px;border-radius:6px;font-family:Consolas,monospace;'>" +
                  $"用户名：{Escape(username)}<br/>初始密码：{Escape(initialPassword)}" +
                  $"</div>",
            buttonText: "立即登录",
            buttonLink: loginLink,
            footer: "请妥善保管账户信息。若非本人操作，请忽略此邮件。");
        return SendAsync(toEmail, subject, html, ct);
    }

    public Task SendResetPasswordAsync(string toEmail, string username, string resetLink, CancellationToken ct = default)
    {
        const string subject = "未来ノート 密码重置请求";
        var html = WrapTemplate(
            title: "密码重置",
            greeting: $"你好，{Escape(username)}：",
            body: "我们收到了您的密码重置请求。请点击下方按钮设置新密码，链接有效期为 <strong>1 小时</strong>。<br/>" +
                  "若非本人操作，请忽略此邮件，您的账户仍然安全。",
            buttonText: "重置密码",
            buttonLink: resetLink,
            footer: "若按钮无法点击，请将以下链接复制到浏览器：<br/>" + Escape(resetLink));
        return SendAsync(toEmail, subject, html, ct);
    }

    public Task SendPasswordChangedAsync(string toEmail, string username, CancellationToken ct = default)
    {
        const string subject = "未来ノート 密码已更改";
        var cstZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var nowCst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cstZone);
        var html = WrapTemplate(
            title: "密码已更改",
            greeting: $"你好，{Escape(username)}：",
            body: $"您的未来ノート账户密码已于 <strong>{nowCst:yyyy-MM-dd HH:mm} (UTC+8)</strong> 成功更改。<br/><br/>" +
                  "若<strong>非本人操作</strong>，请立即联系管理员并重置密码以保护账户安全。",
            buttonText: null,
            buttonLink: null,
            footer: "本邮件为系统自动发送，请勿回复。");
        return SendAsync(toEmail, subject, html, ct);
    }

    public Task SendMemoReminderAsync(string toEmail, string username, string content, DateTime remindAtLocal, string section, CancellationToken ct = default)
    {
        var sectionLabel = string.Equals(section, "life", StringComparison.OrdinalIgnoreCase) ? "生活" : "工作";
        var subject = $"【未来ノート · {sectionLabel}提醒】{TrimForSubject(content)}";
        // 内容按行 escape + 换行，避免 HTML 注入并保留换行
        var contentHtml = string.Join("<br/>", content
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => Escape(line)));

        var html = WrapTemplate(
            title: $"{sectionLabel}备忘提醒",
            greeting: $"你好，{Escape(username)}：",
            body: $"以下{sectionLabel}备忘已到提醒时间 <strong>{remindAtLocal:yyyy-MM-dd HH:mm} (UTC+8)</strong>：<br/><br/>" +
                  $"<div style='background:#f3f4f6;padding:14px 18px;border-left:4px solid #4f46e5;border-radius:4px;color:#111827;font-size:14px;line-height:1.7;white-space:pre-wrap;'>" +
                  $"{contentHtml}" +
                  "</div>",
            buttonText: null,
            buttonLink: null,
            footer: "您可登录未来ノート将其标记为已完成或归档。");
        return SendAsync(toEmail, subject, html, ct);
    }

    private static string TrimForSubject(string content)
    {
        var line = content.Replace("\r", " ").Replace("\n", " ").Trim();
        return line.Length <= 30 ? line : line.Substring(0, 30) + "…";
    }

    // ===== 内部方法 =====

    private async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct)
    {
        var message = new MimeMessage();
        // QQ SMTP 要求 From 地址必须与认证账号相同，始终使用 SmtpUser 作为发件地址
        var fromEmail = !string.IsNullOrWhiteSpace(_options.SmtpUser) ? _options.SmtpUser : _options.FromAddress;
        message.From.Add(new MailboxAddress(_options.FromName, fromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        var socketOption = _options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        try
        {
            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, socketOption, ct);
            if (!string.IsNullOrWhiteSpace(_options.SmtpUser))
            {
                await client.AuthenticateAsync(_options.SmtpUser, _options.SmtpPassword, ct);
            }
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            _logger.LogInformation("邮件已发送：{Subject} → {To}", subject, toEmail);
        }
        catch (Exception ex)
        {
            // 邮件发送失败不应阻断主业务流程（如注册），向上抛由调用方决定策略
            _logger.LogError(ex, "邮件发送失败：{Subject} → {To}", subject, toEmail);
            throw;
        }
    }

    private static string Escape(string input) =>
        System.Net.WebUtility.HtmlEncode(input ?? string.Empty);

    /// <summary>
    /// 统一 HTML 模板：白底卡片 + MiraiNote 品牌色（靛蓝）+ 醒目按钮。
    /// </summary>
    private static string WrapTemplate(string title, string greeting, string body, string? buttonText, string? buttonLink, string footer)
    {
        var button = string.Empty;
        if (!string.IsNullOrEmpty(buttonText) && !string.IsNullOrEmpty(buttonLink))
        {
            button = $@"
                <div style='text-align:center;margin:32px 0;'>
                    <a href='{buttonLink}' style='display:inline-block;padding:12px 32px;background:#4f46e5;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:600;font-size:16px;'>
                        {Escape(buttonText)}
                    </a>
                </div>";
        }

        return $@"<!DOCTYPE html>
<html lang='zh-CN'>
<head><meta charset='UTF-8'/></head>
<body style='margin:0;padding:0;background:#f9fafb;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",""PingFang SC"",""Microsoft YaHei"",sans-serif;color:#1f2937;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background:#f9fafb;padding:40px 0;'>
        <tr><td align='center'>
            <table width='560' cellpadding='0' cellspacing='0' style='background:#ffffff;border-radius:8px;box-shadow:0 1px 3px rgba(0,0,0,0.08);overflow:hidden;'>
                <tr><td style='background:#4f46e5;padding:24px 32px;'>
                    <div style='color:#ffffff;font-size:20px;font-weight:700;'>未来ノート</div>
                </td></tr>
                <tr><td style='padding:32px;'>
                    <h2 style='margin:0 0 16px;font-size:18px;color:#111827;'>{Escape(title)}</h2>
                    <p style='margin:0 0 12px;font-size:14px;line-height:1.6;'>{greeting}</p>
                    <p style='margin:0;font-size:14px;line-height:1.7;color:#374151;'>{body}</p>
                    {button}
                    <p style='margin:32px 0 0;padding-top:16px;border-top:1px solid #e5e7eb;font-size:12px;color:#9ca3af;line-height:1.6;'>{footer}</p>
                </td></tr>
                <tr><td style='background:#f3f4f6;padding:16px 32px;text-align:center;font-size:12px;color:#9ca3af;'>
                    © {DateTime.UtcNow.Year} 未来ノート · 个人助理 Web 应用
                </td></tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";
    }
}
