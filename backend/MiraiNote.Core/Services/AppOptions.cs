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
    /// 对外绝对地址。有值时验证邮件链接用它，否则回落 <see cref="FrontendBaseUrl"/>。
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// 是否要求邮箱验证。
    /// 为 false 时不发验证邮件、不拦截登录；自助注册的 IsEmailVerified 仍为 false。
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
    /// <summary>周报生成时 LLM 输出的 max_tokens 上限（保守值，过大会超出模型上限导致 400）。</summary>
    public int WeeklyReportMaxOutputTokens { get; set; } = 8192;
    /// <summary>周报生成时参考文件文本注入 prompt 的总字符数上限（所有参考文件合计）。</summary>
    public int WeeklyReportMaxReferenceChars { get; set; } = 60_000;
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
    /// <summary>
    /// 成品导出根目录（Mirai M1，绝对路径）。为空时回落 workspace 根的同级 exports 目录
    /// （生产布局 fileservice\exports）。export_file 工具落点，经鉴权接口下载。
    /// </summary>
    public string? ExportsRoot { get; set; }
    /// <summary>
    /// 即弃文件根目录（Mirai M1，绝对路径）。为空时回落 workspace 根的同级 temp 目录
    /// （生产布局 fileservice\temp）。后台任务每日清理超期文件。
    /// </summary>
    public string? TempRoot { get; set; }
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
/// 工作台特别预警和首行实况共用的和风天气配置。两个值都只来自服务器配置或环境变量，不要写入前端。
/// Host 或 Key 任一为空，就不请求天气。实况只读 /v7/weather/now 的 now.text，不另加环境变量。
/// </summary>
/// <summary>
/// 所在地区的国家列表。留空用内置的 10 个常用国家。
/// 写成非空数组时按该顺序整表替换，不要在这里放全世界国家。
/// </summary>
public class RegionOptions
{
    public const string SectionName = "Regions";

    public RegionCountryOption[] Countries { get; set; } = [];
}

public class RegionCountryOption
{
    public string Name { get; set; } = "";

    /// <summary>和风城市搜索 range 用的 ISO 3166-1 alpha-2，例如 cn。</summary>
    public string Code { get; set; } = "";
}

public class QWeatherOptions
{
    public const string SectionName = "QWeather";

    /// <summary>
    /// 控制台里的 API Host。可写 https://abcxyz.qweatherapi.com，或只写 abcxyz.qweatherapi.com。
    /// 不要带路径或密钥。环境变量名：QWeather__ApiHost。
    /// </summary>
    public string ApiHost { get; set; } = "";

    /// <summary>
    /// 和风 API Key，或控制台生成的 JWT。环境变量名：QWeather__ApiKey。
    /// 与 <see cref="ApiHost"/> 任一为空则不请求天气。
    /// </summary>
    public string ApiKey { get; set; } = "";
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
