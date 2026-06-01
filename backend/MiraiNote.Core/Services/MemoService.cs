using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.Auth;
using MiraiNote.Shared.Dtos.Memos;

namespace MiraiNote.Core.Services;

public interface IMemoService
{
    Task<PagedResult<MemoDto>> GetListAsync(int userId, MemoListQuery query, CancellationToken ct = default);
    Task<MemoDto> CreateAsync(int userId, CreateMemoRequest request, CancellationToken ct = default);
    Task<MemoDto> UpdateAsync(int userId, int id, UpdateMemoRequest request, CancellationToken ct = default);
    Task<MemoDto> PatchStatusAsync(int userId, int id, PatchMemoStatusRequest request, CancellationToken ct = default);
    Task DeleteAsync(int userId, int id, CancellationToken ct = default);

    /// <summary>查询当前用户已到提醒时间、且需弹窗、未被确认的备忘。</summary>
    Task<List<MemoDto>> GetDuePopupsAsync(int userId, CancellationToken ct = default);

    /// <summary>用户在前端确认（关闭）弹窗。</summary>
    Task AcknowledgePopupAsync(int userId, int id, CancellationToken ct = default);
}

/// <summary>
/// 备忘业务实现。工作 / 生活 共用一张 Memo 表，通过 Section 字段区分。
/// </summary>
public class MemoService : IMemoService
{
    private static readonly HashSet<string> AllowedSections = new(StringComparer.OrdinalIgnoreCase) { "work", "life" };

    // 提醒方式位标志
    private const byte ReminderPopup = 1;
    private const byte ReminderEmail = 2;

    private readonly MiraiNoteDbContext _db;

    public MemoService(MiraiNoteDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<MemoDto>> GetListAsync(int userId, MemoListQuery query, CancellationToken ct = default)
    {
        var section = NormalizeSection(query.Section);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var q = _db.Memos.AsNoTracking().Where(m => m.UserId == userId && m.Section == section);

        if (!query.IncludeArchived) q = q.Where(m => !m.IsArchived);
        if (!query.IncludeDone) q = q.Where(m => !m.IsDone);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            q = q.Where(m => m.Content.Contains(kw));
        }

        var total = await q.CountAsync(ct);
        // 排序：置顶优先 → 优先级高在前 → 提醒时间近的在前 → 创建时间新的在前
        var items = await q
            .OrderByDescending(m => m.IsPinned)
            .ThenByDescending(m => m.Priority)
            .ThenBy(m => m.RemindAt == null)
            .ThenBy(m => m.RemindAt)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => Map(m))
            .ToListAsync(ct);

        return new PagedResult<MemoDto>
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = items
        };
    }

    public async Task<MemoDto> CreateAsync(int userId, CreateMemoRequest request, CancellationToken ct = default)
    {
        var section = NormalizeSection(request.Section);
        ValidateContent(request.Content);
        ValidateRemind(request.RemindAt, request.RemindMethods);
        var priority = ClampPriority(request.Priority);

        var entity = new Memo
        {
            UserId = userId,
            Section = section,
            Content = request.Content.Trim(),
            RemindAt = request.RemindAt,
            RemindMethods = NormalizeMethods(request.RemindAt, request.RemindMethods),
            Priority = priority,
            IsPinned = request.IsPinned
        };
        _db.Memos.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<MemoDto> UpdateAsync(int userId, int id, UpdateMemoRequest request, CancellationToken ct = default)
    {
        ValidateContent(request.Content);
        ValidateRemind(request.RemindAt, request.RemindMethods);
        var entity = await GetOwnedAsync(userId, id, ct);

        var newMethods = NormalizeMethods(request.RemindAt, request.RemindMethods);
        var remindChanged = entity.RemindAt != request.RemindAt || entity.RemindMethods != newMethods;

        entity.Content = request.Content.Trim();
        entity.RemindAt = request.RemindAt;
        entity.RemindMethods = newMethods;
        entity.Priority = ClampPriority(request.Priority);
        entity.IsPinned = request.IsPinned;

        // 提醒被改动 → 重置发送/确认标志，让其再次触发
        if (remindChanged)
        {
            entity.EmailReminderSent = false;
            entity.PopupAcknowledged = false;
            entity.RemindedAt = null;
        }

        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<MemoDto> PatchStatusAsync(int userId, int id, PatchMemoStatusRequest request, CancellationToken ct = default)
    {
        var entity = await GetOwnedAsync(userId, id, ct);

        if (request.IsDone.HasValue) entity.IsDone = request.IsDone.Value;
        if (request.IsPinned.HasValue) entity.IsPinned = request.IsPinned.Value;
        if (request.IsArchived.HasValue) entity.IsArchived = request.IsArchived.Value;

        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var entity = await GetOwnedAsync(userId, id, ct);
        entity.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<MemoDto>> GetDuePopupsAsync(int userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var list = await _db.Memos.AsNoTracking()
            .Where(m =>
                m.UserId == userId &&
                !m.IsDone &&
                !m.IsArchived &&
                m.RemindAt != null &&
                m.RemindAt <= now &&
                (m.RemindMethods & ReminderPopup) == ReminderPopup &&
                !m.PopupAcknowledged)
            .OrderBy(m => m.RemindAt)
            .Select(m => Map(m))
            .ToListAsync(ct);
        return list;
    }

    public async Task AcknowledgePopupAsync(int userId, int id, CancellationToken ct = default)
    {
        var entity = await GetOwnedAsync(userId, id, ct);
        entity.PopupAcknowledged = true;
        if (entity.RemindedAt == null) entity.RemindedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ===== 私有辅助 =====

    private async Task<Memo> GetOwnedAsync(int userId, int id, CancellationToken ct) =>
        await _db.Memos.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId, ct)
            ?? throw new BusinessException("备忘不存在", 404);

    private static string NormalizeSection(string? section)
    {
        if (string.IsNullOrWhiteSpace(section) || !AllowedSections.Contains(section))
        {
            throw new BusinessException("无效的板块（仅支持 work / life）");
        }
        return section.ToLowerInvariant();
    }

    private static void ValidateContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new BusinessException("备忘内容不能为空");
        }
        if (content.Length > 1000)
        {
            throw new BusinessException("备忘内容不能超过 1000 字");
        }
    }

    private static void ValidateRemind(DateTime? remindAt, byte methods)
    {
        if (methods > 3)
        {
            throw new BusinessException("无效的提醒方式");
        }
        if (methods != 0 && remindAt == null)
        {
            throw new BusinessException("选择了提醒方式时必须设置提醒时间");
        }
    }

    /// <summary>未设置提醒时间则强制方式 = 0；反之保留传入位标志。</summary>
    private static byte NormalizeMethods(DateTime? remindAt, byte methods) =>
        remindAt == null ? (byte)0 : methods;

    private static byte ClampPriority(byte p) => p switch { < 1 => 2, > 3 => 2, _ => p };

    private static MemoDto Map(Memo m) => new()
    {
        Id = m.Id,
        Section = m.Section,
        Content = m.Content,
        RemindAt = m.RemindAt,
        RemindMethods = m.RemindMethods,
        EmailReminderSent = m.EmailReminderSent,
        PopupAcknowledged = m.PopupAcknowledged,
        RemindedAt = m.RemindedAt,
        Priority = m.Priority,
        IsPinned = m.IsPinned,
        IsDone = m.IsDone,
        IsArchived = m.IsArchived,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt
    };
}
