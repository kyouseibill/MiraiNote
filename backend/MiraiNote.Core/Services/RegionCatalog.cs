namespace MiraiNote.Core.Services;

public sealed record RegionCountry(string Name, string Code);

/// <summary>
/// 所在地区 v1 只放常用国家，按产品给定的顺序展示。
/// 配置里的非空列表会整表替换这里，仍然不要塞进全世界。
/// </summary>
public static class RegionCatalog
{
    public static readonly IReadOnlyList<RegionCountry> Default =
    [
        new("中国", "cn"),
        new("日本", "jp"),
        new("美国", "us"),
        new("英国", "gb"),
        new("新加坡", "sg"),
        new("澳大利亚", "au"),
        new("加拿大", "ca"),
        new("韩国", "kr"),
        new("德国", "de"),
        new("法国", "fr"),
    ];

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
            list.Add(new RegionCountry(name, code));
        }

        return list.Count > 0 ? list : Default;
    }
}
