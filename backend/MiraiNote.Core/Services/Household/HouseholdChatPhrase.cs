using System.Globalization;
using System.Text.RegularExpressions;

namespace MiraiNote.Core.Services.Household;

internal enum HouseholdChatIntent
{
    Unrecognized,
    Record,
    History,
    Upcoming
}

internal sealed record HouseholdChatParse(
    HouseholdChatIntent Intent,
    DateOnly? CompletedOn,
    decimal? Cost,
    string? NameHint,
    bool FutureDate);

/// <summary>
/// 从一句话里取出家务意图、相对日期和费用。识别不出就保持未识别，不猜测成写入。
/// </summary>
internal static partial class HouseholdChatPhrase
{
    public const string UnrecognizedMessage = "没听懂是要记下家务，还是要查询。请直接说做了什么，或问什么时候换过、最近哪些要到期。";

    private static readonly HashSet<string> StopFragments = new(StringComparer.Ordinal)
    {
        "今天", "昨天", "前天", "明天", "后天", "上周", "这个", "什么", "时候",
        "最近", "到期", "哪些", "一下", "一个", "做了", "换了", "花了", "完成"
    };

    public static HouseholdChatParse Parse(string? text, DateOnly today)
    {
        var raw = (text ?? "").Trim();
        if (raw.Length == 0)
            return new HouseholdChatParse(HouseholdChatIntent.Unrecognized, null, null, null, false);

        if (IsUpcoming(raw))
            return new HouseholdChatParse(HouseholdChatIntent.Upcoming, null, null, null, false);

        if (IsHistory(raw))
        {
            var subject = ExtractHistorySubject(raw);
            return new HouseholdChatParse(HouseholdChatIntent.History, null, null, subject, false);
        }

        if (!IsRecord(raw))
            return new HouseholdChatParse(HouseholdChatIntent.Unrecognized, null, null, null, false);

        var cost = ReadCost(raw);
        var date = ReadDate(raw, today);
        var completedOn = date ?? today;
        var hint = ExtractRecordName(raw);
        if (string.IsNullOrWhiteSpace(hint))
            return new HouseholdChatParse(HouseholdChatIntent.Unrecognized, null, null, null, false);

        return new HouseholdChatParse(
            HouseholdChatIntent.Record,
            completedOn,
            cost,
            hint,
            completedOn > today);
    }

    public static bool Matches(string utterance, string? name, string? location, IEnumerable<string>? aliases) =>
        Score(utterance, name, location, aliases) >= 2;

    /// <summary>
    /// 名称或别名里，出现在这句话中的最长片段。整名命中高于只蹭到「滤网」这类短词。
    /// </summary>
    public static int Score(string utterance, string? name, string? location, IEnumerable<string>? aliases)
    {
        var text = Compact(utterance);
        if (text.Length == 0)
            return 0;
        var best = FieldScore(text, name);
        if (aliases != null)
        {
            foreach (var alias in aliases)
                best = Math.Max(best, FieldScore(text, alias));
        }

        var combined = Compact(location) + Compact(name);
        if (combined.Length >= 2 && text.Contains(combined, StringComparison.Ordinal))
            best = Math.Max(best, combined.Length);
        return best;
    }

    private static bool IsUpcoming(string text) =>
        text.Contains("最近要到期", StringComparison.Ordinal)
        || text.Contains("要到期的有哪些", StringComparison.Ordinal)
        || text.Contains("哪些要到期", StringComparison.Ordinal)
        || text.Contains("快到期", StringComparison.Ordinal);

    private static bool IsHistory(string text) =>
        text.Contains("什么时候", StringComparison.Ordinal);

    private static bool IsRecord(string text) =>
        text.Contains("换了", StringComparison.Ordinal)
        || text.Contains("更换", StringComparison.Ordinal)
        || text.Contains("做了", StringComparison.Ordinal)
        || text.Contains("完成了", StringComparison.Ordinal)
        || text.Contains("洗了", StringComparison.Ordinal)
        || text.Contains("保养", StringComparison.Ordinal)
        || text.Contains("花了", StringComparison.Ordinal);

