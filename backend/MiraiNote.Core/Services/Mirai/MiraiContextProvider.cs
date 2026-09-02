using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;

namespace MiraiNote.Core.Services.Mirai;

/// <summary>
/// context 会话上下文注入（Mirai M1）：会话挂载的业务对象在发消息前装配为对象快照，
/// 注入 system prompt，使对话围绕该对象展开。快照不持久化，每次发消息按当前实体状态重建。
/// </summary>
public interface IMiraiContextProvider
{
    /// <summary>挂载对象是否存在（会话创建校验用，404/400 判定）。</summary>
    Task<bool> AttachTargetExistsAsync(
        int userId, string attachToType, int attachToObjectId, CancellationToken ct = default);

    /// <summary>
    /// 装配对象快照文本（Markdown，注入 system prompt）。
    /// 对象不存在（挂载后被删除）时返回 null，对话退化为普通会话，不报错。
    /// </summary>
    Task<string?> BuildSnapshotAsync(
        int userId, string attachToType, int attachToObjectId, CancellationToken ct = default);
}

/// <inheritdoc />
public class MiraiContextProvider : IMiraiContextProvider
{
    /// <summary>挂载对象允许的类型（与契约 AttachToType 一致）。</summary>
    public static readonly HashSet<string> AttachTypes = new(StringComparer.Ordinal)
        { "worklog", "lifelog", "memo", "inbox", "briefing" };

    private const int MaxContentChars = 8_000;

    private readonly MiraiNoteDbContext _db;

    public MiraiContextProvider(MiraiNoteDbContext db)
    {
        _db = db;
    }

    public async Task<bool> AttachTargetExistsAsync(
        int userId, string attachToType, int attachToObjectId, CancellationToken ct = default)
    {
        if (!AttachTypes.Contains(attachToType)) return false;
        return attachToType switch
        {
            "worklog" => await _db.WorkLogs
                .AnyAsync(w => w.Id == attachToObjectId && w.UserId == userId, ct),
            "lifelog" => await _db.LifeLogs
                .AnyAsync(l => l.Id == attachToObjectId && l.UserId == userId, ct),
            "memo" => await _db.Memos
                .AnyAsync(m => m.Id == attachToObjectId && m.UserId == userId, ct),
            "inbox" => await _db.InboxItems
                .AnyAsync(i => i.Id == attachToObjectId && i.UserId == userId, ct),
            "briefing" => await _db.DailyBriefings
                .AnyAsync(b => b.Id == attachToObjectId && b.UserId == userId, ct),
            _ => false
        };
    }

    public async Task<string?> BuildSnapshotAsync(
        int userId, string attachToType, int attachToObjectId, CancellationToken ct = default)
    {
        var body = attachToType switch
        {
            "worklog" => await BuildWorkLogSectionAsync(userId, attachToObjectId, ct),
            "lifelog" => await BuildLifeLogSectionAsync(userId, attachToObjectId, ct),
            "memo" => await BuildMemoSectionAsync(userId, attachToObjectId, ct),
            "inbox" => await BuildInboxSectionAsync(userId, attachToObjectId, ct),
            "briefing" => await BuildBriefingSectionAsync(userId, attachToObjectId, ct),
            _ => null
        };
        return body == null ? null : $"【当前挂载对象】\n{body}";
    }

    private async Task<string?> BuildWorkLogSectionAsync(int userId, int id, CancellationToken ct)
    {
        var w = await _db.WorkLogs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (w == null) return null;
        var lines = new List<string>
        {
            $"类型：工作记录（worklog）#{w.Id}《{w.Title}》",
            $"日期：{w.LogDate:yyyy-MM-dd}"
        };
        if (!string.IsNullOrWhiteSpace(w.Category)) lines.Add($"分类：{w.Category}");
        if (!string.IsNullOrWhiteSpace(w.Tags)) lines.Add($"标签：{w.Tags}");
        if (!string.IsNullOrWhiteSpace(w.Purpose)) lines.Add($"目的：{w.Purpose}");
        if (!string.IsNullOrWhiteSpace(w.Content))
        {
            lines.Add("内容：");
            lines.Add(Truncate(w.Content));
        }
        return string.Join("\n", lines);
    }

    private async Task<string?> BuildLifeLogSectionAsync(int userId, int id, CancellationToken ct)
    {
        var l = await _db.LifeLogs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (l == null) return null;
        var lines = new List<string>
        {
            $"类型：生活记录（lifelog）#{l.Id}",
            $"日期：{l.LogDate:yyyy-MM-dd}"
        };
        if (!string.IsNullOrWhiteSpace(l.Mood)) lines.Add($"心情：{l.Mood}");
        lines.Add("内容：");
        lines.Add(Truncate(l.Content));
        return string.Join("\n", lines);
    }

    private async Task<string?> BuildMemoSectionAsync(int userId, int id, CancellationToken ct)
    {
        var m = await _db.Memos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (m == null) return null;
        var lines = new List<string?>
        {
            $"类型：{(m.RemindAt != null ? "任务" : "备忘")}（memo）#{m.Id}",
            $"板块：{(m.Section == "life" ? "生活" : "工作")}",
            m.RemindAt.HasValue ? $"到期：{m.RemindAt:yyyy-MM-dd HH:mm} UTC" : null,
            $"状态：{(m.IsDone ? "已完成" : "未完成")}；优先级：{m.Priority}",
            "内容：",
            Truncate(m.Content)
        };
        return string.Join("\n", lines.Where(x => x != null));
    }

    private async Task<string?> BuildInboxSectionAsync(int userId, int id, CancellationToken ct)
    {
        var i = await _db.InboxItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (i == null) return null;
        var lines = new List<string>
        {
            $"类型：捕获项（inbox）#{i.Id}（状态 {i.Status}）",
            "原始输入：",
            Truncate(i.Raw)
        };
        if (!string.IsNullOrWhiteSpace(i.AiParse))
            lines.Add($"AI 分拣结果（JSON）：\n{Truncate(i.AiParse)}");
        return string.Join("\n", lines);
    }

    private async Task<string?> BuildBriefingSectionAsync(int userId, int id, CancellationToken ct)
    {
        var b = await _db.DailyBriefings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (b == null) return null;
        return $"类型：晨报（briefing）#{b.Id}（{b.BriefDate:yyyy-MM-dd}）\n正文：\n{Truncate(b.Content)}";
    }

    private static string Truncate(string value) =>
        value.Length <= MaxContentChars ? value : value[..MaxContentChars] + "\n…（已截断）";
}
