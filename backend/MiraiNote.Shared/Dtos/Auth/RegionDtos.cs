namespace MiraiNote.Shared.Dtos.Auth;

public class RegionCountryDto
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
}

public class RegionCityDto
{
    public string Name { get; set; } = "";

    /// <summary>展示用「国家 · 城市」，例如「中国 · 上海」。</summary>
    public string Label { get; set; } = "";
}
