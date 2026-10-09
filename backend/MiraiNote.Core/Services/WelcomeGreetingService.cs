using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Shared;

namespace MiraiNote.Core.Services;

public interface IWelcomeGreetingService
{
    Task<WelcomeGreeting> GetGreetingAsync(int userId, DateTimeOffset utcNow, CancellationToken ct = default);
}

/// <summary>
/// 工作台欢迎语。Content 与 DisplayName 都只是称呼。
/// 日期和实况在 DateLine，备忘摘要在 MemoSummary。
/// </summary>
public sealed record WelcomeGreeting(
    string Content,
    string? FeatureNote,
    string? WeatherWarning,
    IReadOnlyList<WelcomeNewsItem> News,
    string DisplayName,
    string DateLine,
    string? WeatherBrief,
    string? MemoSummary);

/// <summary>称呼、日期行、实况和备忘摘要拆开，避免大标题重复日期和天气。</summary>
public sealed record WelcomeLines(
    string DisplayName,
    string DateLine,
    string? WeatherBrief,
    string? MemoSummary);

/// <summary>
/// 工作台欢迎语。称呼、上海日历日和备忘摘要始终本地生成。
/// 实况、特别预警和新闻失败、超时或未配置时直接省略，不影响称呼和日期。
/// </summary>
public sealed class WelcomeGreetingService : IWelcomeGreetingService
{
    private readonly MiraiNoteDbContext _db;
    private readonly IReadOnlyList<FeatureLaunchNote> _featureNotes;
    private readonly IWelcomeWeatherSource _weather;
    private readonly IWelcomeNewsSource _news;

    public WelcomeGreetingService(
        MiraiNoteDbContext db,
        ISevereWeatherWarningSource weather,
        IWelcomeNewsSource news,
        IWelcomeWeatherSource welcomeWeather)
        : this(db, FeatureLaunchCatalog.Release, weather, news, welcomeWeather)
    {
    }

    public WelcomeGreetingService(MiraiNoteDbContext db, FeatureLaunchCatalog catalog)
        : this(db, catalog, DisabledSevereWeather.Instance, DisabledWelcomeNews.Instance, DisabledWelcomeWeather.Instance)
    {
    }

    public WelcomeGreetingService(
        MiraiNoteDbContext db,
        FeatureLaunchCatalog catalog,
        ISevereWeatherWarningSource weather,
        IWelcomeNewsSource news,
        IWelcomeWeatherSource? welcomeWeather = null)
    {
        _db = db;
        _featureNotes = catalog.Notes;
        _weather = welcomeWeather ?? weather as IWelcomeWeatherSource ?? DisabledWelcomeWeather.Instance;
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
            .Select(u => new { u.Username, u.Nickname, u.WeatherPlace })
            .FirstOrDefaultAsync(ct);
        var username = string.IsNullOrWhiteSpace(user?.Username) ? "你" : user!.Username.Trim();
        var name = string.IsNullOrWhiteSpace(user?.Nickname) ? username : user!.Nickname.Trim();

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

        var featureNote = FeatureLaunchNotes.Select(_featureNotes, today);
        var weatherTask = ReadWeatherAsync(user?.WeatherPlace, ct);
        await Task.WhenAll(weatherTask, newsTask);
        var weather = await weatherTask;
        var lines = Arrange(name, today, dueToday, unfinishedElsewhere, weather.NowText, weather.NowTemp);

        return new WelcomeGreeting(
            lines.DisplayName,
            featureNote,
            weather.Warning,
            newsTask.Result,
            lines.DisplayName,
            lines.DateLine,
            lines.WeatherBrief,
            lines.MemoSummary);
    }

    private async Task<WelcomeWeather> ReadWeatherAsync(string? place, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(place))
            return default;

        try
        {
            return await _weather.GetAsync(place, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return default;
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
    /// 大标题只有称呼。日期行固定为上海日历日「M月d日 · 周几」。
    /// 实况接在后面：文字和气温都有时是「 · 多云 24°C」；缺气温只留文字；
    /// 缺文字只留「24°C」；都没有则不加天气后缀。周几和气温都不取每日预报。
    /// 没有今天到期的备忘、也没有其他未完成备忘时，备忘摘要为空。
    /// </summary>
    public static WelcomeLines Arrange(
        string name,
        DateOnly today,
        int dueToday,
        int unfinishedElsewhere,
        string? weatherText = null,
        string? weatherTemp = null)
    {
        var brief = FormatLiveCondition(weatherText, weatherTemp);
        var dateLine = $"{today.ToString("M月d日", CultureInfo.InvariantCulture)} · {ShanghaiWeekday(today)}";
        if (brief != null)
            dateLine = $"{dateLine} · {brief}";

        var parts = new List<string>(2);
        if (dueToday > 0)
            parts.Add($"今天有 {dueToday} 条备忘到期");
        if (unfinishedElsewhere > 0)
            parts.Add($"还有 {unfinishedElsewhere} 条备忘没做完");
        var memo = parts.Count == 0 ? null : $"{string.Join("，", parts)}。";
        return new WelcomeLines(name, dateLine, brief, memo);
    }

    /// <summary>上海日历日的中文星期。调用方须先用 <see cref="ShanghaiClock"/> 换成上海日期。</summary>
    public static string ShanghaiWeekday(DateOnly shanghaiDate) => shanghaiDate.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };

    /// <summary>
    /// 日期行上的实况片段。文字来自 now.text，气温来自 now.temp，单位固定 °C。
    /// </summary>
    public static string? FormatLiveCondition(string? weatherText, string? weatherTemp)
    {
        var brief = NormalizeWeatherText(weatherText);
        var temp = QWeatherNow.NormalizeTemp(weatherTemp);
        if (brief == null && temp == null)
            return null;
        if (brief == null)
            return temp + "°C";
        if (temp == null)
            return brief;
        return brief + " " + temp + "°C";
    }

    private static string? NormalizeWeatherText(string? weatherText)
    {
        if (string.IsNullOrWhiteSpace(weatherText))
            return null;
        var collapsed = string.Join(' ', weatherText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? null : collapsed;
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

    private sealed class DisabledWelcomeWeather : IWelcomeWeatherSource
    {
        public static readonly DisabledWelcomeWeather Instance = new();
        public Task<WelcomeWeather> GetAsync(string? place, CancellationToken ct = default) =>
            Task.FromResult(default(WelcomeWeather));
    }
}
