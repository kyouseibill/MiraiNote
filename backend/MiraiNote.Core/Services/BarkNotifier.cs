using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services;

public interface IBarkNotifier
{
    /// <summary>key 为空时直接返回，不发请求。</summary>
    Task PushAsync(BarkPushRequest request, CancellationToken ct = default);
}

public sealed class BarkPushRequest
{
    public string DeviceKey { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public string OpenUrl { get; init; } = "";
}

/// <summary>Bark 推送只允许官方主机。用户不能自己填服务器地址。</summary>
public static class BarkEndpoints
{
    public const string AllowedHost = "api.day.app";
    public static readonly Uri OfficialPushUri = new("https://api.day.app/push");

    public static void EnsureAllowed(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(endpoint.Host, AllowedHost, StringComparison.OrdinalIgnoreCase)
            || !endpoint.IsDefaultPort
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException("Bark 推送地址只允许 https://api.day.app");
        }
    }
}

/// <summary>只接受设备 key 本身。带协议或路径的一律拒绝，避免把服务器地址存进去。</summary>
public static partial class BarkDeviceKey
{
    public const int MaxLength = 64;

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidKey();

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var key = raw.Trim();
        if (key.Contains("://", StringComparison.Ordinal)
            || key.Contains('/')
            || key.Contains('\\')
            || key.Contains(' ')
            || key.Contains('@')
            || key.Contains('?')
            || key.Contains('#'))
        {
            throw new BusinessException("请只填写 Bark key，不要填写服务器地址");
        }

        if (!ValidKey().IsMatch(key))
        {
            throw new BusinessException("Bark key 格式不正确");
        }

        return key;
    }
}

public sealed class BarkNotifier : IBarkNotifier
{
    public const string HttpClientName = "Bark";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<BarkNotifier> _logger;

    public BarkNotifier(IHttpClientFactory http, ILogger<BarkNotifier> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task PushAsync(BarkPushRequest request, CancellationToken ct = default) =>
        PushAsync(request, BarkEndpoints.OfficialPushUri, ct);

    internal async Task PushAsync(BarkPushRequest request, Uri endpoint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceKey))
            return;

        BarkEndpoints.EnsureAllowed(endpoint);

        var payload = JsonSerializer.Serialize(new BarkPayload
        {
            DeviceKey = request.DeviceKey.Trim(),
            Title = request.Title,
            Body = request.Body,
            Url = request.OpenUrl
        });

        using var client = _http.CreateClient(HttpClientName);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.PostAsync(endpoint, content, ct);
        if (!response.IsSuccessStatusCode || !await ResponseOkAsync(response, ct))
        {
            throw new InvalidOperationException("Bark 推送失败");
        }

        _logger.LogInformation("Bark 提醒已发送");
    }

    private static async Task<bool> ResponseOkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return true;

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number)
                return code.GetInt32() == 200;
            return true;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private sealed class BarkPayload
    {
        [JsonPropertyName("device_key")]
        public string DeviceKey { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }
}
