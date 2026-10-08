namespace MiraiNote.Core.Services;

/// <summary>
/// 随发版写入的功能句。发版时在 <see cref="All"/> 里加一条，并写上开始日期。
/// 不要每天补句，也不要做管理后台。
/// 从开始日起按 Asia/Shanghai 日历日显示 7 天（含开始日），满 7 天后不再出现。
/// </summary>
public static class FeatureLaunchNotes
{
    public const int VisibleDays = 7;

    /// <summary>
    /// 发版时在这里追加。例如：
    /// new(new DateOnly(2026, 10, 8), "MiraiAI 可以在对话里接着上次的文件继续做。")
    /// </summary>
    public static readonly IReadOnlyList<FeatureLaunchNote> All = [];

    /// <summary>
    /// 选出仍在有效期内的一句。多条重叠时用开始日最晚的那条。没有则返回 null。
    /// </summary>
    public static string? Select(IReadOnlyList<FeatureLaunchNote> notes, DateOnly today)
    {
        string? text = null;
        var best = DateOnly.MinValue;
        foreach (var note in notes)
        {
            if (string.IsNullOrWhiteSpace(note.Text))
                continue;
            if (today < note.StartDate || today >= note.StartDate.AddDays(VisibleDays))
                continue;
            if (text != null && note.StartDate < best)
                continue;
            best = note.StartDate;
            text = note.Text.Trim();
        }

        return text;
    }
}

public sealed record FeatureLaunchNote(DateOnly StartDate, string Text);

/// <summary>把功能句列表交给欢迎语服务。单独成类型，避免被依赖注入当成空集合。</summary>
public sealed class FeatureLaunchCatalog
{
    public static FeatureLaunchCatalog Release { get; } = new(FeatureLaunchNotes.All);

    public FeatureLaunchCatalog(IReadOnlyList<FeatureLaunchNote> notes)
    {
        Notes = notes;
    }

    public IReadOnlyList<FeatureLaunchNote> Notes { get; }
}
