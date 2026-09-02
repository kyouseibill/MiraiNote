using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

public class ServerFetchWebPageTool : IServerAgentTool
{
    private readonly IHttpClientFactory _httpClientFactory;

    public string Name => "fetch_web_page";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "读取并分析公开网页内容。适用于用户给出 URL 后要求总结网页、提取正文、标题、链接或检查页面内容。仅支持 http/https；登录页面请使用 login_and_fetch_web。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["url"] = ToolParameterProperty.String("网页 URL，必须是 http 或 https"),
            ["output"] = ToolParameterProperty.Enum("返回格式：text=提取可读文本，html=原始 HTML 摘要，links=页面链接", new() { "text", "html", "links" }),
            ["headers_json"] = ToolParameterProperty.String("可选请求头 JSON 对象，例如 {\"User-Agent\":\"...\"}")
        },
        Required = new() { "url" }
    };

    public ServerFetchWebPageTool(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;
        if (!ToolArgHelper.TryGetString(args, "url", out var url))
            return "读取网页失败：url 为必填项。";
        ToolArgHelper.TryGetString(args, "output", out var output);
        if (string.IsNullOrWhiteSpace(output)) output = "text";

        if (!TryCreateHttpUri(url, out var uri))
            return "读取网页失败：仅支持 http/https URL。";

        try
        {
            using var client = CreateClient(_httpClientFactory);
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyHeaders(req, args);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await ReadLimitedAsync(resp.Content, ct);

            return output switch
            {
                "html" => JsonSerializer.Serialize(new { status = (int)resp.StatusCode, url = uri.ToString(), html = Truncate(body, 100_000) }),
                "links" => JsonSerializer.Serialize(new { status = (int)resp.StatusCode, url = uri.ToString(), links = ExtractLinks(body, uri).Take(100) }),
                _ => JsonSerializer.Serialize(new { status = (int)resp.StatusCode, url = uri.ToString(), text = ExtractReadableText(body) })
            };
        }
        catch (TaskCanceledException)
        {
            return "读取网页超时。";
        }
        catch (Exception ex)
        {
            return $"读取网页失败：{ex.Message}";
        }
    }

    internal static HttpClient CreateClient(IHttpClientFactory factory, HttpMessageHandler? handler = null)
    {
        var client = handler == null ? factory.CreateClient() : new HttpClient(handler, disposeHandler: true);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MiraiNote-Agent/1.0");
        return client;
    }

    internal static bool TryCreateHttpUri(string url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out uri!) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return true;
        uri = null!;
        return false;
    }

    internal static void ApplyHeaders(HttpRequestMessage req, JsonElement args)
    {
        if (!ToolArgHelper.TryGetString(args, "headers_json", out var headersJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(headersJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                var value = prop.Value.GetString();
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!req.Headers.TryAddWithoutValidation(prop.Name, value))
                    req.Content?.Headers.TryAddWithoutValidation(prop.Name, value);
            }
        }
        catch
        {
            // Ignore malformed optional headers.
        }
    }

    internal static async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        const int maxBytes = 2 * 1024 * 1024;
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            var remaining = maxBytes - (int)ms.Length;
            if (remaining <= 0) break;
            await ms.WriteAsync(buffer.AsMemory(0, Math.Min(read, remaining)), ct);
            if (ms.Length >= maxBytes) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    internal static string ExtractReadableText(string html)
    {
        var text = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return Truncate(text, 100_000);
    }

    internal static IEnumerable<object> ExtractLinks(string html, Uri baseUri)
    {
        foreach (Match match in Regex.Matches(html, "<a\\s+[^>]*href=[\"'](?<href>[^\"']+)[\"']", RegexOptions.IgnoreCase))
        {
            var href = match.Groups["href"].Value;
            if (Uri.TryCreate(baseUri, href, out var absolute))
                yield return new { href = absolute.ToString() };
        }
    }

    internal static string Truncate(string text, int maxChars) =>
        text.Length > maxChars ? text[..maxChars] + "\n... (已截断)" : text;
}

public class ServerHttpApiTool : IServerAgentTool
{
    private readonly IHttpClientFactory _httpClientFactory;

