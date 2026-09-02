using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Mirai;

namespace MiraiNote.Core.Services.Mirai;

/// <summary>晨报获取结果：成功时 Briefing 有值；生成失败/进行中时 Error 有值。</summary>
public sealed record BriefingOutcome(BriefingDto? Briefing, string? Error);

/// <summary>
/// 晨报业务：事实聚合（EF LINQ）→ prompt → 落库。
/// 每用户每日一条（过滤唯一索引 + 占位行防并发）；regenerate 限 3 次/日（429）。
/// </summary>
public interface IBriefingService
{
    /// <summary>GET /mirai/day/overview 触发路径：当日无晨报则生成；失败不抛错（降级为 briefingError）。</summary>
    Task<BriefingOutcome> GetOrGenerateAsync(int userId, DateOnly date, int tzOffsetMinutes, CancellationToken ct = default);

    /// <summary>POST /mirai/briefing/regenerate：强制重生成；超每日限额抛 429。</summary>
    Task<BriefingDto> RegenerateAsync(int userId, DateOnly date, int tzOffsetMinutes, CancellationToken ct = default);
}

/// <inheritdoc />
public class BriefingService : IBriefingService
{
    private const int RegenerateDailyLimit = 3;
    private static readonly TimeSpan GenerateTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ConcurrencyWaitLimit = TimeSpan.FromSeconds(25);

    private readonly MiraiNoteDbContext _db;
    private readonly DeepSeekOptions _deepSeekOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BriefingService> _logger;

    public BriefingService(
        MiraiNoteDbContext db,
        IOptions<DeepSeekOptions> deepSeekOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<BriefingService> logger)
    {
        _db = db;
        _deepSeekOptions = deepSeekOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ===== GET overview 触发路径 =====

    public async Task<BriefingOutcome> GetOrGenerateAsync(
        int userId, DateOnly date, int tzOffsetMinutes, CancellationToken ct = default)
    {
        var existing = await FindLiveAsync(userId, date, ct);
        if (existing != null)
        {
            // 占位行（Content 为空）：另一请求正在生成，等待其完成。
            return existing.Content.Length > 0
                ? new BriefingOutcome(MapDto(existing), null)
                : await WaitForConcurrentGenerationAsync(userId, date, ct);
        }

        // 占位行防并发：依赖 (UserId, BriefDate) 过滤唯一索引，插入冲突即说明并发请求已抢到。
        var placeholder = new DailyBriefing
        {
            UserId = userId,
            BriefDate = date,
            Content = string.Empty,
            GeneratedAt = DateTime.UtcNow
        };
        _db.DailyBriefings.Add(placeholder);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogInformation("晨报占位行插入冲突（用户 {UserId} 日期 {Date}），等待并发生成：{Message}",
                userId, date, ex.Message);
            return await WaitForConcurrentGenerationAsync(userId, date, ct);
        }

        var (content, sources) = await GenerateCoreAsync(userId, date, tzOffsetMinutes, ct);
        if (content == null)
        {
            // 生成失败：软删占位行（下次 GET 可重试），本请求降级为 briefingError，不影响纯数据区。
            placeholder.IsDeleted = true;
            await _db.SaveChangesAsync(ct);
            return new BriefingOutcome(null, "晨报生成失败，请稍后重试或手动重新生成");
        }

        placeholder.Content = content;
        placeholder.SourcesJson = JsonSerializer.Serialize(sources, MiraiJson.Options);
        placeholder.Model = _deepSeekOptions.Model;
        placeholder.GeneratedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new BriefingOutcome(MapDto(placeholder), null);
    }

    // ===== 强制重生成 =====

