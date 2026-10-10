using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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
    string? MemoSummary,
    string? GreetingLine,
    string? InspirationLine);

/// <summary>称呼、日期行、实况和备忘摘要拆开，避免大标题重复日期和天气。</summary>
public sealed record WelcomeLines(
    string DisplayName,
    string DateLine,
    string? WeatherBrief,
    string? MemoSummary);

/// <summary>
/// 工作台欢迎语。称呼、上海日历日和备忘摘要始终本地生成。
/// 实况、特别预警和新闻失败、超时或未配置时直接省略，不影响称呼和日期。
/// 大标题是按时段固定的一句。小句交给 DeepSeek，失败则为 null。
/// </summary>
public sealed class WelcomeGreetingService : IWelcomeGreetingService
{
    private readonly MiraiNoteDbContext _db;
    private readonly IReadOnlyList<FeatureLaunchNote> _featureNotes;
    private readonly IWelcomeWeatherSource _weather;
    private readonly IWelcomeNewsSource _news;
    private readonly IWelcomeInspirationSource _inspiration;

    public WelcomeGreetingService(
        MiraiNoteDbContext db,
        ISevereWeatherWarningSource weather,
        IWelcomeNewsSource news,
        IWelcomeWeatherSource welcomeWeather)
        : this(db, FeatureLaunchCatalog.Release, weather, news, welcomeWeather, null)
    {
    }

    public WelcomeGreetingService(
        MiraiNoteDbContext db,
        ISevereWeatherWarningSource weather,
        IWelcomeNewsSource news,
        IWelcomeWeatherSource welcomeWeather,
        IWelcomeInspirationSource inspiration)
        : this(db, FeatureLaunchCatalog.Release, weather, news, welcomeWeather, inspiration)
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
        IWelcomeWeatherSource? welcomeWeather = null,
        IWelcomeInspirationSource? inspiration = null)
    {
        _db = db;
        _featureNotes = catalog.Notes;
        _weather = welcomeWeather ?? weather as IWelcomeWeatherSource ?? DisabledWelcomeWeather.Instance;
        _news = news;
        _inspiration = inspiration ?? DisabledInspiration.Instance;
    }

    public async Task<WelcomeGreeting> GetGreetingAsync(
        int userId,
        DateTimeOffset utcNow,
        CancellationToken ct = default)
    {
        var newsTask = ReadNewsAsync(userId, utcNow, ct);
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
        var wall = ShanghaiClock.ToShanghaiWall(utcNow);
        var greetingLine = ReadGreetingLine(wall, name);
        var inspiration = await ReadInspirationAsync(user?.WeatherPlace, weather, wall, ct);

        return new WelcomeGreeting(
            lines.DisplayName,
            featureNote,
            weather.Warning,
            newsTask.Result,
            lines.DisplayName,
            lines.DateLine,
            lines.WeatherBrief,
            lines.MemoSummary,
            greetingLine,
            inspiration);
    }

    private async Task<string?> ReadInspirationAsync(
        string? place,
        WelcomeWeather weather,
        DateTime shanghaiWall,
        CancellationToken ct)
    {
        try
        {
            return await _inspiration.GetLineAsync(
                new WelcomeInspirationContext(place, weather.NowText, weather.NowTemp, shanghaiWall),
                ct);
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

    private async Task<IReadOnlyList<WelcomeNewsItem>> ReadNewsAsync(
        int userId,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        try
        {
            return await _news.GetLatestAsync(userId, utcNow, ct) ?? [];
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

    /// <summary>
    /// 上海墙钟时段，起点含、终点不含：05–11 早晨，11–13 中午，13–18 下午，18–23 晚上，23–05 深夜。
    /// </summary>
    public static string PeriodOf(DateTime shanghaiWall)
    {
        var hour = shanghaiWall.Hour;
        if (hour >= 5 && hour < 11) return WelcomeGreetingCopy.Morning;
        if (hour >= 11 && hour < 13) return WelcomeGreetingCopy.Noon;
        if (hour >= 13 && hour < 18) return WelcomeGreetingCopy.Afternoon;
        if (hour >= 18 && hour < 23) return WelcomeGreetingCopy.Evening;
        return WelcomeGreetingCopy.LateNight;
    }

    /// <summary>大标题只看上海墙钟时段。下雨和周五不再换句。</summary>
    public static string ResolveSlot(DateTime shanghaiWall) => PeriodOf(shanghaiWall);

    /// <summary>3–5 春，6–8 夏，9–11 秋，12、1、2 冬。</summary>
    public static string SeasonOf(DateOnly shanghaiDate) => shanghaiDate.Month switch
    {
        >= 3 and <= 5 => WelcomeGreetingCopy.Spring,
        >= 6 and <= 8 => WelcomeGreetingCopy.Summer,
        >= 9 and <= 11 => WelcomeGreetingCopy.Autumn,
        _ => WelcomeGreetingCopy.Winter,
    };

    /// <summary>
    /// 同一上海日期、同一槽位得到同一个下标。按 Id 排序后的列表用这个下标。
    /// 日期加一天，下标也前进一步。
    /// </summary>
    public static int PickIndex(DateOnly shanghaiDate, string slot, int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var bias = slot switch
        {
            WelcomeGreetingCopy.Morning => 0,
            WelcomeGreetingCopy.Noon => 1,
            WelcomeGreetingCopy.Afternoon => 2,
            WelcomeGreetingCopy.Evening => 3,
            WelcomeGreetingCopy.LateNight => 4,
            _ => 5,
        };
        var mixed = shanghaiDate.DayNumber + bias;
        var index = mixed % count;
        return index < 0 ? index + count : index;
    }

    public static T? Pick<T>(IReadOnlyList<T> orderedById, DateOnly shanghaiDate, string slot)
    {
        if (orderedById.Count == 0)
            return default;
        return orderedById[PickIndex(shanghaiDate, slot, orderedById.Count)];
    }

    public static string FillName(string text, string name)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "你" : name.Trim();
        return text.Replace("{name}", safe, StringComparison.Ordinal);
    }

    /// <summary>只有 Development 和 Test 接受 now。Production 永远忽略。</summary>
    public static bool AllowsWelcomeNowOverride(IHostEnvironment env) =>
        env.IsDevelopment() || env.IsEnvironment("Test");

    private static readonly Regex OffsetSwallowedAsSpace = new(@" (\d{2}:\d{2})$", RegexOptions.Compiled);

    /// <summary>
    /// 解析 QA 传入的时刻。带 Z 或数字偏移的按偏移换算；没有偏移的按上海墙钟。
    /// 查询串里的加号有时会变成空格，这里把末尾的「 08:00」补回「+08:00」。
    /// </summary>
    public static bool TryParseWelcomeNow(string? text, out DateTimeOffset utcNow)
    {
        utcNow = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var repaired = OffsetSwallowedAsSpace.Replace(text.Trim(), "+$1");
        var utc = ShanghaiClock.ParseToUtc(repaired);
        if (utc == null)
            return false;

        utcNow = new DateTimeOffset(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc));
        return true;
    }

    private static string ReadGreetingLine(DateTime shanghaiWall, string name) =>
        FillName(WelcomeGreetingCopy.Template(PeriodOf(shanghaiWall)), name);

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

    private sealed class DisabledInspiration : IWelcomeInspirationSource
    {
        public static readonly DisabledInspiration Instance = new();
        public Task<string?> GetLineAsync(WelcomeInspirationContext context, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
