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
    /// <summary>图片/文件 URL 路径前缀（相对路径，如 uploads）。始终保持为短名称，不得设为绝对路径。</summary>
    public string BasePath { get; set; } = "uploads";
    /// <summary>文件物理存储根目录（绝对路径）。为空时使用 {WebRootPath}/{BasePath}。生产环境建议配置此项。</summary>
    public string? PhysicalPath { get; set; }
}

/// <summary>
/// Tavily 互联网搜索 API 配置。
/// </summary>
public class TavilyOptions
{
    public const string SectionName = "Tavily";
    /// <summary>Tavily API Key，为空时禁用互联网搜索工具。</summary>
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.tavily.com";
    /// <summary>单次搜索返回最大结果数。</summary>
    public int MaxResults { get; set; } = 5;
}