    public async Task<BriefingDto> RegenerateAsync(
        int userId, DateOnly date, int tzOffsetMinutes, CancellationToken ct = default)
    {
        // 每日限额：按 UTC 日统计 briefing_regenerate 动作（Decision=applied）。
        var utcDayStart = DateTime.UtcNow.Date;
        var regenerateCountToday = await _db.AIActionLogs
            .AsNoTracking()
            .CountAsync(l => l.UserId == userId
                && l.ActionType == AIActionLog.ActionTypeBriefingRegenerate
                && l.Decision == "applied"
                && l.CreatedAt >= utcDayStart, ct);
        if (regenerateCountToday >= RegenerateDailyLimit)
            throw new BusinessException($"晨报重生成已达每日上限（{RegenerateDailyLimit} 次）", 429);

        var (content, sources) = await GenerateCoreAsync(userId, date, tzOffsetMinutes, ct);
        if (content == null)
            throw new BusinessException("晨报生成失败，请稍后重试", 500);

        // 旧行软删后插入新行（过滤唯一索引允许 IsDeleted=1 共存）。
        var existing = await _db.DailyBriefings
            .FirstOrDefaultAsync(b => b.UserId == userId && b.BriefDate == date, ct);
        if (existing != null) existing.IsDeleted = true;

        var briefing = new DailyBriefing
        {
            UserId = userId,
            BriefDate = date,
            Content = content,
            SourcesJson = JsonSerializer.Serialize(sources, MiraiJson.Options),
            Model = _deepSeekOptions.Model,
            GeneratedAt = DateTime.UtcNow
        };
        _db.DailyBriefings.Add(briefing);
        await _db.SaveChangesAsync(ct); // 先保存拿到 briefing.Id

        _db.AIActionLogs.Add(new AIActionLog
        {
            UserId = userId,
            ActionType = AIActionLog.ActionTypeBriefingRegenerate,
            IntentDesc = $"重生成晨报 {date:yyyy-MM-dd}",
            TargetType = "briefing",
            TargetId = briefing.Id,
            PayloadJson = JsonSerializer.Serialize(new { date = date.ToString("yyyy-MM-dd") }, MiraiJson.Options),
            Decision = "applied",
            DecidedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return MapDto(briefing);
    }

    // ===== 生成核心 =====

    /// <summary>聚合事实 → 调 DeepSeek → (content, sources)。失败返回 (null, null)。</summary>
    private async Task<(string? Content, List<SourceRefDto> Sources)> GenerateCoreAsync(
        int userId, DateOnly date, int tzOffsetMinutes, CancellationToken ct)
    {
        try
        {
            var facts = await GatherFactsAsync(userId, date, tzOffsetMinutes, ct);

            // 全空事实前置短路：跳过 LLM（推理模型对全空事实块思考不收敛，见 tools/eval/REPORT.md b06）
            if (facts.DueTasks.Count == 0 && facts.OverdueTasks.Count == 0
                && facts.YesterdayWorklogs.Count == 0 && facts.YesterdayLifelogCount == 0
                && facts.InboxBacklogCount == 0 && facts.RelatedHistory.Count == 0)
            {
                return ("今天没有到期任务，昨日也没有新记录，收件箱无积压。", new List<SourceRefDto>());
            }

            var userPrompt = BuildUserPrompt(date, facts);

            if (string.IsNullOrWhiteSpace(_deepSeekOptions.ApiKey))
                throw new HttpRequestException("DeepSeek API Key 未配置");

            using var client = DeepSeekJsonClient.CreateAuthorizedClient(
                _httpClientFactory, _deepSeekOptions.BaseUrl, _deepSeekOptions.ApiKey);
            var messages = new List<object>
            {
                new { role = "system", content = MiraiPrompts.BriefingSystemPrompt },
                new { role = "user", content = userPrompt }
            };
            var content = await DeepSeekJsonClient.CompleteAsync(
                client, _deepSeekOptions.Model, messages,
                temperature: 0.3, maxTokens: 6000, jsonObject: false,
                timeout: GenerateTimeout, ct);

            if (string.IsNullOrWhiteSpace(content))
                throw new JsonException("晨报内容为空");

            return (content.Trim(), ExtractSources(content, facts));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning("晨报生成失败（用户 {UserId} 日期 {Date}）：{Message}", userId, date, ex.Message);
            return (null, new List<SourceRefDto>());
        }
    }

    /// <summary>事实聚合（全部 EF LINQ，不含任何模型推断内容）。</summary>
    private async Task<BriefingFactSet> GatherFactsAsync(
        int userId, DateOnly date, int tzOffsetMinutes, CancellationToken ct)
    {
        var (dayStartUtc, dayEndUtc) = MiraiTime.LocalDayRangeUtc(date, tzOffsetMinutes);
        var dueTasks = await MiraiQueries.GetDueTasksAsync(_db, userId, dayStartUtc, dayEndUtc, ct);
        var overdueTasks = await MiraiQueries.GetOverdueTasksAsync(_db, userId, dayStartUtc, ct);

        var yesterday = date.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var yesterdayWorklogs = await _db.WorkLogs
            .AsNoTracking()
            .Where(w => w.UserId == userId && w.LogDate == yesterday)
            .OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.Title })
            .ToListAsync(ct);

        var yesterdayLifelogs = await _db.LifeLogs
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.LogDate == yesterday)
            .Select(l => l.Mood)
            .ToListAsync(ct);