    public string Name => "call_http_api";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public string Description =>
        "调用 HTTP API 并分析响应。支持 GET/POST/PUT/PATCH/DELETE、自定义请求头、JSON 或原始请求体，以及 Bearer/Basic 认证。注意：非 GET 请求可能修改远端数据。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["url"] = ToolParameterProperty.String("API URL，必须是 http 或 https"),
            ["method"] = ToolParameterProperty.Enum("HTTP 方法", new() { "GET", "POST", "PUT", "PATCH", "DELETE" }),
            ["headers_json"] = ToolParameterProperty.String("请求头 JSON 对象"),
            ["body"] = ToolParameterProperty.String("原始请求体"),
            ["json_body"] = ToolParameterProperty.String("JSON 请求体字符串；提供后 Content-Type 默认为 application/json"),
            ["auth_type"] = ToolParameterProperty.Enum("认证类型", new() { "none", "bearer", "basic" }),
            ["token"] = ToolParameterProperty.String("Bearer token"),
            ["username"] = ToolParameterProperty.String("Basic Auth 用户名"),
            ["password"] = ToolParameterProperty.String("Basic Auth 密码")
        },
        Required = new() { "url" }
    };

    public ServerHttpApiTool(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;
        if (!ToolArgHelper.TryGetString(args, "url", out var url))
            return "API 调用失败：url 为必填项。";
        if (!ServerFetchWebPageTool.TryCreateHttpUri(url, out var uri))
            return "API 调用失败：仅支持 http/https URL。";

        ToolArgHelper.TryGetString(args, "method", out var methodText);
        var method = new HttpMethod(string.IsNullOrWhiteSpace(methodText) ? "GET" : methodText.ToUpperInvariant());

        try
        {
            using var client = ServerFetchWebPageTool.CreateClient(_httpClientFactory);
            using var req = new HttpRequestMessage(method, uri);
            ServerFetchWebPageTool.ApplyHeaders(req, args);
            ApplyAuth(req, args);
            ApplyBody(req, args);

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await ServerFetchWebPageTool.ReadLimitedAsync(resp.Content, ct);
            return JsonSerializer.Serialize(new
            {
                status = (int)resp.StatusCode,
                reason = resp.ReasonPhrase,
                contentType = resp.Content.Headers.ContentType?.ToString(),
                body = ServerFetchWebPageTool.Truncate(body, 200_000)
            });
        }
        catch (TaskCanceledException)
        {
            return "API 调用超时。";
        }
        catch (Exception ex)
        {
            return $"API 调用失败：{ex.Message}";
        }
    }

    internal static void ApplyAuth(HttpRequestMessage req, JsonElement args)
    {
        ToolArgHelper.TryGetString(args, "auth_type", out var authType);
        authType = string.IsNullOrWhiteSpace(authType) ? "none" : authType.ToLowerInvariant();
        if (authType == "bearer" && ToolArgHelper.TryGetString(args, "token", out var token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (authType == "basic" &&
                 ToolArgHelper.TryGetString(args, "username", out var username) &&
                 ToolArgHelper.TryGetString(args, "password", out var password))
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
    }

    internal static void ApplyBody(HttpRequestMessage req, JsonElement args)
    {
        if (ToolArgHelper.TryGetString(args, "json_body", out var jsonBody))
        {
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }
        else if (ToolArgHelper.TryGetString(args, "body", out var body))
        {
            req.Content = new StringContent(body, Encoding.UTF8, "text/plain");
        }
    }
}

public class ServerLoginAndFetchWebTool : IServerAgentTool
{
    private readonly IHttpClientFactory _httpClientFactory;

    public string Name => "login_and_fetch_web";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public string Description =>
        "使用用户提供的用户名密码模拟登录网站/API，然后携带登录 Cookie 访问目标 URL。适用于传统表单登录或 JSON 登录接口后的页面/API 分析。若网站强依赖验证码、短信、扫码或复杂 JS 交互，应改用 run_shell 编写 Playwright/Selenium 脚本。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["login_url"] = ToolParameterProperty.String("登录提交 URL"),
            ["target_url"] = ToolParameterProperty.String("登录后要访问的页面或 API URL"),
            ["username"] = ToolParameterProperty.String("用户名"),
            ["password"] = ToolParameterProperty.String("密码"),
            ["username_field"] = ToolParameterProperty.String("用户名字段名，默认 username"),
            ["password_field"] = ToolParameterProperty.String("密码字段名，默认 password"),
            ["login_format"] = ToolParameterProperty.Enum("登录请求格式：form 或 json", new() { "form", "json" }),
            ["extra_fields_json"] = ToolParameterProperty.String("登录附加字段 JSON 对象，例如 CSRF token、remember=true"),
            ["headers_json"] = ToolParameterProperty.String("登录和目标请求共用请求头 JSON 对象"),
            ["target_method"] = ToolParameterProperty.Enum("目标请求 HTTP 方法", new() { "GET", "POST" }),
            ["target_body"] = ToolParameterProperty.String("目标请求原始 body，可选"),
            ["output"] = ToolParameterProperty.Enum("返回格式：text/html/json/raw", new() { "text", "html", "json", "raw" })
        },
        Required = new() { "login_url", "target_url", "username", "password" }
    };

    public ServerLoginAndFetchWebTool(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;
        if (!ToolArgHelper.TryGetString(args, "login_url", out var loginUrl) ||
            !ToolArgHelper.TryGetString(args, "target_url", out var targetUrl) ||
            !ToolArgHelper.TryGetString(args, "username", out var username) ||
            !ToolArgHelper.TryGetString(args, "password", out var password))
            return "登录访问失败：login_url、target_url、username、password 均为必填项。";

        if (!ServerFetchWebPageTool.TryCreateHttpUri(loginUrl, out var loginUri) ||
            !ServerFetchWebPageTool.TryCreateHttpUri(targetUrl, out var targetUri))
            return "登录访问失败：仅支持 http/https URL。";

        ToolArgHelper.TryGetString(args, "username_field", out var usernameField);
        ToolArgHelper.TryGetString(args, "password_field", out var passwordField);
        ToolArgHelper.TryGetString(args, "login_format", out var loginFormat);
        ToolArgHelper.TryGetString(args, "target_method", out var targetMethod);
        ToolArgHelper.TryGetString(args, "output", out var output);
        usernameField = string.IsNullOrWhiteSpace(usernameField) ? "username" : usernameField;
        passwordField = string.IsNullOrWhiteSpace(passwordField) ? "password" : passwordField;
        loginFormat = string.IsNullOrWhiteSpace(loginFormat) ? "form" : loginFormat.ToLowerInvariant();
        targetMethod = string.IsNullOrWhiteSpace(targetMethod) ? "GET" : targetMethod.ToUpperInvariant();
        output = string.IsNullOrWhiteSpace(output) ? "text" : output.ToLowerInvariant();

        var cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = true,
            UseCookies = true
        };

        try
        {
            using var client = ServerFetchWebPageTool.CreateClient(_httpClientFactory, handler);

            using var loginReq = new HttpRequestMessage(HttpMethod.Post, loginUri);
            ServerFetchWebPageTool.ApplyHeaders(loginReq, args);
            loginReq.Content = BuildLoginContent(args, loginFormat, usernameField, passwordField, username, password);
            using var loginResp = await client.SendAsync(loginReq, HttpCompletionOption.ResponseHeadersRead, ct);
            var loginBody = await ServerFetchWebPageTool.ReadLimitedAsync(loginResp.Content, ct);

            using var targetReq = new HttpRequestMessage(new HttpMethod(targetMethod), targetUri);
            ServerFetchWebPageTool.ApplyHeaders(targetReq, args);
            if (ToolArgHelper.TryGetString(args, "target_body", out var targetBody))
                targetReq.Content = new StringContent(targetBody, Encoding.UTF8, "application/json");

            using var targetResp = await client.SendAsync(targetReq, HttpCompletionOption.ResponseHeadersRead, ct);
            var targetBodyText = await ServerFetchWebPageTool.ReadLimitedAsync(targetResp.Content, ct);
            var cookieCount = cookies.GetCookies(loginUri).Count + cookies.GetCookies(targetUri).Count;

            object rendered = output switch
            {
                "html" or "raw" => ServerFetchWebPageTool.Truncate(targetBodyText, 200_000),
                "json" => TryMinifyJson(targetBodyText),
                _ => ServerFetchWebPageTool.ExtractReadableText(targetBodyText)
            };

            return JsonSerializer.Serialize(new
            {
                loginStatus = (int)loginResp.StatusCode,
                targetStatus = (int)targetResp.StatusCode,
                targetContentType = targetResp.Content.Headers.ContentType?.ToString(),
                cookieCount,
                loginPreview = ServerFetchWebPageTool.Truncate(ServerFetchWebPageTool.ExtractReadableText(loginBody), 2000),
                result = rendered
            });
        }
        catch (TaskCanceledException)
        {
            return "登录访问超时。";
        }
        catch (Exception ex)
        {
            return $"登录访问失败：{ex.Message}";
        }
    }

    private static HttpContent BuildLoginContent(
        JsonElement args,
        string loginFormat,
        string usernameField,
        string passwordField,
        string username,
        string password)
    {
        var fields = ParseStringDictionary(args, "extra_fields_json");
        fields[usernameField] = username;
        fields[passwordField] = password;

        if (loginFormat == "json")
            return new StringContent(JsonSerializer.Serialize(fields), Encoding.UTF8, "application/json");

        return new FormUrlEncodedContent(fields);
    }

    private static Dictionary<string, string> ParseStringDictionary(JsonElement args, string key)
    {
        var result = new Dictionary<string, string>();
        if (!ToolArgHelper.TryGetString(args, key, out var json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.ToString();
            }
        }
        catch
        {
            // Ignore malformed optional fields.
        }
        return result;
    }

    private static object TryMinifyJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText()) ?? text;
        }
        catch
        {
            return ServerFetchWebPageTool.Truncate(text, 200_000);
        }
    }
}
