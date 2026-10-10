namespace MiraiNote.Core.Services;

/// <summary>
/// 选中国家后可直接浏览的常用城市。不是全世界城市库；搜不到的仍走和风关键字。
/// 日本札幌与 Geo 把区级结果提升后的展示一致，用「札幌市」。
/// </summary>
internal static class CommonCities
{
    private static readonly Dictionary<string, string[]> ByCountry = new(StringComparer.Ordinal)
    {
        ["中国"] =
        [
            "北京", "上海", "广州", "深圳", "成都", "杭州", "重庆", "武汉", "西安", "南京",
            "天津", "苏州", "长沙", "郑州", "青岛", "厦门", "大连", "沈阳", "哈尔滨", "无锡",
        ],
        ["日本"] =
        [
            "东京", "大阪", "名古屋", "札幌市", "福冈", "京都", "神户", "横滨",
            "广岛", "仙台", "那霸", "川崎", "熊本", "长崎", "金泽",
        ],
        ["美国"] =
        [
            "纽约", "洛杉矶", "芝加哥", "旧金山", "西雅图", "波士顿", "华盛顿",
            "休斯顿", "迈阿密", "亚特兰大", "达拉斯", "费城", "凤凰城", "圣迭戈", "丹佛", "檀香山",
        ],
        ["英国"] = ["伦敦", "曼彻斯特", "爱丁堡", "伯明翰", "利物浦", "剑桥", "牛津", "格拉斯哥"],
        ["韩国"] = ["首尔", "釜山", "仁川", "大邱", "大田", "光州", "济州"],
        ["法国"] = ["巴黎", "里昂", "马赛", "尼斯", "图卢兹", "波尔多"],
        ["德国"] = ["柏林", "慕尼黑", "汉堡", "法兰克福", "科隆", "斯图加特"],
        ["新加坡"] = ["新加坡"],
        ["澳大利亚"] = ["悉尼", "墨尔本", "布里斯班", "珀斯", "堪培拉", "阿德莱德"],
        ["加拿大"] = ["多伦多", "温哥华", "蒙特利尔", "渥太华", "卡尔加里"],
    };

    public static IReadOnlyList<string> List(string country) =>
        ByCountry.TryGetValue(country, out var cities) ? cities : [];

    public static IReadOnlyList<string> Matching(string country, string query)
    {
        var cities = List(country);
        if (query.Length == 0)
            return cities;

        return cities.Where(name =>
                name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || CityNames.Key(name).Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
