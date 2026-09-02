using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;
using MiraiNote.Shared.Dtos.Mirai;

namespace MiraiNote.Core.Services.Mirai;

/// <summary>
/// 捕获收件箱业务：创建并同步分拣、列表、纠错重分拣、确认分发（单事务）、丢弃、撤销分发。
/// 分拣失败不抛错（Status=Error，客户端提供重试）；分发/撤销/丢弃全程写 AIActionLogs 审计。
/// </summary>
public interface IInboxTriageService
{
    /// <summary>POST /mirai/inbox：创建捕获项并同步分拣。</summary>
    Task<InboxItemDto> CreateAndTriageAsync(int userId, CreateInboxItemRequest request, CancellationToken ct = default);

    /// <summary>GET /mirai/inbox：分页列表（默认排除 Discarded，createdAt desc）。</summary>
    Task<PagedResult<InboxItemDto>> GetListAsync(int userId, int? status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>POST /mirai/inbox/{id}/retriage：纠错重分拣。</summary>
    Task<InboxItemDto> RetriageAsync(int userId, int inboxItemId, RetriageRequest request, CancellationToken ct = default);

    /// <summary>POST /mirai/inbox/{id}/dispatch：单事务创建全部目标实体 + AIActionLog + 置 Dispatched。</summary>
    Task<DispatchResultDto> DispatchAsync(int userId, int inboxItemId, DispatchRequest request, CancellationToken ct = default);

    /// <summary>POST /mirai/inbox/{id}/discard：软删 + AIActionLog(discarded)。</summary>
    Task DiscardAsync(int userId, int inboxItemId, CancellationToken ct = default);

    /// <summary>POST /mirai/inbox/{id}/undo：软删本次创建的全部实体，AIActionLog 置 undone，回 Triaged。</summary>
    Task UndoAsync(int userId, int inboxItemId, CancellationToken ct = default);
}

/// <inheritdoc />
public class InboxTriageService : IInboxTriageService
{
    private const int MaxRawLength = 2000;
    private const int MaxCorrectionLength = 500;
    private static readonly TimeSpan TriageTimeout = TimeSpan.FromSeconds(25);
    private static readonly HashSet<string> DispatchableTypes = new(StringComparer.Ordinal)
        { "task", "worklog", "lifelog" };

    private readonly MiraiNoteDbContext _db;
    private readonly DeepSeekOptions _deepSeekOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<InboxTriageService> _logger;

    public InboxTriageService(
        MiraiNoteDbContext db,
        IOptions<DeepSeekOptions> deepSeekOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<InboxTriageService> logger)
    {
        _db = db;
        _deepSeekOptions = deepSeekOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ===== 创建并同步分拣 =====

    public async Task<InboxItemDto> CreateAndTriageAsync(
        int userId, CreateInboxItemRequest request, CancellationToken ct = default)
    {
        var raw = request.Raw?.Trim() ?? string.Empty;
        if (raw.Length == 0) throw new BusinessException("raw 不能为空", 400);
        if (raw.Length > MaxRawLength) throw new BusinessException($"raw 长度不能超过 {MaxRawLength} 字符", 400);
        if (request.Source is < (int)InboxSource.HotkeyCapture or > (int)InboxSource.Retriage)
            throw new BusinessException("source 取值无效（1..4）", 400);
        if (string.IsNullOrWhiteSpace(request.LocalTime))
            throw new BusinessException("localTime 不能为空", 400);
        if (Math.Abs(request.TzOffsetMinutes) > MiraiTime.MaxTzOffsetMinutes)
            throw new BusinessException("tzOffsetMinutes 取值无效（±840 分钟内）", 400);

        var item = new InboxItem
        {
            UserId = userId,
            Raw = raw,
            Source = (byte)request.Source,
            Status = (byte)InboxStatus.Pending
        };
        _db.InboxItems.Add(item);
        await _db.SaveChangesAsync(ct);

        await TriageCoreAsync(item, request.LocalTime.Trim(), request.TzOffsetMinutes, correction: null, ct);
        return MapDto(item);
    }

    // ===== 列表 =====

    public async Task<PagedResult<InboxItemDto>> GetListAsync(
        int userId, int? status, int page, int pageSize, CancellationToken ct = default)
    {
        if (status.HasValue &&
            (status.Value < (int)InboxStatus.Pending || status.Value > (int)InboxStatus.Error))
            throw new BusinessException("status 取值无效（0..5）", 400);
        if (page < 1) throw new BusinessException("page 必须 ≥ 1", 400);
        if (pageSize is < 1 or > 200) throw new BusinessException("pageSize 取值无效（1..200）", 400);

        var query = _db.InboxItems.AsNoTracking().Where(i => i.UserId == userId);
        // 契约：默认排除 Discarded；显式传 status=4 时按请求返回。
        query = status.HasValue
            ? query.Where(i => i.Status == status.Value)
            : query.Where(i => i.Status != (byte)InboxStatus.Discarded);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<InboxItemDto>
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items.Select(MapDto).ToList()
        };
    }

