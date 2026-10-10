namespace MiraiNote.Core.Services;

/// <summary>
/// 工作台大标题。每个时段只有一句，和前端 <c>greetingCopy.ts</c> 相同。
/// 不再按天下标轮换，也不因下雨或周五换句。
/// </summary>
public static class WelcomeGreetingCopy
{
    public const string Morning = "morning";
    public const string Noon = "noon";
    public const string Afternoon = "afternoon";
    public const string Evening = "evening";
    public const string LateNight = "latenight";

    public const string Spring = "spring";
    public const string Summer = "summer";
    public const string Autumn = "autumn";
    public const string Winter = "winter";

    public const string MorningLine = "早上好，{name}";
    public const string NoonLine = "中午好，{name}";
    public const string AfternoonLine = "下午好，{name}";
    public const string EveningLine = "晚上好，{name}";
    public const string LateNightLine = "夜深了，{name}，早点休息";

    public static string Template(string period) => period switch
    {
        Morning => MorningLine,
        Noon => NoonLine,
        Afternoon => AfternoonLine,
        Evening => EveningLine,
        LateNight => LateNightLine,
        _ => "你好，{name}",
    };
}
