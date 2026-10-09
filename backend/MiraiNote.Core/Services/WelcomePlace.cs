using System.Text;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services;

/// <summary>把用户填写的「国家-城市」整理成可查询的国家和城市。</summary>
public static class WelcomePlace
{
    public const int MaxLength = 80;

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

    public static bool TrySplit(string? place, out string country, out string city)
    {
        country = "";
        city = "";
        if (string.IsNullOrWhiteSpace(place)) return false;

        var normalized = place.Trim()
            .Replace('－', '-')
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace('―', '-');
        var index = normalized.IndexOf('-');
        if (index <= 0 || index >= normalized.Length - 1) return false;

        country = Collapse(normalized[..index]);
        city = Collapse(normalized[(index + 1)..]);
        return country.Length > 0 && city.Length > 0;
    }

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
