using System.Globalization;

namespace MiraiNote.Core.Services;

public sealed record RegionCountry(string Name, string EnglishName, string Code);

/// <summary>
/// 所在地区的国家来自 ISO 3166-1。空配置用整张表，中国、日本在最前，其余 8 个常用国家紧随其后。
/// 配置里的非空列表仍按给定顺序整表替换，不会和内置表合并。
/// </summary>
public static class RegionCatalog
{
    internal static readonly string[] PinnedCodes = ["cn", "jp", "us", "gb", "sg", "au", "ca", "kr", "de", "fr"];

    public static readonly IReadOnlyList<RegionCountry> Default = CreateDefault();

    public static IReadOnlyList<RegionCountry> Resolve(RegionOptions? options)
    {
        if (options?.Countries is not { Length: > 0 })
            return Default;

        var list = new List<RegionCountry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in options.Countries)
        {
            var name = item.Name?.Trim() ?? "";
            var code = item.Code?.Trim().ToLowerInvariant() ?? "";
            if (name.Length is < 1 or > 20) continue;
            if (code.Length != 2 || code.Any(ch => ch is < 'a' or > 'z')) continue;
            if (name.Any(WelcomePlace.IsSeparatorChar)) continue;
            if (!seen.Add(name)) continue;
            list.Add(new RegionCountry(name, NormalizeEnglish(item.EnglishName), code));
        }

        return list.Count > 0 ? list : Default;
    }

    /// <summary>空查询保持原顺序。中文名、英文名、国家代码任一包含都算命中，忽略大小写。</summary>
    public static IReadOnlyList<RegionCountry> Search(IReadOnlyList<RegionCountry> countries, string? query)
    {
        var text = query?.Trim() ?? "";
        if (text.Length == 0) return countries;
        return countries.Where(item => Matches(item, text)).ToArray();
    }

    public static bool Matches(RegionCountry country, string query) =>
        country.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || country.EnglishName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || country.Code.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeEnglish(string? raw)
    {
        var english = raw?.Trim() ?? "";
        if (english.Length is 0 or > 80) return "";
        foreach (var ch in english)
        {
            if (char.IsControl(ch)) return "";
        }

        return english;
    }

    private static IReadOnlyList<RegionCountry> CreateDefault()
    {
        var all = Iso3166Countries.All;
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in all)
        {
            if (item.Name.Length is < 1 or > 20)
                throw new InvalidOperationException("ISO 国家中文名长度无效");
            if (item.EnglishName.Length is < 1 or > 80)
                throw new InvalidOperationException("ISO 国家英文名长度无效");
            if (item.Code.Length != 2 || item.Code.Any(ch => ch is < 'a' or > 'z'))
                throw new InvalidOperationException("ISO 国家代码无效");
            if (item.Name.Any(WelcomePlace.IsSeparatorChar))
                throw new InvalidOperationException("ISO 国家中文名含分隔符");
            if (!seenNames.Add(item.Name) || !seenCodes.Add(item.Code))
                throw new InvalidOperationException("ISO 国家重复");
        }

        var byCode = all.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var pinned = new List<RegionCountry>(PinnedCodes.Length);
        foreach (var code in PinnedCodes)
        {
            if (!byCode.TryGetValue(code, out var country))
                throw new InvalidOperationException("置顶国家缺失");
            pinned.Add(country);
        }

        var pinnedSet = PinnedCodes.ToHashSet(StringComparer.Ordinal);
        var order = StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), false);
        var rest = all
            .Where(item => !pinnedSet.Contains(item.Code))
            .OrderBy(item => item.Name, order)
            .ThenBy(item => item.Code, StringComparer.Ordinal);
        return pinned.Concat(rest).ToArray();
    }
}