    private static string? ExtractHistorySubject(string text)
    {
        var value = text;
        foreach (var word in new[] { "什么时候", "换过", "换的", "做过", "做的", "完成的", "保养过", "了", "吗", "呢", "？" , "?" })
            value = value.Replace(word, "", StringComparison.Ordinal);
        value = Punctuation().Replace(value, "");
        var compact = value.Trim();
        return compact.Length == 0 ? null : compact;
    }

    private static string ExtractRecordName(string text)
    {
        var value = CostPattern().Replace(text, "");
        value = DatePattern().Replace(value, "");
        foreach (var word in new[] { "更换了", "更换", "换了", "做了", "完成了", "洗了", "花了", "给" })
            value = value.Replace(word, "", StringComparison.Ordinal);
        value = Punctuation().Replace(value, "");
        return value.Trim();
    }

    private static decimal? ReadCost(string text)
    {
        var match = CostPattern().Match(text);
        if (!match.Success)
            return null;
        return decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static DateOnly? ReadDate(string text, DateOnly today)
    {
        var match = DatePattern().Match(text);
        if (!match.Success)
            return null;
        var token = match.Value;
        if (token is "今天")
            return today;
        if (token is "昨天")
            return today.AddDays(-1);
        if (token is "前天")
            return today.AddDays(-2);
        if (token is "明天")
            return today.AddDays(1);
        if (token is "后天")
            return today.AddDays(2);
        if (token.StartsWith("上周", StringComparison.Ordinal) && token.Length >= 3)
            return LastWeekday(today, token[^1]);

        if (token.Contains('年', StringComparison.Ordinal))
        {
            var parts = ChineseDate().Match(token);
            if (parts.Success)
                return new DateOnly(int.Parse(parts.Groups[1].Value), int.Parse(parts.Groups[2].Value), int.Parse(parts.Groups[3].Value));
        }

        if (token.Contains('-', StringComparison.Ordinal) && DateOnly.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
            return iso;

        var monthDay = MonthDay().Match(token);
        if (monthDay.Success)
            return new DateOnly(today.Year, int.Parse(monthDay.Groups[1].Value), int.Parse(monthDay.Groups[2].Value));

        return null;
    }

    private static DateOnly LastWeekday(DateOnly today, char weekday)
    {
        var fromMonday = weekday switch
        {
            '一' => 0,
            '二' => 1,
            '三' => 2,
            '四' => 3,
            '五' => 4,
            '六' => 5,
            _ => 6
        };
        var daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
        var thisMonday = today.AddDays(-daysFromMonday);
        return thisMonday.AddDays(-7 + fromMonday);
    }

    private static int FieldScore(string text, string? field)
    {
        var value = Compact(field);
        if (value.Length < 2)
            return 0;
        for (var len = value.Length; len >= 2; len--)
        {
            for (var i = 0; i <= value.Length - len; i++)
            {
                var slice = value.Substring(i, len);
                if (StopFragments.Contains(slice))
                    continue;
                if (text.Contains(slice, StringComparison.Ordinal))
                    return len;
            }
        }
        return 0;
    }

    private static string Compact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return new string(value.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToLowerInvariant();
    }

    [GeneratedRegex(@"(?:花了|花费|费用)\s*(\d+(?:\.\d{1,2})?)\s*元?")]
    private static partial Regex CostPattern();

    [GeneratedRegex(@"上周[一二三四五六日天]|今天|昨天|前天|明天|后天|\d{4}年\d{1,2}月\d{1,2}日|\d{4}-\d{2}-\d{2}|\d{1,2}月\d{1,2}日")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(\d{4})年(\d{1,2})月(\d{1,2})日")]
    private static partial Regex ChineseDate();

    [GeneratedRegex(@"(\d{1,2})月(\d{1,2})日")]
    private static partial Regex MonthDay();

    [GeneratedRegex(@"[，,。！!？?、]+")]
    private static partial Regex Punctuation();
}
