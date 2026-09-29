using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Household;

namespace MiraiNote.Core.Services.Household;

public interface IHouseholdTestClockService
{
    Task<HouseholdTestClockDto> GetAsync(int userId, CancellationToken ct = default);
    Task<HouseholdTestClockDto> SetAsync(int userId, SetHouseholdTestClockRequest request, CancellationToken ct = default);
    Task<HouseholdTestClockDto> ResetAsync(int userId, CancellationToken ct = default);
}

public sealed class HouseholdTestClockService : IHouseholdTestClockService
{
    private readonly AdjustableHouseholdTimeProvider _clock;
    private readonly HouseholdOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly MiraiNoteDbContext _db;

    public HouseholdTestClockService(
        AdjustableHouseholdTimeProvider clock,
        IOptions<HouseholdOptions> options,
        IHostEnvironment environment,
        MiraiNoteDbContext db)
    {
        _clock = clock;
        _options = options.Value;
        _environment = environment;
        _db = db;
    }

    public Task<HouseholdTestClockDto> GetAsync(int userId, CancellationToken ct = default) =>
        WithAdminAsync(userId, ct);

    public async Task<HouseholdTestClockDto> SetAsync(int userId, SetHouseholdTestClockRequest request, CancellationToken ct = default)
    {
        await WithAdminAsync(userId, ct);
        var hasAbsolute = request.UtcNow != null;
        var hasOffset = request.OffsetSeconds != null;
        if (hasAbsolute == hasOffset)
            throw new BusinessException("请指定 utcNow 或 offsetSeconds 其中之一", 400);

        if (hasAbsolute)
            _clock.SetAbsolute(request.UtcNow!.Value);
        else
            _clock.SetOffset(TimeSpan.FromSeconds(request.OffsetSeconds!.Value));

        return Describe();
    }

    public async Task<HouseholdTestClockDto> ResetAsync(int userId, CancellationToken ct = default)
    {
        await WithAdminAsync(userId, ct);
        _clock.Reset();
        return Describe();
    }

    private async Task<HouseholdTestClockDto> WithAdminAsync(int userId, CancellationToken ct)
    {
        if (!HouseholdTestClockPolicy.IsEnabled(_options, _environment))
            throw new BusinessException("测试时钟未启用", 404);

        if (userId <= 0)
            throw new BusinessException("未登录", 401);

        var isAdmin = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (bool?)u.IsAdmin)
            .FirstOrDefaultAsync(ct);
        if (isAdmin != true)
            throw new BusinessException("只有系统管理员可以调整测试时钟", 403);

        return Describe();
    }

    private HouseholdTestClockDto Describe()
    {
        var utc = _clock.GetUtcNow();
        return new HouseholdTestClockDto
        {
            Enabled = true,
            Mode = _clock.Mode,
            UtcNow = utc,
            ShanghaiToday = ShanghaiClock.ToShanghaiDate(utc),
            AbsoluteUtc = _clock.AbsoluteUtc,
            OffsetSeconds = _clock.OffsetSeconds
        };
    }
}
