namespace MiraiNote.Core.Services;

/// <summary>
/// 应用级 URL 配置（用于拼接邮件链接）。
/// </summary>
public class AppOptions
{
    public const string SectionName = "App";

    /// <summary>前端域名，邮件链接基址。例如：https://mirainote.example.com</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>
    /// 是否要求邮箱验证。
    /// 设为 false 时注册后直接标记为已验证，不发送验证邮件。
    /// </summary>
    public bool RequireEmailVerification { get; set; } = true;
}

/// <summary>
/// DeepSeek API 配置。
/// </summary>
public class DeepSeekOptions
{
    public const string SectionName = "DeepSeek";
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-chat";
}

/// <summary>
/// 文件上传配置。
/// </summary>
public class UploadOptions
{
    public const string SectionName = "Upload";
    /// <summary>上传文件存储根目录（相对于 wwwroot）。</summary>
    public string BasePath { get; set; } = "uploads";
}
