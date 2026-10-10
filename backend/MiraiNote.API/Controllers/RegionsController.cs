using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiraiNote.Core.Services;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.API.Controllers;

/// <summary>所在地区。注册页还没有登录，国家和城市搜索都不要求凭证。</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/regions")]
public sealed class RegionsController : ControllerBase
{
    private readonly IRegionService _regions;

    public RegionsController(IRegionService regions)
    {
        _regions = regions;
    }

    [HttpGet("countries")]
    public ActionResult<ApiResponse<IReadOnlyList<RegionCountryDto>>> Countries()
    {
        return Ok(ApiResponse<IReadOnlyList<RegionCountryDto>>.Ok(_regions.ListCountries()));
    }

    [HttpGet("cities")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RegionCityDto>>>> Cities(
        [FromQuery] string? country,
        [FromQuery] string? q,
        CancellationToken ct)
    {
        var result = await _regions.SearchCitiesAsync(country, q, ct);
        return Ok(ApiResponse<IReadOnlyList<RegionCityDto>>.Ok(result));
    }
}
