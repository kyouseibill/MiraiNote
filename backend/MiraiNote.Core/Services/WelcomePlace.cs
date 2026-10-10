using System.Text;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services;

/// <summary>把所在地区整理成可查询的国家和城市。展示和入库用「国家 · 城市」，旧的连字符写法仍能拆开。</summary>
public static class WelcomePlace
{
    public const int MaxLength = 80;
    public const string DisplaySeparator = " · ";

    private static readonly Dictionary<string, string> CountryCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["中国"] = "cn",
        ["china"] = "cn",
        ["cn"] = "cn",
        ["美国"] = "us",
        ["usa"] = "us",
        ["us"] = "us",
        ["united states"] = "us",
        ["日本"] = "jp",
        ["japan"] = "jp",
        ["jp"] = "jp",
        ["英国"] = "gb",
        ["uk"] = "gb",
        ["gb"] = "gb",
        ["united kingdom"] = "gb",
        ["韩国"] = "kr",
        ["south korea"] = "kr",
        ["korea"] = "kr",
        ["kr"] = "kr",
        ["法国"] = "fr",
        ["france"] = "fr",
        ["fr"] = "fr",
        ["德国"] = "de",
        ["germany"] = "de",
        ["de"] = "de",
        ["新加坡"] = "sg",
        ["singapore"] = "sg",
        ["sg"] = "sg",
        ["加拿大"] = "ca",
        ["canada"] = "ca",
        ["ca"] = "ca",
        ["澳大利亚"] = "au",
        ["australia"] = "au",
        ["au"] = "au",
        ["印度"] = "in",
        ["india"] = "in",
        ["in"] = "in",
        ["俄罗斯"] = "ru",
        ["russia"] = "ru",
        ["ru"] = "ru",
        ["香港"] = "hk",
        ["中国香港"] = "hk",
        ["hong kong"] = "hk",
        ["hk"] = "hk",
        ["澳门"] = "mo",
        ["中国澳门"] = "mo",
        ["macau"] = "mo",
        ["mo"] = "mo",
        ["台湾"] = "tw",
        ["中国台湾"] = "tw",
        ["taiwan"] = "tw",
        ["tw"] = "tw",
        ["意大利"] = "it",
        ["italy"] = "it",
        ["西班牙"] = "es",
        ["spain"] = "es",
        ["荷兰"] = "nl",
        ["netherlands"] = "nl",
        ["瑞士"] = "ch",
        ["switzerland"] = "ch",
        ["瑞典"] = "se",
        ["sweden"] = "se",
        ["巴西"] = "br",
        ["brazil"] = "br",
        ["泰国"] = "th",
        ["thailand"] = "th",
        ["越南"] = "vn",
        ["vietnam"] = "vn",
        ["马来西亚"] = "my",
        ["malaysia"] = "my",
        ["印度尼西亚"] = "id",
        ["印尼"] = "id",
        ["indonesia"] = "id",
        ["菲律宾"] = "ph",
        ["philippines"] = "ph",
        ["新西兰"] = "nz",
        ["new zealand"] = "nz",
        ["爱尔兰"] = "ie",
        ["ireland"] = "ie",
        ["墨西哥"] = "mx",
        ["mexico"] = "mx",
        ["阿联酋"] = "ae",
        ["united arab emirates"] = "ae",
    };

    /// <summary>空白变成 null。超长拒绝保存。不改写用户能看懂的连字符。</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var sb = new StringBuilder(raw.Length);
        var pendingSpace = false;
        foreach (var ch in raw.Trim())
        {
            if (char.IsControl(ch)) continue;
            if (ch == '\u3000' || char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(ch);
        }

        var text = sb.ToString().Trim();
        if (text.Length == 0) return null;
        if (text.Length > MaxLength)
            throw new BusinessException("国家-城市请控制在 80 个字以内");
        return text;
    }

    public static string Format(string country, string city) => country + DisplaySeparator + city;

    public static bool TrySplit(string? place, out string country, out string city)
    {
        country = "";
        city = "";
        if (string.IsNullOrWhiteSpace(place)) return false;

        var text = place.Trim();
        var index = IndexOfSeparator(text);
        if (index <= 0 || index >= text.Length - 1) return false;

        country = Collapse(text[..index]);
        city = Collapse(text[(index + 1)..]);
        return country.Length > 0 && city.Length > 0;
    }

    /// <summary>
    /// 空白得到 null。能对上国家目录、且城市不是区县的，收成「国家 · 城市」。
    /// 超长仍抛原来的长度错误，不把原文写进消息。
    /// </summary>
    public static bool TryCanonicalize(
        string? raw,
        IReadOnlyList<RegionCountry> countries,
        out string? canonical,
        out string? error)
    {
        canonical = null;
        error = null;
        var normalized = Normalize(raw);
        if (normalized == null) return true;

        if (!TrySplit(normalized, out var country, out var city) || SeparatorCount(normalized) != 1)
        {
            error = "请选择所在地区";
            return false;
        }

        if (!countries.Any(item => string.Equals(item.Name, country, StringComparison.Ordinal)))
        {
            error = "请选择所在地区";
            return false;
        }

        if (!IsAcceptableCity(city))
        {
            error = "请选择城市";
            return false;
        }

        canonical = Format(country, city);
        if (canonical.Length > MaxLength)
        {
            error = "国家-城市请控制在 80 个字以内";
            return false;
        }

        return true;
    }

    internal static bool IsDistrictName(string name)
    {
        ReadOnlySpan<string> suffixes = ["街道", "自治县", "林区", "区", "县", "旗", "镇", "乡"];
        foreach (var suffix in suffixes)
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsAcceptableCity(string city)
    {
        if (city.Length is < 1 or > 40) return false;
        if (IsDistrictName(city)) return false;

        var hasLetter = false;
        foreach (var ch in city)
        {
            if (char.IsControl(ch) || IsSeparatorChar(ch)) return false;
            if (char.IsLetter(ch)) hasLetter = true;
        }

        return hasLetter;
    }

    private static int IndexOfSeparator(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (IsSeparatorChar(text[i])) return i;
        }

        return -1;
    }

    private static int SeparatorCount(string text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (IsSeparatorChar(ch)) count++;
        }

        return count;
    }

    internal static bool IsSeparatorChar(char ch) =>
        ch is '-' or '－' or '–' or '—' or '―' or '\u00B7' or '\u30FB';

    /// <summary>已知国家名换成和风 range 用的 ISO 代码。认不出就返回 null，改由结果里的国家名过滤。</summary>
    public static string? CountryCode(string country)
    {
        var key = Collapse(country);
        return CountryCodes.TryGetValue(key, out var code) ? code : null;
    }

    public static string CacheKey(string place)
    {
        if (!TrySplit(place, out var country, out var city))
            return Collapse(place).ToLowerInvariant();
        return country.ToLowerInvariant() + "-" + city.ToLowerInvariant();
    }

    private static string Collapse(string value)
    {
        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }
}
