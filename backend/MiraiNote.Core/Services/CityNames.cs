namespace MiraiNote.Core.Services;

/// <summary>
/// 城市名与行政区名按同一套后缀比较。去掉市、区、郡等尾巴后相同，就视为同一座城市。
/// </summary>
internal static class CityNames
{
    public const int MaxResults = 20;

    private static readonly string[] Suffixes = ["特别行政区", "自治区", "省", "市", "州", "郡", "都", "府", "道", "区"];

    public static string Key(string name)
    {
        var value = name.Trim();
        foreach (var suffix in Suffixes)
        {
            if (value.Length > suffix.Length && value.EndsWith(suffix, StringComparison.Ordinal))
                return value[..^suffix.Length];
        }

        return value;
    }
}
