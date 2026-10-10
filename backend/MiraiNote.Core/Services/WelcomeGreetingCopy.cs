namespace MiraiNote.Core.Services;

/// <summary>
/// 工作台大标题的问候池。和前端 <c>greetingCopy.ts</c> 是同一套句子。
/// 管理端文案库退役后，挑选改在这里完成，不再读表。
/// </summary>
public static class WelcomeGreetingCopy
{
    public const string Morning = "morning";
    public const string Noon = "noon";
    public const string Afternoon = "afternoon";
    public const string Evening = "evening";
    public const string LateNight = "latenight";
    public const string Rain = "rain";
    public const string Friday = "friday";

    public const string Spring = "spring";
    public const string Summer = "summer";
    public const string Autumn = "autumn";
    public const string Winter = "winter";

    public static readonly IReadOnlyList<string> MorningLines =
    [
        "早安，{name}",
        "早上好，{name}",
        "新的一天，{name}",
        "{name}，早",
    ];

    public static readonly IReadOnlyList<string> NoonLines =
    [
        "中午好，{name}",
        "{name}，记得吃饭",
        "午安，{name}",
        "{name}，歇一会儿吧",
    ];

    public static readonly IReadOnlyList<string> AfternoonLines =
    [
        "下午好，{name}",
        "{name}，喝杯茶？",
        "下午也加油，{name}",
        "{name}，下午好呀",
    ];

    public static readonly IReadOnlyList<string> EveningLines =
    [
        "晚上好，{name}",
        "{name}，今天辛苦了",
        "晚上好，{name}，放松一下",
        "{name}，晚上好呀",
    ];

    public static readonly IReadOnlyList<string> LateNightLines =
    [
        "夜深了，{name}",
        "{name}，早点休息",
        "还没睡呀，{name}",
        "夜深了，{name}，别熬太晚",
    ];

    public static readonly IReadOnlyList<string> RainLines =
    [
        "下雨了，{name}，记得带伞",
    ];

    public static readonly IReadOnlyList<string> FridayLines =
    [
        "周五了，{name}",
    ];

    public static IReadOnlyList<string> Lines(string slot) => slot switch
    {
        Morning => MorningLines,
        Noon => NoonLines,
        Afternoon => AfternoonLines,
        Evening => EveningLines,
        LateNight => LateNightLines,
        Rain => RainLines,
        Friday => FridayLines,
        _ => [],
    };
}