    // ===== 纠错重分拣 =====

    public async Task<InboxItemDto> RetriageAsync(
        int userId, int inboxItemId, RetriageRequest request, CancellationToken ct = default)
    {
        var item = await _db.InboxItems
            .FirstOrDefaultAsync(i => i.Id == inboxItemId && i.UserId == userId, ct)
            ?? throw new BusinessException("捕获项不存在", 404);

        var correction = request.Correction?.Trim();
        if (correction?.Length > MaxCorrectionLength)
            throw new BusinessException($"纠错语不能超过 {MaxCorrectionLength} 字符", 400);

        // 换算上下文从上一次 AiParse 信封读取（若无则回退 UTC 0 偏移）。
        var envelope = TryParseEnvelope(item.AiParse);
        var localTime = envelope?.LocalTime;
        var tzOffset = envelope?.TzOffsetMinutes ?? 0;

        item.Source = (byte)InboxSource.Retriage;
        item.CorrectionNote = string.IsNullOrEmpty(correction) ? null : correction;
        item.Status = (byte)InboxStatus.Triaging;
        item.Error = null;
        item.AiParse = null;
        await _db.SaveChangesAsync(ct);

        await TriageCoreAsync(
            item,
            localTime ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
            tzOffset,
            correction,
            ct);
        return MapDto(item);
    }

    // ===== 确认分发（单事务） =====