        // 本周统计：记录数 / 完成任务数（近似：IsDone 且本周内更新）/ 分类 top3
        var (weekStart, weekEnd) = MiraiTime.LocalWeekRange(date);
        var weekStartUtc = MiraiTime.LocalDayRangeUtc(weekStart, tzOffsetMinutes).StartUtc;
        var weekEndUtc = MiraiTime.LocalDayRangeUtc(weekEnd, tzOffsetMinutes).EndUtc;
        var weekWorkLogs = await _db.WorkLogs
            .AsNoTracking()
            .Where(w => w.UserId == userId
                && w.LogDate >= weekStart.ToDateTime(TimeOnly.MinValue)
                && w.LogDate <= weekEnd.ToDateTime(TimeOnly.MinValue))
            .Select(w => new { w.Category, w.LogDate })
            .ToListAsync(ct);
        var weekLifeLogCount = await _db.LifeLogs
            .AsNoTracking()
            .CountAsync(l => l.UserId == userId
                && l.LogDate >= weekStart.ToDateTime(TimeOnly.MinValue)
                && l.LogDate <= weekEnd.ToDateTime(TimeOnly.MinValue), ct);
        var weekDoneCount = await _db.Memos
            .AsNoTracking()
            .CountAsync(m => m.UserId == userId && m.IsDone && !m.IsArchived
                && m.UpdatedAt >= weekStartUtc && m.UpdatedAt < weekEndUtc, ct);

        var backlogItems = await _db.InboxItems
            .AsNoTracking()
            .Where(i => i.UserId == userId
                && i.Status != (byte)InboxStatus.Dispatched
                && i.Status != (byte)InboxStatus.Discarded)
            .Select(i => i.CreatedAt)
            .ToListAsync(ct);
        var oldestBacklog = backlogItems.Count == 0 ? (DateTime?)null : backlogItems.Min();

        var relatedHistory = await GetRelatedHistoryAsync(userId, date, dueTasks, ct);

        return new BriefingFactSet(
            dueTasks,
            overdueTasks,
            yesterdayWorklogs.Select(w => (w.Id, w.Title)).ToList(),
            yesterdayLifelogs.Count,
            yesterdayLifelogs.Where(m => !string.IsNullOrEmpty(m)).ToList()!,
            weekWorkLogs.Count + weekLifeLogCount,
            weekDoneCount,
            weekWorkLogs
                .Where(w => !string.IsNullOrEmpty(w.Category))
                .GroupBy(w => w.Category!, StringComparer.Ordinal)
                .Select(g => (Category: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Category, StringComparer.Ordinal)
                .Take(3)
                .ToList(),
            backlogItems.Count,
            oldestBacklog.HasValue ? DateTime.UtcNow - oldestBacklog.Value : null,
            relatedHistory);
    }

