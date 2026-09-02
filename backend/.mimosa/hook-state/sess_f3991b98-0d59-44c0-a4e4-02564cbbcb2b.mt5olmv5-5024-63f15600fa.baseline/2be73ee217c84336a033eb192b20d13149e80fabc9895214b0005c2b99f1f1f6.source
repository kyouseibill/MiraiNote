using System.Text.Json;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// 邮件发送工具。使用系统配置的 SMTP 代用户发送邮件。
/// 风险等级 Dangerous，需要用户确认后才执行。
/// </summary>
public class ServerSendEmailTool : IServerAgentTool
{
    private readonly IEmailService _emailService;

    public string Name => "send_email";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;
    public string Description =>
        "使用系统配置的邮箱代用户发送邮件。需要用户确认后才会发送。" +
        "适用于用户要求发送周报、提醒、通知等场景。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["to"] = ToolParameterProperty.String("收件人邮箱地址（必填）"),
            ["subject"] = ToolParameterProperty.String("邮件主题（必填）"),
            ["body"] = ToolParameterProperty.String("邮件正文（必填，支持纯文本或简单 HTML）"),
            ["body_format"] = ToolParameterProperty.Enum("正文格式：plain 或 html", new() { "plain", "html" })
        },
        Required = new() { "to", "subject", "body" }
    };

    public ServerSendEmailTool(IEmailService emailService)
    {
        _emailService = emailService;
    }

    // IAgentTool 兼容
    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "to", out var to))
            return "发送失败：未提供 to（收件人）。";
        if (!ToolArgHelper.TryGetString(args, "subject", out var subject))
            return "发送失败：未提供 subject（主题）。";
        if (!ToolArgHelper.TryGetString(args, "body", out var body))
            return "发送失败：未提供 body（正文）。";

        // 简单邮箱格式校验
        if (!to.Contains('@') || !to.Contains('.'))
            return $"发送失败：收件人邮箱「{to}」格式不正确。";

        try
        {
            var htmlBody = body;
            // 如果是纯文本，包裹为基础 HTML
            var format = "plain";
            if (args.TryGetProperty("body_format", out var fmt) && fmt.GetString() == "html")
                format = "html";

            if (format == "plain")
            {
                htmlBody = WrapPlainTextAsHtml(body);
            }

            await _emailService.SendCustomEmailAsync(to, subject, htmlBody, ct);
            return $"邮件已成功发送至 {to}，主题：「{subject}」。";
        }
        catch (Exception ex)
        {
            return $"邮件发送失败：{ex.Message}";
        }
    }

    private static string WrapPlainTextAsHtml(string text)
    {
        var escaped = System.Net.WebUtility.HtmlEncode(text)
            .Replace("\r\n", "<br/>")
            .Replace("\n", "<br/>");
        return $@"<!DOCTYPE html>
<html lang='zh-CN'>
<head><meta charset='UTF-8'/></head>
<body style='font-family:sans-serif;padding:20px;color:#333;'>
<div>{escaped}</div>
<hr style='margin-top:24px;border:none;border-top:1px solid #e5e7eb;'/>
<p style='font-size:12px;color:#9ca3af;'>此邮件由 MiraiNote Agent 代用户发送。</p>
</body>
</html>";
    }
}
