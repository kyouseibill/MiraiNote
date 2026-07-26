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
    public string Model { get; set; } = "deepseek-v4-flash";
    /// <summary>模型上下文窗口 token 数。DeepSeek V4 默认按 1M 配置。</summary>
    public int ContextWindowTokens { get; set; } = 1_000_000;
    /// <summary>单个聊天附件提取后最多保留的文本字符数，用于长 PDF/Word/Excel 分析。</summary>
    public int MaxAttachmentTextChars { get; set; } = 800_000;
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
/// Agent 文件系统访问配置。
/// </summary>
public class FileSystemOptions
{
    public const string SectionName = "FileSystem";
    /// <summary>Agent 工作区根目录（绝对路径）。为空时使用 {ContentRootPath}/workspace。</summary>
    public string? WorkspaceRoot { get; set; }
    /// <summary>是否允许文件写入操作（默认 true）。</summary>
    public bool AllowWrite { get; set; } = true;
    /// <summary>是否允许 Shell 命令执行（默认 false，需显式开启）。</summary>
    public bool AllowShell { get; set; } = false;
}

/// <summary>
/// 天气查询配置（Open-Meteo 免费 API，无需 Key；保留扩展性）。
/// </summary>
public class WeatherOptions
{
    public const string SectionName = "Weather";
    /// <summary>天气提供商，默认 OpenMeteo</summary>
    public string Provider { get; set; } = "OpenMeteo";
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