    /// <summary>到期任务内容关键词在近 30 天 WorkLog 的命中摘要（每条 ≤100 字）。</summary>
    private async Task<List<(int TaskId, string Excerpt, int WorkLogId, string WorkLogTitle)>> GetRelatedHistoryAsync(
        int userId, DateOnly date, List<DueTaskDto> dueTasks, CancellationToken ct)
    {
        var results = new List<(int, string, int, string)>();
        if (dueTasks.Count == 0) return results;

        var since = date.AddDays(-30).ToDateTime(TimeOnly.MinValue);
        var recentWorkLogs = await _db.WorkLogs
            .AsNoTracking()
            .Where(w => w.UserId == userId && w.LogDate >= since && w.LogDate <= date.ToDateTime(TimeOnly.MinValue))
            .OrderByDescending(w => w.LogDate)
            .Select(w => new { w.Id, w.Title, w.Content })
            .ToListAsync(ct);
        if (recentWorkLogs.Count == 0) return results;

        foreach (var task in dueTasks.Take(8))
        {
            var keywords = ExtractKeywords(task.Content);
            var hit = recentWorkLogs.FirstOrDefault(w =>
                keywords.Any(k => w.Title.Contains(k, StringComparison.Ordinal)
                    || (w.Content != null && w.Content.Contains(k, StringComparison.Ordinal))));
            if (hit == null) continue;

            var excerpt = Regex.Replace(hit.Content ?? hit.Title, @"\s+", " ").Trim();
            results.Add((task.Id, excerpt.Length > 100 ? excerpt[..100] : excerpt, hit.Id, hit.Title));
        }
        return results;
    }

