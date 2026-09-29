using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

public sealed record HouseholdNotificationMessage(
    string Subject,
    string Text,
    string? ItemUrl,
    string? PurchaseLink);

public static class HouseholdNotificationComposer
{
    public static HouseholdNotificationMessage Test() => new(
        "MiraiNote 通知测试",
        "这是一条测试通知。如果收到，说明这个通道可用。",
        null,
        null);

    public static HouseholdNotificationMessage Item(
        string name,
        HouseholdReminderKind kind,
        DateOnly today,
        DateOnly due,
        int? stock,
        int? threshold,
        string? itemPurchaseLink,
        string? consumablePurchaseLink,
        string? itemUrl)
    {
        var daysUntil = due.DayNumber - today.DayNumber;
        var text = kind switch
        {
            HouseholdReminderKind.Lead => $"{name}将于 {due:yyyy-MM-dd} 到期，还有 {daysUntil} 天。",
            HouseholdReminderKind.Due => $"{name}今天到期。",
            HouseholdReminderKind.Overdue => $"{name}已逾期 {today.DayNumber - due.DayNumber} 天。",
            _ => name
        };

        var stockLine = StockLine(stock, threshold);
        if (stockLine != null)
            text += "\n" + stockLine;

        var purchase = FirstLink(itemPurchaseLink, consumablePurchaseLink);
        var subjectName = OneLine(name);
        var subject = kind switch
        {
            HouseholdReminderKind.Overdue => $"家务逾期：{subjectName}",
            HouseholdReminderKind.Due => $"家务今天到期：{subjectName}",
            _ => $"家务提醒：{subjectName}"
        };
        return new HouseholdNotificationMessage(subject, text, NormalizeOrNull(itemUrl), purchase);
    }

    public static HouseholdNotificationMessage Restock(
        string name,
        int stock,
        int threshold,
        string? purchaseLink)
    {
        var text = $"{name}库存 {stock}，低于或等于阈值 {threshold}。\n{StockLine(stock, threshold)}";
        return new HouseholdNotificationMessage(
            $"耗材需要补货：{OneLine(name)}",
            text,
            null,
            NormalizeOrNull(purchaseLink));
    }

    public static string? StockLine(int? stock, int? threshold)
    {
        if (stock is not int value || threshold is not int limit)
            return null;
        return value <= limit ? $"库存 {value}，需先买" : $"库存 {value}";
    }

    public static string BuildEmailHtml(HouseholdNotificationMessage message)
    {
        var body = string.Join("<br/>", (message.Text ?? "")
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => System.Net.WebUtility.HtmlEncode(line)));

        var links = new System.Text.StringBuilder();
        AppendLink(links, message.ItemUrl, "查看事项");
        AppendLink(links, message.PurchaseLink, "购买链接");

        return $"""
            <!DOCTYPE html><html lang="zh-CN"><head><meta charset="UTF-8"/></head>
            <body style="font-family:sans-serif;color:#1f2937;line-height:1.7;">
            <p>{body}</p>
            {links}
            </body></html>
            """;
    }

    private static void AppendLink(System.Text.StringBuilder html, string? raw, string label)
    {
        if (!HouseholdUrls.TryNormalize(raw, out var absolute))
            return;
        var encoded = System.Net.WebUtility.HtmlEncode(absolute);
        var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
        html.Append($"<p><a href=\"{encoded}\">{encodedLabel}</a></p>");
    }

    private static string? FirstLink(string? first, string? second)
    {
        if (HouseholdUrls.TryNormalize(first, out var primary))
            return primary;
        return HouseholdUrls.TryNormalize(second, out var fallback) ? fallback : null;
    }

    private static string? NormalizeOrNull(string? value) =>
        HouseholdUrls.TryNormalize(value, out var absolute) ? absolute : null;

    private static string OneLine(string value)
    {
        var line = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return line.Length <= 40 ? line : line[..40] + "…";
    }
}

public sealed class HouseholdNotificationDeliveryException : Exception
{
    public HouseholdNotificationDeliveryException(string message) : base(message)
    {
    }
}

public sealed class BarkNotificationChannel
{
    public const string HttpClientName = "HouseholdBark";
    public const string TimeSensitiveLevel = "timeSensitive";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IHttpClientFactory _http;
    private readonly ILogger<BarkNotificationChannel> _logger;

    public BarkNotificationChannel(IHttpClientFactory http, ILogger<BarkNotificationChannel> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task SendAsync(string barkAddress, HouseholdNotificationMessage message, CancellationToken ct)
    {
        var address = HouseholdUrls.Require(barkAddress, HouseholdFieldLimits.PurchaseLink, "Bark 地址");
        var body = message.Text;
        if (!string.IsNullOrEmpty(message.PurchaseLink))
            body += "\n购买链接：" + message.PurchaseLink;

        var payload = new Dictionary<string, string>
        {
            ["title"] = message.Subject,
            ["body"] = body,
            ["level"] = TimeSensitiveLevel
        };
        if (!string.IsNullOrEmpty(message.ItemUrl))
            payload["url"] = message.ItemUrl;

        var client = _http.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };

        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Bark 通知发送失败，HTTP {StatusCode}", (int)response.StatusCode);
                throw new HouseholdNotificationDeliveryException("Bark 通知发送失败");
            }
        }
        catch (HouseholdNotificationDeliveryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 异常文本经常带请求地址，而 Bark 地址里有密钥，所以只记类型。
            _logger.LogError("Bark 通知发送失败，类型 {ExceptionType}", ex.GetType().Name);
            throw new HouseholdNotificationDeliveryException("Bark 通知发送失败");
        }
    }
}

public sealed class EmailNotificationChannel
{
    private readonly IEmailService _email;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailNotificationChannel> _logger;

    public EmailNotificationChannel(
        IEmailService email,
        IOptions<EmailOptions> options,
        ILogger<EmailNotificationChannel> logger)
    {
        _email = email;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, HouseholdNotificationMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.SmtpHost))
        {
            _logger.LogError("家务邮件通知未发送：SMTP 未配置");
            throw new HouseholdNotificationDeliveryException("邮件服务未配置");
        }

        var html = HouseholdNotificationComposer.BuildEmailHtml(message);
        try
        {
            await _email.SendCustomEmailAsync(toEmail, message.Subject, html, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError("家务邮件通知发送失败，类型 {ExceptionType}", ex.GetType().Name);
            throw new HouseholdNotificationDeliveryException("邮件发送失败");
        }
    }
}
