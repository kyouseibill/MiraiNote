namespace MiraiNote.Core.Services;

/// <summary>
/// 对外链接。有 <see cref="AppOptions.PublicBaseUrl"/> 时用它，否则回落 <see cref="AppOptions.FrontendBaseUrl"/>。
/// </summary>
public static class AppLinks
{
    public static string Absolute(AppOptions options, string pathAndQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            ? options.FrontendBaseUrl
            : options.PublicBaseUrl;
        baseUrl = baseUrl.Trim().TrimEnd('/');
        if (!baseUrl.Contains("://", StringComparison.Ordinal))
        {
            baseUrl = "https://" + baseUrl;
        }
        return baseUrl + pathAndQuery;
    }

    /// <summary>点开 Bark 后进入对应板块的备忘列表，不是单条备忘。</summary>
    public static string MemoList(AppOptions options, string section)
    {
        var path = string.Equals(section, "life", StringComparison.OrdinalIgnoreCase)
            ? "/life/memos"
            : "/work/memos";
        return Absolute(options, path);
    }
}