    /// <summary>从任务内容提取检索关键词（中文按 2 字以上片段，取最长的 3 个）。</summary>
    private static List<string> ExtractKeywords(string content)
    {
        var segments = Regex.Split(content, @"[\s,，。.、;；:：!！?？()（）\[\]【】""'']+
")
            .Where(s => s.Length >= 2)
            .OrderByDescending(s => s.Length)
            .Take(3)
            .ToList();
        return segments;
    }

    private static string BuildUserPrompt(DateOnly date, BriefingFactSet facts)
    {
        string FormatTask(DueTaskDto t) =>
            $"- #{t.Id} [{(t.Priority == 3 ? "高" : t.Priority == 2 ? "中" : "低")}][{t.Section}] {t.Content}" +
            (t.RemindAt.HasValue ? $"（{t.RemindAt:yyyy-MM-dd HH:mm} UTC 到期）" : "");

        var dueTasksText = facts.DueTasks.Count == 0
            ? "（无）"
            : string.Join("\n", facts.DueTasks.Select(FormatTask));
        var overdueText = facts.OverdueTasks.Count == 0
            ? "（无）"
            : string.Join("\n", facts.OverdueTasks.Select(FormatTask));
        var yesterdayWorklogsText = facts.YesterdayWorklogs.Count == 0
            ? "（无记录）"
            : string.Join("\n", facts.YesterdayWorklogs.Select(w => $"- #{w.Id} {w.Title}"));
        var weekStatsText =
            $"本周记录 {facts.WeekEntryCount} 条；完成任务 {facts.WeekDoneCount} 件" +
            (facts.WeekCategoryTop3.Count > 0
                ? "；分类分布：" + string.Join("、", facts.WeekCategoryTop3.Select(c => $"{c.Category}×{c.Count}"))
                : "");
        var inboxBacklogText = facts.InboxBacklogCount == 0
            ? "收件箱无积压"
            : $"收件箱积压 {facts.InboxBacklogCount} 条，最早积压 {Math.Max(0, (int)Math.Floor(facts.OldestBacklogAge?.TotalDays ?? 0))} 天";
        var relatedText = facts.RelatedHistory.Count == 0
            ? "（无）"
            : string.Join("\n", facts.RelatedHistory.Select(h =>
                $"- 任务 #{h.TaskId} 相关：#{h.WorkLogId}《{h.WorkLogTitle}》{h.Excerpt}"));

        return MiraiPrompts.BriefingUserPrompt
            .Replace("{{date}}", date.ToString("yyyy-MM-dd"))
            .Replace("{{weekday}}", MiraiTime.WeekdayCn(date))
            .Replace("{{dueTasks}}", dueTasksText)
            .Replace("{{overdueTasks}}", overdueText)
            .Replace("{{yesterdayWorklogs}}", yesterdayWorklogsText)
            .Replace("{{weekStats}}", weekStatsText)
            .Replace("{{inboxBacklog}}", inboxBacklogText)
            .Replace("{{relatedHistory}}", relatedText);
    }

    /// <summary>
    /// 从生成内容解析 【来源: 标题 #Id】 标注 → 匹配已知事实，构建溯源清单；
    /// 未解析到标注时回退为全部事实来源。
    /// </summary>
    private static List<SourceRefDto> ExtractSources(string content, BriefingFactSet facts)
    {
        var known = new Dictionary<int, SourceRefDto>();
        foreach (var task in facts.DueTasks.Concat(facts.OverdueTasks))
            known.TryAdd(task.Id, new SourceRefDto("memo", task.Id, Truncate(task.Content, 50)));
        foreach (var worklog in facts.YesterdayWorklogs)
            known.TryAdd(worklog.Id, new SourceRefDto("worklog", worklog.Id, worklog.Title));

        var cited = new List<SourceRefDto>();
        foreach (Match match in Regex.Matches(content, @"【来源[:：]\s*[^#【】]*#(\d+)】"))
        {
            if (int.TryParse(match.Groups[1].Value, out var id) && known.TryGetValue(id, out var source))
                cited.Add(source);
        }

        return cited.Count > 0
            ? cited.DistinctBy(s => (s.Type, s.Id)).ToList()
            : known.Values.ToList();
    }

    /// <summary>等待并发请求完成占位行填充（500ms 轮询，上限 25s）。</summary>
    private async Task<BriefingOutcome> WaitForConcurrentGenerationAsync(
        int userId, DateOnly date, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + ConcurrencyWaitLimit;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500, ct);
            var row = await FindLiveAsync(userId, date, ct);
            if (row == null)
                return new BriefingOutcome(null, "晨报生成失败，请稍后重试或手动重新生成");
            if (row.Content.Length > 0)
                return new BriefingOutcome(MapDto(row), null);
        }
        return new BriefingOutcome(null, "晨报正在生成中，请稍后刷新");
    }

    private Task<DailyBriefing?> FindLiveAsync(int userId, DateOnly date, CancellationToken ct)
        => _db.DailyBriefings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == userId && b.BriefDate == date, ct);

    private static BriefingDto MapDto(DailyBriefing b) => new(
        b.Id,
        b.BriefDate,
        b.Content,
        DeserializeSources(b.SourcesJson),
        b.Model ?? string.Empty,
        b.GeneratedAt);

    private static List<SourceRefDto> DeserializeSources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<SourceRefDto>();
        try
        {
            return JsonSerializer.Deserialize<List<SourceRefDto>>(json, MiraiJson.Options)
                ?? new List<SourceRefDto>();
        }
        catch (JsonException)
        {
            return new List<SourceRefDto>();
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>晨报事实集合（纯 SQL 聚合结果，不含模型推断）。</summary>
    private sealed record BriefingFactSet(
        List<DueTaskDto> DueTasks,
        List<DueTaskDto> OverdueTasks,
        List<(int Id, string Title)> YesterdayWorklogs,
        int YesterdayLifelogCount,
        List<string> YesterdayMoods,
        int WeekEntryCount,
        int WeekDoneCount,
        List<(string Category, int Count)> WeekCategoryTop3,
        int InboxBacklogCount,
        TimeSpan? OldestBacklogAge,
        List<(int TaskId, string Excerpt, int WorkLogId, string WorkLogTitle)> RelatedHistory);
}
