using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Shared;

namespace MiraiNote.Core.Services;

public interface IWelcomeGreetingService
{
    Task<WelcomeGreeting> GetGreetingAsync(int userId, DateTimeOffset utcNow, CancellationToken ct = default);
}

/// <summary>工作台欢迎语：名字、上海日历日，以及今天的备忘情况。</summary>
public sealed record WelcomeGreeting(
    string Content,
    string? FeatureNote,
    string? WeatherWarning,
    IReadOnlyList<WelcomeNewsItem> News);

/// <summary>
/// 工作台欢迎语。名字、日期和备忘摘要始终本地生成。
/// 特别预警和新闻失败、超时或未配置时直接省略，不影响前面两句。
/// </summary>
public sealed class WelcomeGreetingService : IWelcomeGreetingService
{
    private readonly MiraiNoteDbContext _db;
    private readonly IReadOnlyList<FeatureLaunchNote> _featureNotes;
    private readonly ISevereWeatherWarningSource _weather;
    private readonly IWelcomeNewsSource _news;

    public WelcomeGreetingService(
        MiraiNoteDbContext db,
        ISevereWeatherWarningSource weather,
        IWelcomeNewsSource news)
        : this(db, FeatureLaunchCatalog.Release, weather, news)
    {
    }

    public WelcomeGreetingService(MiraiNoteDbContext db, FeatureLaunchCatalog catalog)
        : this(db, catalog, DisabledSevereWeather.Instance, DisabledWelcomeNews.Instance)
    {
    }

    public WelcomeGreetingService(
        MiraiNoteDbContext db,
        FeatureLaunchCatalog catalog,
        ISevereWeatherWarningSource weather,
        IWelcomeNewsSource news)
    {
        _db = db;
        _featureNotes = catalog.Notes;
        _weather = weather;
        _news = news;
    }

    public async Task<WelcomeGreeting> GetGreetingAsync(
        int userId,
        DateTimeOffset utcNow,
        CancellationToken ct = default)
    {
        var newsTask = ReadNewsAsync(ct);
        var today = ShanghaiClock.Today(utcNow);
        var (startUtc, endUtc) = ShanghaiClock.DayRangeUtc(today);

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Username, u.WeatherPlace })
            .FirstOrDefaultAsync(ct);
        var name = string.IsNullOrWhiteSpace(user?.Username) ? "你" : user!.Username.Trim();

        var reminds = await _db.Memos.AsNoTracking()
            .Where(m => m.UserId == userId && !m.IsDone && !m.IsArchived)
            .Select(m => m.RemindAt)
            .ToListAsync(ct);

        var dueToday = 0;
        var unfinishedElsewhere = 0;
        foreach (var remindAt in reminds)
        {
            if (remindAt is DateTime at && at >= startUtc && at < endUtc)
                dueToday++;
            else
                unfinishedElsewhere++;
        }

        var content = Compose(name, today, dueToday, unfinishedElsewhere);
        var featureNote = FeatureLaunchNotes.Select(_featureNotes, today);
        var weatherTask = ReadWeatherAsync(user?.WeatherPlace, ct);
        await Task.WhenAll(weatherTask, newsTask);

        return new WelcomeGreeting(content, featureNote, weatherTask.Result, newsTask.Result);
    }

    private async Task<string?> ReadWeatherAsync(string? place, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(place))
            return null;

        try
        {
            return await _weather.GetWarningAsync(place, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<WelcomeNewsItem>> ReadNewsAsync(CancellationToken ct)
    {
        try
        {
            return await _news.GetLatestAsync(ct) ?? [];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// 没有今天到期的备忘、也没有其他未完成备忘时，只留名字和日期。
    /// 到期只统计上海当天、尚未完成的备忘；其余未完成备忘另说。
    /// </summary>
    public static string Compose(string name, DateOnly today, int dueToday, int unfinishedElsewhere)
    {
        var head = $"{name}，{today.ToString("M月d日", CultureInfo.InvariantCulture)}";
        var parts = new List<string>(2);
        if (dueToday > 0)
            parts.Add($"今天有 {dueToday} 条备忘到期");
        if (unfinishedElsewhere > 0)
            parts.Add($"还有 {unfinishedElsewhere} 条备忘没做完");
        if (parts.Count == 0)
            return head;
        return $"{head}。{string.Join("，", parts)}。";
    }

    private sealed class DisabledSevereWeather : ISevereWeatherWarningSource
    {
        public static readonly DisabledSevereWeather Instance = new();
        public Task<string?> GetWarningAsync(string? place, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DisabledWelcomeNews : IWelcomeNewsSource
    {
        public static readonly DisabledWelcomeNews Instance = new();
        public Task<IReadOnlyList<WelcomeNewsItem>> GetLatestAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WelcomeNewsItem>>([]);
    }
}
