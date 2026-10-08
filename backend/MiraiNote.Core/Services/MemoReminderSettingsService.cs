using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;

namespace MiraiNote.Core.Services;

public interface IMemoReminderSettingsService
{
    Task<MemoReminderSettingsDto> GetAsync(int userId, CancellationToken ct = default);
    Task<MemoReminderSettingsDto> UpdateBarkKeyAsync(int userId, string? barkKey, CancellationToken ct = default);
}

public class MemoReminderSettingsService : IMemoReminderSettingsService
{
    private readonly MiraiNoteDbContext _db;

    public MemoReminderSettingsService(MiraiNoteDbContext db)
    {
        _db = db;
    }

    public async Task<MemoReminderSettingsDto> GetAsync(int userId, CancellationToken ct = default)
    {
        var row = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.BarkDeviceKey })
            .FirstOrDefaultAsync(ct)
            ?? throw new BusinessException("用户不存在", 404);

        return new MemoReminderSettingsDto
        {
            BarkConfigured = !string.IsNullOrWhiteSpace(row.BarkDeviceKey)
        };
    }

    public async Task<MemoReminderSettingsDto> UpdateBarkKeyAsync(int userId, string? barkKey, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new BusinessException("用户不存在", 404);

        user.BarkDeviceKey = BarkDeviceKey.Normalize(barkKey);
        await _db.SaveChangesAsync(ct);

        return new MemoReminderSettingsDto
        {
            BarkConfigured = !string.IsNullOrWhiteSpace(user.BarkDeviceKey)
        };
    }
}
