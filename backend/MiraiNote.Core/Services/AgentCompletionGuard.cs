using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiraiNote.Core.Services;

/// <summary>
/// Guards against an agent ending a requested image-download workflow while it is still mid-step.
/// </summary>
internal static class AgentCompletionGuard
{
    private static readonly Regex ImageDownloadRequest = new(
        @"(?:下载|保存|获取|找|搜|生成).{0,24}(?:图片|照片|图像|头像|壁纸|image|photo)|(?:图片|照片|图像|头像|壁纸|image|photo).{0,24}(?:下载|保存|获取)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IncompleteReply = new(
        @"(?:[：:,，、]|我再|继续(?:尝试|抓取|搜索|下载)|正在(?:尝试|抓取|搜索|下载)|接下来(?:继续|尝试)|先(?:抓取|搜索|下载))$",
        RegexOptions.CultureInvariant);

    internal static bool RequiresContinuation(string userRequest, string assistantContent, bool hasDeliveredImage)
    {
        if (hasDeliveredImage || !ImageDownloadRequest.IsMatch(userRequest)) return false;

        var trimmed = assistantContent.Trim();
        return trimmed.Length == 0 || IncompleteReply.IsMatch(trimmed);
    }

    internal static bool IsPublishedImageResult(string toolName, string result)
    {
        if (!string.Equals(toolName, "publish_workspace_file", StringComparison.Ordinal)) return false;
        try
        {
            using var doc = JsonDocument.Parse(result);
            return doc.RootElement.TryGetProperty("markdown", out var markdown) &&
                   markdown.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(markdown.GetString());
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
