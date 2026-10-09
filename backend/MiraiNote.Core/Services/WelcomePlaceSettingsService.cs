using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.Core.Services;

public interface IWelcomePlaceSettingsService
{
    Task<WelcomeSettingsDto> GetAsync(int userId, CancellationToken ct = default);
    Task<WelcomeSettingsDto> UpdateAsync(int userId, string? place, CancellationToken ct = default);
}

public sealed class WelcomePlaceSettingsService : IWelcomePlaceSettingsService
{
    private readonly MiraiNoteDbContext _db;

    public WelcomePlaceSettingsService(MiraiNoteDbContext db)
    {
        _db = db;
    }

    public async Task<WelcomeSettingsDto> GetAsync(int userId, CancellationToken ct = default)
    {
        var row = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.WeatherPlace })
            .FirstOrDefaultAsync(ct)
            ?? throw new BusinessException("用户不存在", 404);

        return new WelcomeSettingsDto
        {
            Place = string.IsNullOrWhiteSpace(row.WeatherPlace) ? null : row.WeatherPlace
        };
    }

    public async Task<WelcomeSettingsDto> UpdateAsync(int userId, string? place, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new BusinessException("用户不存在", 404);

        user.WeatherPlace = WelcomePlace.Normalize(place);
        await _db.SaveChangesAsync(ct);
        return new WelcomeSettingsDto { Place = user.WeatherPlace };
    }
}
