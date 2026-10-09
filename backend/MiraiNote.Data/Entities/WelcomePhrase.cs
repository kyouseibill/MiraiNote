using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiraiNote.Data.Entities;

/// <summary>
/// 工作台欢迎文案。问候、诗词、句子共用一张表，由管理端维护。
/// 选择时只看未删除且已启用的行，按 Id 排序后按上海日期取一条。
/// </summary>
[Table("WelcomePhrase")]
public class WelcomePhrase : BaseEntity
{
    public const int MaxKindLength = 20;
    public const int MaxTextLength = 500;
    public const int MaxAuthorLength = 100;
    public const int MaxSourceLength = 200;
    public const int MaxPeriodLength = 20;
    public const int MaxSpecialLength = 20;
    public const int MaxSeasonLength = 20;

    [Key]
    public int Id { get; set; }

    /// <summary>greeting | poem | quote。</summary>
    [Required]
    [MaxLength(MaxKindLength)]
    public string Kind { get; set; } = WelcomePhraseKind.Greeting;

    /// <summary>正文。问候里的 <c>{name}</c> 在返回前换成称呼。</summary>
    [Required]
    [MaxLength(MaxTextLength)]
    public string Text { get; set; } = string.Empty;

    /// <summary>作者。诗词用，可空。</summary>
    [MaxLength(MaxAuthorLength)]
    public string? Author { get; set; }

    /// <summary>出处，例如诗题。可空。</summary>
    [MaxLength(MaxSourceLength)]
    public string? Source { get; set; }

    /// <summary>时段：morning | noon | afternoon | evening | latenight。可空。</summary>
    [MaxLength(MaxPeriodLength)]
    public string? Period { get; set; }

    /// <summary>特殊场景：rain | friday。可空。</summary>
    [MaxLength(MaxSpecialLength)]
    public string? Special { get; set; }

    /// <summary>季节：spring | summer | autumn | winter。诗词用，可空。</summary>
    [MaxLength(MaxSeasonLength)]
    public string? Season { get; set; }

    /// <summary>停用后立刻不再入选。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>管理列表排序。挑选仍按 Id，不按这一列。</summary>
    public int SortOrder { get; set; }
}

public static class WelcomePhraseKind
{
    public const string Greeting = "greeting";
    public const string Poem = "poem";
    public const string Quote = "quote";

    public static readonly string[] All = [Greeting, Poem, Quote];
}

public static class WelcomePhrasePeriod
{
    public const string Morning = "morning";
    public const string Noon = "noon";
    public const string Afternoon = "afternoon";
    public const string Evening = "evening";
    public const string LateNight = "latenight";

    public static readonly string[] All = [Morning, Noon, Afternoon, Evening, LateNight];
}

public static class WelcomePhraseSpecial
{
    public const string Rain = "rain";
    public const string Friday = "friday";

    public static readonly string[] All = [Rain, Friday];
}

public static class WelcomePhraseSeason
{
    public const string Spring = "spring";
    public const string Summer = "summer";
    public const string Autumn = "autumn";
    public const string Winter = "winter";

    public static readonly string[] All = [Spring, Summer, Autumn, Winter];
}
