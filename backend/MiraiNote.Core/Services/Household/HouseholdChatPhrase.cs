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
    bool FutureDate,
    bool InvalidInput = false);

/// <summary>
/// 从一句话里取出家务意图、相对日期和费用。识别不出就保持未识别，不猜测成写入。
/// </summary>
internal static partial class HouseholdChatPhrase
{
    public const string UnrecognizedMessage = "没听懂是要记下家务，还是要查询。请直接说做了什么，或问什么时候换过、最近哪些要到期。";
    public const string RephraseMessage = "日期或金额不合理，请换一种说法。";

    private static readonly HashSet<string> StopFragments = new(StringComparer.Ordinal)
    {
        "今天", "昨天", "前天", "明天", "后天", "上周", "这个", "什么", "时候",
        "最近", "到期", "哪些", "一下", "一个", "做了", "换了", "花了", "完成"
    };

    /// <summary>
    /// 只靠这些泛词，或只蹭到两个字，不算匹配。整段名称或别名仍可以命中。
    /// </summary>
    private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "厨房", "阳台", "卫生间", "客厅", "滤芯", "滤网", "过滤", "棉"
    };

    public static HouseholdChatParse Parse(string? text, DateOnly today)
    {
        var raw = NormalizeAmounts((text ?? "").Trim());
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
        if (cost.Invalid || date.Invalid)
            return new HouseholdChatParse(HouseholdChatIntent.Unrecognized, null, null, null, false, true);

        var completedOn = date.Date ?? today;
        var hint = ExtractRecordName(raw);
        if (string.IsNullOrWhiteSpace(hint))
            return new HouseholdChatParse(HouseholdChatIntent.Unrecognized, null, null, null, false);

        return new HouseholdChatParse(
            HouseholdChatIntent.Record,
            completedOn,
            cost.Amount,
            hint,
            completedOn > today);
    }

    public static bool Matches(string utterance, string? name, string? location, IEnumerable<string>? aliases) =>
        Score(utterance, name, location, aliases) >= 2;

    /// <summary>
    /// 去掉空白并忽略大小写后，这句话抽出的名称与事项名或别名完全相同。
    /// </summary>
    public static bool IsExactName(string? nameHint, string? name, IEnumerable<string>? aliases)
    {
        var key = Compact(nameHint);
        if (key.Length < 2)
            return false;
        if (Compact(name) == key)
            return true;
        if (aliases == null)
            return false;
        foreach (var alias in aliases)
        {
            if (Compact(alias) == key)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 名称或别名整段出现在这句话里才算。泛词和两个字的局部重叠记 0。
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

        var place = Compact(location);
        if (place.Length > 2 && !GenericWords.Contains(place))
            best = Math.Max(best, FieldScore(text, place));
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

    /// <summary>动词前的时间或体貌副词。长的在前，避免「刚刚」被拆成「刚」。</summary>
    private static readonly string[] TimeAspectAdverbs =
    [
        "刚刚", "刚才", "今天", "昨天", "前天", "已经", "刚", "又", "才"
    ];

    private static readonly string[] RecordVerbs = ["更换了", "更换", "换了", "做了", "完成了", "洗了", "花了"];

    private static string ExtractRecordName(string text)
    {
        var value = CostPattern().Replace(text, "");
        value = DatePattern().Replace(value, "");
        value = StripTimeAspectAdverbs(value);
        foreach (var word in RecordVerbs)
            value = value.Replace(word, "", StringComparison.Ordinal);
        value = value.Replace("给", "", StringComparison.Ordinal);
        value = Punctuation().Replace(value, "");
        return value.Trim();
    }

    /// <summary>
    /// 只去掉句首、或紧挨在动词前面的副词。名称中间的「刚」不动。
    /// </summary>
    private static string StripTimeAspectAdverbs(string value)
    {
        while (true)
        {
            var next = StripOneLeadingAdverb(value);
            if (next == value)
                next = StripOneAdverbBeforeVerb(value);
            if (next == value)
                return value;
            value = next;
        }
    }

    private static string StripOneLeadingAdverb(string value)
    {
        foreach (var adverb in TimeAspectAdverbs)
        {
            if (value.StartsWith(adverb, StringComparison.Ordinal))
                return value[adverb.Length..];
        }

        return value;
    }

    private static string StripOneAdverbBeforeVerb(string value)
    {
        foreach (var verb in RecordVerbs)
        {
            var index = value.IndexOf(verb, StringComparison.Ordinal);
            if (index <= 0)
                continue;
            var prefix = value[..index];
            foreach (var adverb in TimeAspectAdverbs)
            {
                if (!prefix.EndsWith(adverb, StringComparison.Ordinal))
                    continue;
                return string.Concat(prefix.AsSpan(0, prefix.Length - adverb.Length), value.AsSpan(index));
            }
        }

        return value;
    }

    private readonly record struct CostRead(decimal? Amount, bool Invalid);

    private readonly record struct DateRead(DateOnly? Date, bool Invalid);

    private static CostRead ReadCost(string text)
    {
        var match = CostPattern().Match(text);
        if (!match.Success)
            return new CostRead(null, false);
        if (!decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return new CostRead(null, true);
        if (amount > HouseholdCost.MaxAmount)
            return new CostRead(null, true);
        return new CostRead(amount, false);
    }

    private static DateRead ReadDate(string text, DateOnly today)
    {
        var match = DatePattern().Match(text);
        if (!match.Success)
            return new DateRead(null, false);
        var token = match.Value;
        if (token is "今天")
            return new DateRead(today, false);
        if (token is "昨天")
            return new DateRead(today.AddDays(-1), false);
        if (token is "前天")
            return new DateRead(today.AddDays(-2), false);
        if (token is "明天")
            return new DateRead(today.AddDays(1), false);
        if (token is "后天")
            return new DateRead(today.AddDays(2), false);
        if (token.StartsWith("上周", StringComparison.Ordinal) && token.Length >= 3)
            return new DateRead(LastWeekday(today, token[^1]), false);

        if (token.Contains('年', StringComparison.Ordinal))
        {
            var parts = ChineseDate().Match(token);
            if (!parts.Success)
                return new DateRead(null, true);
            return TryCreateDate(
                int.Parse(parts.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(parts.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(parts.Groups[3].Value, CultureInfo.InvariantCulture),
                out var chinese)
                ? new DateRead(chinese, false)
                : new DateRead(null, true);
        }

        if (token.Contains('-', StringComparison.Ordinal))
        {
            return DateOnly.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)
                ? new DateRead(iso, false)
                : new DateRead(null, true);
        }

        var monthDay = MonthDay().Match(token);
        if (monthDay.Success)
        {
            return ResolveMonthDay(
                today,
                int.Parse(monthDay.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(monthDay.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        return new DateRead(null, false);
    }

    /// <summary>
    /// 只说月日时先取今年。晚于今天就改取去年。两年都不存在（如 2月30日）则判为无效。
    /// </summary>
    private static DateRead ResolveMonthDay(DateOnly today, int month, int day)
    {
        var thisYearOk = TryCreateDate(today.Year, month, day, out var thisYear);
        DateOnly lastYear = default;
        var lastYearOk = today.Year > 1 && TryCreateDate(today.Year - 1, month, day, out lastYear);
        if (thisYearOk && thisYear <= today)
            return new DateRead(thisYear, false);
        if ((thisYearOk && thisYear > today || !thisYearOk) && lastYearOk)
            return new DateRead(lastYear, false);
        return new DateRead(null, true);
    }

    private static bool TryCreateDate(int year, int month, int day, out DateOnly date)
    {
        date = default;
        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
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
        if (value.Length < 2 || GenericWords.Contains(value))
            return 0;
        if (text.Contains(value, StringComparison.Ordinal))
            return value.Length;

        var best = 0;
        for (var len = value.Length - 1; len >= 3; len--)
        {
            for (var i = 0; i <= value.Length - len; i++)
            {
                var slice = value.Substring(i, len);
                if (StopFragments.Contains(slice) || GenericWords.Contains(slice))
                    continue;
                if (text.Contains(slice, StringComparison.Ordinal))
                    best = Math.Max(best, len);
            }
            if (best > 0)
                return best;
        }

        return 0;
    }

    /// <summary>
    /// 全角数字和小数点先转成半角。千分位只去掉「一位数字、逗号、后面正好三位且不再跟数字」。
    /// 12，3 这种逗号留着，避免被当成 123。
    /// </summary>
    private static string NormalizeAmounts(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            if (ch is >= '０' and <= '９')
                chars[i] = (char)('0' + (ch - '０'));
            else if (ch == '，')
                chars[i] = ',';
            else if (ch == '．')
                chars[i] = '.';
        }

        var normalized = new string(chars);
        string previous;
        do
        {
            previous = normalized;
            normalized = ThousandsSeparator().Replace(normalized, "");
        }
        while (normalized != previous);

        return normalized;
    }

    private static string Compact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return new string(value.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToLowerInvariant();
    }

    [GeneratedRegex(@"(?:花了|花费|费用)\s*(\d+(?:\.\d{1,2})?)\s*元?")]
    private static partial Regex CostPattern();

    [GeneratedRegex(@"(?<=\d)[,，](?=\d{3}(?!\d))")]
    private static partial Regex ThousandsSeparator();

    [GeneratedRegex(@"上周[一二三四五六日天]|今天|昨天|前天|明天|后天|\d{4}年\d{1,2}月\d{1,2}日|\d{4}-\d{2}-\d{2}|\d{1,2}月\d{1,2}日")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(\d{4})年(\d{1,2})月(\d{1,2})日")]
    private static partial Regex ChineseDate();

    [GeneratedRegex(@"(\d{1,2})月(\d{1,2})日")]
    private static partial Regex MonthDay();

    [GeneratedRegex(@"[，,。！!？?、]+")]
    private static partial Regex Punctuation();
}