    public async Task<DispatchResultDto> DispatchAsync(
        int userId, int inboxItemId, DispatchRequest request, CancellationToken ct = default)
    {
        if (request.Items == null || request.Items.Count == 0)
            throw new BusinessException("items 不能为空", 400);

        var item = await _db.InboxItems
            .FirstOrDefaultAsync(i => i.Id == inboxItemId && i.UserId == userId, ct)
            ?? throw new BusinessException("捕获项不存在", 404);
        if (item.Status != (byte)InboxStatus.Triaged)
            throw new BusinessException("仅 Triaged 状态的捕获项可分发", 409);

        var envelope = TryParseEnvelope(item.AiParse)
            ?? throw new BusinessException("分拣结果不可用，请先重新分拣", 422);
        var suggestions = envelope.Items ?? new List<TriageSuggestionDto>();
        var suggestionMap = suggestions
            .GroupBy(s => s.SuggestionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // 请求级校验（进入事务前全部完成）
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var planned = new List<(DispatchItemRequest Req, TriageSuggestionDto Suggestion, FieldsDto Merged)>();
        foreach (var req in request.Items)
        {
            if (string.IsNullOrWhiteSpace(req.SuggestionId))
                throw new BusinessException("suggestionId 不能为空", 400);
            if (!seenIds.Add(req.SuggestionId))
                throw new BusinessException($"suggestionId 重复：{req.SuggestionId}", 400);
            if (!suggestionMap.TryGetValue(req.SuggestionId, out var suggestion))
                throw new BusinessException($"suggestionId 不在分拣结果中：{req.SuggestionId}", 422);
            if (!DispatchableTypes.Contains(suggestion.Type))
                throw new BusinessException($"类型 {suggestion.Type} 的建议不可分发", 422);

            ValidateOverrideKeys(suggestion.Type, req.Overrides);
            var merged = MergeFields(suggestion.Fields, req.Overrides);
            ValidateMergedFields(suggestion.Type, merged);
            planned.Add((req, suggestion, merged));
        }

        // 单事务：创建实体 → 写 AIActionLog → 置 Dispatched。任一步失败全部回滚。
        // SQL Server 启用了 EnableRetryOnFailure，手动事务必须包进执行策略（否则
        // SqlServerRetryingExecutionStrategy 拒绝用户事务，联调 2026-08-22 实测触发）。
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var created = new List<CreatedRefDto>();
                var logDateSource = TryParseLocalDate(envelope.LocalTime)?.ToDateTime(TimeOnly.MinValue)
                                    ?? DateTime.UtcNow.Date;

                foreach (var (req, suggestion, merged) in planned)
                {
                    var (entityType, entityId, title) = suggestion.Type switch
                    {
                        "task" => await CreateMemoAsync(userId, merged, envelope.TzOffsetMinutes, ct),
                        "worklog" => await CreateWorkLogAsync(userId, merged, logDateSource, ct),
                        "lifelog" => await CreateLifeLogAsync(userId, merged, logDateSource, ct),
                        _ => throw new BusinessException($"类型 {suggestion.Type} 的建议不可分发", 422)
                    };
                    created.Add(new CreatedRefDto(req.SuggestionId, suggestion.Type, entityId, Truncate(title, 50)));

                    _db.AIActionLogs.Add(new AIActionLog
                    {
                        UserId = userId,
                        ActionType = AIActionLog.ActionTypeInboxDispatch,
                        IntentDesc = Truncate(item.Raw, MaxCorrectionLength * 2),
                        TargetType = "inbox",
                        TargetId = item.Id,
                        PayloadJson = JsonSerializer.Serialize(
                            new DispatchLogPayload(req.SuggestionId, entityType, entityId, suggestion, req.Overrides),
                            MiraiJson.Options),
                        Decision = nameof(InboxDecision.Applied).ToLowerInvariant(),
                        DecidedAt = DateTime.UtcNow
                    });
                }

                item.Status = (byte)InboxStatus.Dispatched;
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new DispatchResultDto(item.Id, created);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "分发事务失败，已回滚（InboxItem={InboxItemId}）", inboxItemId);
                throw new BusinessException("分发失败：数据写入异常，已全部回滚", 500);
            }
        });
    }

    // ===== 丢弃 =====

    public async Task DiscardAsync(int userId, int inboxItemId, CancellationToken ct = default)
    {
        var item = await _db.InboxItems
            .FirstOrDefaultAsync(i => i.Id == inboxItemId && i.UserId == userId, ct)
            ?? throw new BusinessException("捕获项不存在", 404);
        if (item.Status == (byte)InboxStatus.Dispatched)
            throw new BusinessException("已分发的捕获项不能丢弃，请使用撤销", 409);

        // 软删以 Status=Discarded 表达（列表默认排除）；不置 IsDeleted，
        // 否则全局过滤器会让契约 §2.2 的显式 status=4 过滤永远查不到数据。
        item.Status = (byte)InboxStatus.Discarded;
        _db.AIActionLogs.Add(new AIActionLog
        {
            UserId = userId,
            ActionType = AIActionLog.ActionTypeInboxDiscard,
            IntentDesc = Truncate(item.Raw, 500),
            TargetType = "inbox",
            TargetId = item.Id,
            Decision = nameof(InboxDecision.Discarded).ToLowerInvariant(),
            DecidedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    // ===== 撤销分发 =====

    public async Task UndoAsync(int userId, int inboxItemId, CancellationToken ct = default)
    {
        var item = await _db.InboxItems
            .FirstOrDefaultAsync(i => i.Id == inboxItemId && i.UserId == userId, ct)
            ?? throw new BusinessException("捕获项不存在", 404);
        if (item.Status != (byte)InboxStatus.Dispatched)
            throw new BusinessException("仅已分发的捕获项可撤销", 409);

        var dispatchLogs = await _db.AIActionLogs
            .Where(l => l.UserId == userId
                && l.ActionType == AIActionLog.ActionTypeInboxDispatch
                && l.TargetType == "inbox"
                && l.TargetId == inboxItemId
                && l.Decision == "applied")
            .ToListAsync(ct);

        // 与 DispatchAsync 相同：手动事务必须包进执行策略（EnableRetryOnFailure 兼容）。
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var log in dispatchLogs)
                {
                    log.Decision = nameof(InboxDecision.Undone).ToLowerInvariant();
                    log.DecidedAt = DateTime.UtcNow;

                    var payload = TryDeserialize<DispatchLogPayload>(log.PayloadJson);
                    if (payload?.CreatedId is int entityId && !string.IsNullOrEmpty(payload.CreatedType))
                        await SoftDeleteCreatedEntityAsync(userId, payload.CreatedType, entityId, ct);
                }

                _db.AIActionLogs.Add(new AIActionLog
                {
                    UserId = userId,
                    ActionType = AIActionLog.ActionTypeInboxUndo,
                    IntentDesc = Truncate(item.Raw, 500),
                    TargetType = "inbox",
                    TargetId = item.Id,
                    PayloadJson = JsonSerializer.Serialize(new { restored = dispatchLogs.Count }, MiraiJson.Options),
                    Decision = nameof(InboxDecision.Undone).ToLowerInvariant(),
                    DecidedAt = DateTime.UtcNow
                });

                item.Status = (byte)InboxStatus.Triaged;
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "撤销事务失败，已回滚（InboxItem={InboxItemId}）", inboxItemId);
                throw new BusinessException("撤销失败：数据写入异常，已全部回滚", 500);
            }
        });
    }

    // ===== 分拣核心 =====

    /// <summary>
    /// 装配 prompt → 调 DeepSeek（json_object，25s 超时，失败重试 1 次）→ 更新 AiParse/Status。
    /// 任何失败均以 Status=Error 落库，不向调用方抛错。
    /// </summary>
    private async Task TriageCoreAsync(
        InboxItem item, string localTime, int tzOffsetMinutes, string? correction, CancellationToken ct)
    {
        try
        {
            var recentTags = await GetRecentTagsAsync(item.UserId, ct);
            var systemPrompt = MiraiPrompts.TriageSystemPrompt
                .Replace("{{localTime}}", localTime)
                .Replace("{{tzOffsetMinutes}}", tzOffsetMinutes.ToString())
                .Replace("{{recentTags}}", recentTags);

            var userContent = MiraiPrompts.TriageRealUserPrefix + item.Raw;
            if (!string.IsNullOrWhiteSpace(correction))
            {
                userContent += MiraiPrompts.TriageCorrectionSuffix
                    .Replace("{{correction}}", correction.Trim());
            }

            // v1.2 起两类 user 消息带前缀标记，防推理模型把 few-shot 内容混入真实分拣
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = MiraiPrompts.TriageFewShotUserPrefix + MiraiPrompts.TriageFewShotUser },
                new { role = "assistant", content = MiraiPrompts.TriageFewShotAssistant },
                new { role = "user", content = userContent }
            };

            var result = await CallTriageWithRetryAsync(messages, ct);
            item.AiParse = JsonSerializer.Serialize(
                new AiParseEnvelope(
                    result.Items ?? new List<TriageSuggestionDto>(),
                    result.Uncertain ?? new List<string>(),
                    tzOffsetMinutes,
                    localTime),
                MiraiJson.Options);
            item.AiModel = _deepSeekOptions.Model;
            item.Status = (byte)InboxStatus.Triaged;
            item.TriagedAt = DateTime.UtcNow;
            item.Error = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning("分拣失败（InboxItem={Id}）：{Message}", item.Id, ex.Message);
            item.Status = (byte)InboxStatus.Error;
            item.Error = Truncate($"分拣失败：{ex.Message}", 500);
            item.AiModel = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "分拣发生未预期异常（InboxItem={Id}）", item.Id);
            item.Status = (byte)InboxStatus.Error;
            item.Error = Truncate($"分拣失败：{ex.Message}", 500);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>调 DeepSeek；解析失败附错误提示重试 1 次，仍失败抛出（由调用方置 Error）。</summary>
    private async Task<TriageResultDto> CallTriageWithRetryAsync(List<object> messages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_deepSeekOptions.ApiKey))
            throw new HttpRequestException("DeepSeek API Key 未配置");

        using var client = DeepSeekJsonClient.CreateAuthorizedClient(
            _httpClientFactory, _deepSeekOptions.BaseUrl, _deepSeekOptions.ApiKey);

        Exception? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var content = await DeepSeekJsonClient.CompleteAsync(
                    client, _deepSeekOptions.Model, messages,
                    temperature: 0.2, maxTokens: 8000, jsonObject: true,
                    timeout: TriageTimeout, ct);

                return ParseTriageContent(content)
                    ?? throw new JsonException("输出不是有效的分拣 JSON 结构");
            }
            catch (JsonException ex)
            {
                lastError = ex;
                // 解析失败：附错误提示重试一次
                messages = new List<object>(messages)
                {
                    new { role = "assistant", content = "（上一次输出无法解析）" },
                    new { role = "user", content = $"上一次输出无法解析为 JSON（{Truncate(ex.Message, 120)}）。请重新只输出符合约定结构的 JSON，不要包含任何其他文字或代码围栏。" }
                };
            }
            catch (HttpRequestException ex)
            {
                lastError = ex; // 网络/5xx：同载荷重试一次
            }
            catch (OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                lastError = new TaskCanceledException("AI 调用超时（25s）");
            }
        }

        throw lastError!;
    }

    /// <summary>解析模型输出：容忍 Markdown 代码围栏与 BOM；结构不符返回 null。</summary>
    private static TriageResultDto? ParseTriageContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        // 防御：剥离 ```json ... ``` 围栏（prompt 已禁止，但模型可能漂移）
        var cleaned = Regex.Replace(content.Trim(), @"^```(?:json)?\s*|\s*```$", string.Empty).Trim();
        try
        {
            var result = JsonSerializer.Deserialize<TriageResultDto>(cleaned, MiraiJson.Options);
            if (result == null) return null;
            return new TriageResultDto(
                result.Items ?? new List<TriageSuggestionDto>(),
                result.Uncertain ?? new List<string>());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>近 50 条 WorkLog 的 tag 频次 top10（逗号分隔；无则"无"）。</summary>
    private async Task<string> GetRecentTagsAsync(int userId, CancellationToken ct)
    {
        var tagsStrings = await _db.WorkLogs
            .AsNoTracking()
            .Where(w => w.UserId == userId && w.Tags != null)
            .OrderByDescending(w => w.CreatedAt)
            .Take(50)
            .Select(w => w.Tags!)
            .ToListAsync(ct);

        var topTags = tagsStrings
            .SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(t => t.Length > 0)
            .GroupBy(t => t, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(10)
            .Select(g => g.Key)
            .ToList();

        return topTags.Count == 0 ? "无" : string.Join("，", topTags);
    }

    // ===== 分发辅助 =====

    /// <summary>overrides 白名单校验：仅允许对应类型 fields 的键（Content 为 task/worklog/lifelog 共有）。</summary>
    private static void ValidateOverrideKeys(string type, FieldsDto? overrides)
    {
        if (overrides == null) return;

        var violations = new List<string>();
        if (overrides.Title != null && type != "worklog") violations.Add("title");
        if (overrides.Tags != null && type != "worklog") violations.Add("tags");
        if (overrides.Category != null && type != "worklog") violations.Add("category");
        if (overrides.Mood != null && type != "lifelog") violations.Add("mood");
        if (overrides.RemindAtLocal != null && type != "task") violations.Add("remindAtLocal");
        if (overrides.Priority != null && type != "task") violations.Add("priority");
        if (overrides.Section != null && type != "task") violations.Add("section");
        if (violations.Count > 0)
            throw new BusinessException($"类型 {type} 的 overrides 不允许字段：{string.Join(", ", violations)}", 400);
    }

    /// <summary>深合并：overrides 非 null 字段覆盖建议值。</summary>
    private static FieldsDto MergeFields(FieldsDto? suggestion, FieldsDto? overrides)
    {
        var s = suggestion ?? new FieldsDto(null, null, null, null, null, null, null, null);
        if (overrides == null) return s;

        return new FieldsDto(
            Content: overrides.Content ?? s.Content,
            RemindAtLocal: overrides.RemindAtLocal ?? s.RemindAtLocal,
            Priority: overrides.Priority ?? s.Priority,
            Section: overrides.Section ?? s.Section,
            Title: overrides.Title ?? s.Title,
            Tags: overrides.Tags ?? s.Tags,
            Category: overrides.Category ?? s.Category,
            Mood: overrides.Mood ?? s.Mood);
    }

    private static void ValidateMergedFields(string type, FieldsDto merged)
    {
        var missing = type switch
        {
            "task" => string.IsNullOrWhiteSpace(merged.Content) ? "content" : null,
            "worklog" => string.IsNullOrWhiteSpace(merged.Title) ? "title" : null,
            "lifelog" => string.IsNullOrWhiteSpace(merged.Content) ? "content" : null,
            _ => "type"
        };
        if (missing != null)
            throw new BusinessException($"类型 {type} 的建议缺少必填字段 {missing}，请通过 overrides 补全", 400);
    }

    private async Task<(string Type, int Id, string Title)> CreateMemoAsync(
        int userId, FieldsDto merged, int tzOffsetMinutes, CancellationToken ct)
    {
        var memo = new Memo
        {
            UserId = userId,
            Section = merged.Section is "work" or "life" ? merged.Section : "work",
            Content = merged.Content!.Trim(),
            RemindAt = MiraiTime.LocalToUtc(merged.RemindAtLocal, tzOffsetMinutes),
            RemindMethods = 1, // 契约：remindMethods 默认 1（弹窗）
            Priority = (byte)Math.Clamp(merged.Priority ?? 2, 1, 3)
        };
        _db.Memos.Add(memo);
        await _db.SaveChangesAsync(ct);
        return ("memo", memo.Id, memo.Content);
    }

    private async Task<(string Type, int Id, string Title)> CreateWorkLogAsync(
        int userId, FieldsDto merged, DateTime logDate, CancellationToken ct)
    {
        var workLog = new WorkLog
        {
            UserId = userId,
            Title = merged.Title!.Trim(),
            Content = string.IsNullOrWhiteSpace(merged.Content) ? null : merged.Content.Trim(),
            Tags = merged.Tags is { Count: > 0 }
                ? Truncate(string.Join(",", merged.Tags.Where(t => !string.IsNullOrWhiteSpace(t))), 500)
                : null,
            Category = string.IsNullOrWhiteSpace(merged.Category) ? null : Truncate(merged.Category.Trim(), 100),
            LogDate = logDate,
            Status = 0
        };
        _db.WorkLogs.Add(workLog);
        await _db.SaveChangesAsync(ct);
        return ("worklog", workLog.Id, workLog.Title);
    }

    private async Task<(string Type, int Id, string Title)> CreateLifeLogAsync(
        int userId, FieldsDto merged, DateTime logDate, CancellationToken ct)
    {
        var lifeLog = new LifeLog
        {
            UserId = userId,
            Content = merged.Content!.Trim(),
            Mood = string.IsNullOrWhiteSpace(merged.Mood) ? null : Truncate(merged.Mood.Trim(), 50),
            LogDate = logDate
        };
        _db.LifeLogs.Add(lifeLog);
        await _db.SaveChangesAsync(ct);
        return ("lifelog", lifeLog.Id, lifeLog.Content);
    }

    /// <summary>按类型软删 dispatch 创建的实体（undo 用）。实体不存在时静默跳过（可能已被用户单独删除）。</summary>
    private async Task SoftDeleteCreatedEntityAsync(int userId, string createdType, int createdId, CancellationToken ct)
    {
        switch (createdType)
        {
            case "memo":
                var memo = await _db.Memos
                    .FirstOrDefaultAsync(m => m.Id == createdId && m.UserId == userId, ct);
                if (memo != null) memo.IsDeleted = true;
                break;
            case "worklog":
                var workLog = await _db.WorkLogs
                    .FirstOrDefaultAsync(w => w.Id == createdId && w.UserId == userId, ct);
                if (workLog != null) workLog.IsDeleted = true;
                break;
            case "lifelog":
                var lifeLog = await _db.LifeLogs
                    .FirstOrDefaultAsync(l => l.Id == createdId && l.UserId == userId, ct);
                if (lifeLog != null) lifeLog.IsDeleted = true;
                break;
        }
    }

    // ===== 映射与解析 =====

    private static InboxItemDto MapDto(InboxItem item) => new(
        item.Id,
        item.Raw,
        item.Source,
        item.Status,
        TryParseEnvelope(item.AiParse) is { } envelope
            ? new TriageResultDto(envelope.Items ?? new List<TriageSuggestionDto>(), envelope.Uncertain ?? new List<string>())
            : null,
        item.AiModel,
        item.CorrectionNote,
        item.Error,
        item.TriagedAt,
        item.CreatedAt);

    internal static AiParseEnvelope? TryParseEnvelope(string? aiParseJson)
    {
        if (string.IsNullOrWhiteSpace(aiParseJson)) return null;
        var envelope = TryDeserialize<AiParseEnvelope>(aiParseJson);
        if (envelope != null) return envelope;

        // 兼容：直接存的 TriageResult（无信封字段）
        var legacy = TryDeserialize<TriageResultDto>(aiParseJson);
        return legacy == null
            ? null
            : new AiParseEnvelope(
                legacy.Items ?? new List<TriageSuggestionDto>(),
                legacy.Uncertain ?? new List<string>(),
                0, null);
    }

    private static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, MiraiJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateOnly? TryParseLocalDate(string? localTime)
    {
        if (string.IsNullOrWhiteSpace(localTime)) return null;
        return DateOnly.TryParse(localTime, out var date) ? date : null;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
