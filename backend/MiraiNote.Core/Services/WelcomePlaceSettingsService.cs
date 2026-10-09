using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.Core.Services;

public interface IWelcomePlaceSettingsService
{
    Task<WelcomeSettingsDto> GetAsync(int userId, CancellationToken ct = default);
    Task<WelcomeSettingsDto> UpdateAsync(int userId, string? place, string? nickname, CancellationToken ct = default);
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
            .Select(u => new { u.WeatherPlace, u.Nickname })
            .FirstOrDefaultAsync(ct)
            ?? throw new BusinessException("用户不存在", 404);

        return new WelcomeSettingsDto
        {
            Place = string.IsNullOrWhiteSpace(row.WeatherPlace) ? null : row.WeatherPlace,
            Nickname = string.IsNullOrWhiteSpace(row.Nickname) ? null : row.Nickname
        };
    }

    public async Task<WelcomeSettingsDto> UpdateAsync(int userId, string? place, string? nickname, CancellationToken ct = default)
    {
        var normalizedPlace = WelcomePlace.Normalize(place);
        var normalizedNickname = WelcomeNickname.Normalize(nickname);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new BusinessException("用户不存在", 404);

        user.WeatherPlace = normalizedPlace;
        user.Nickname = normalizedNickname;
        await _db.SaveChangesAsync(ct);
        return new WelcomeSettingsDto
        {
            Place = user.WeatherPlace,
            Nickname = user.Nickname
        };
    }
}

/// <summary>欢迎语昵称。只 Trim；空白即清空。超长直接拒绝，不把原文写进错误消息。</summary>
public static class WelcomeNickname
{
    public const int MaxLength = 20;

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim();
        if (text.Length > MaxLength)
            throw new BusinessException("昵称请控制在 20 个字以内");
        return text;
    }
}
