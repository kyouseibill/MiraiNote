using Microsoft.Extensions.Options;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.Core.Services;

public interface IRegionCitySource
{
    Task<IReadOnlyList<string>> SearchAsync(string countryName, string countryCode, string query, CancellationToken ct = default);
}

public interface IRegionService
{
    IReadOnlyList<RegionCountryDto> ListCountries();
    Task<IReadOnlyList<RegionCityDto>> SearchCitiesAsync(string? country, string? query, CancellationToken ct = default);
}

public sealed class RegionService : IRegionService
{
    private readonly IReadOnlyList<RegionCountry> _countries;
    private readonly IRegionCitySource _cities;

    public RegionService(IOptions<RegionOptions> options, IRegionCitySource cities)
    {
        _countries = RegionCatalog.Resolve(options.Value);
        _cities = cities;
    }

    public IReadOnlyList<RegionCountryDto> ListCountries() =>
        _countries.Select(item => new RegionCountryDto
        {
            Name = item.Name,
            EnglishName = item.EnglishName,
            Code = item.Code,
        }).ToArray();

    public async Task<IReadOnlyList<RegionCityDto>> SearchCitiesAsync(
        string? country,
        string? query,
        CancellationToken ct = default)
    {
        var text = query?.Trim() ?? "";
        if (text.Length == 0)
            return [];

        var name = country?.Trim() ?? "";
        var match = _countries.FirstOrDefault(item => item.Name == name);
        if (match == null)
            throw new BusinessException("请选择国家");
        if (text.Length > 30)
            throw new BusinessException("请输入更短的城市名");

        var names = await _cities.SearchAsync(match.Name, match.Code, text, ct);
        return names
            .Where(city => !string.IsNullOrWhiteSpace(city))
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .Select(city => new RegionCityDto
            {
                Name = city,
                Label = WelcomePlace.Format(match.Name, city),
            })
            .ToArray();
    }
}
