using System.Globalization;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services.Household;

/// <summary>
/// http/https 绝对地址。通知和购买链接都只用这里的结果，避免把未规范化的原文写进邮件或 Bark。
/// </summary>
public static class HouseholdUrls
{
    public static bool ContainsControlOrFormat(string value)
    {
        foreach (var ch in value)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 只接受带主机名的 http/https。<c>http:evil.com</c>、<c>http:///evil</c>、空主机和其它协议都拒绝。
    /// 成功时返回 <see cref="Uri.AbsoluteUri"/>。
    /// </summary>
    public static bool TryNormalize(string? value, out string absolute)
    {
        absolute = "";
        if (string.IsNullOrWhiteSpace(value) || ContainsControlOrFormat(value))
            return false;

        var trimmed = value.Trim();
        var schemeIndex = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex <= 0)
            return false;

        var scheme = trimmed[..schemeIndex];
        if (!scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (schemeIndex + 3 >= trimmed.Length || trimmed[schemeIndex + 3] == '/')
            return false;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;
        if (string.IsNullOrEmpty(uri.Host) || uri.Host.Trim('.').Length == 0)
            return false;

        absolute = uri.AbsoluteUri;
        return absolute.Length > 0;
    }

    /// <summary>空字符串视为未填写。非法地址抛 400，消息不回显原文。</summary>
    public static string? NormalizeOptional(string? value, int maxLength, string fieldLabel)
    {
        if (value == null)
            return null;
        if (ContainsControlOrFormat(value))
            throw new BusinessException($"{fieldLabel}只接受 http 或 https", 400);

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return null;
        if (trimmed.Length > maxLength)
            throw new BusinessException($"{fieldLabel}不能超过 {maxLength} 个字符", 400);
        if (!TryNormalize(trimmed, out var absolute) || absolute.Length > maxLength)
            throw new BusinessException($"{fieldLabel}只接受 http 或 https", 400);
        return absolute;
    }

    public static string Require(string? value, int maxLength, string fieldLabel)
    {
        var normalized = NormalizeOptional(value, maxLength, fieldLabel);
        if (normalized == null)
            throw new BusinessException($"请填写{fieldLabel}", 400);
        return normalized;
    }
}
