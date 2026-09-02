using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Shared.Common;

namespace MiraiNote.Core.Services.Mirai;

/// <summary>
/// Chat 会话 M1 扩展规则（契约 §2.9）：
/// sessionType ∈ {legacy, command, context}；context 时 attachToType/attachToObjectId
/// 必填且挂载对象必须存在；非 context 会话不允许携带挂载字段。
/// </summary>
public static class MiraiSessionRules
{
    /// <summary>会话类型合法值：legacy | command | context。</summary>
    public static readonly HashSet<string> SessionTypes = new(StringComparer.Ordinal)
        { "legacy", "command", "context" };

    /// <summary>
    /// 校验并归一化会话创建参数。返回归一化后的 (sessionType, attachToType, attachToObjectId)。
    /// 违规抛 BusinessException（400）。
    /// </summary>
    public static async Task<(string SessionType, string? AttachToType, int? AttachToObjectId)> ValidateAsync(
        string? sessionType, string? attachToType, int? attachToObjectId,
        MiraiNoteDbContext db, int userId, CancellationToken ct = default)
    {
        sessionType = string.IsNullOrWhiteSpace(sessionType) ? null : sessionType.Trim();
        attachToType = string.IsNullOrWhiteSpace(attachToType) ? null : attachToType.Trim();

        if (sessionType != null && !SessionTypes.Contains(sessionType))
            throw new BusinessException($"sessionType 取值无效（{string.Join("/", SessionTypes)}）", 400);
        if (sessionType == "context")
        {
            if (attachToType == null || attachToObjectId is not int attachToId)
                throw new BusinessException("context 会话必须提供 attachToType 与 attachToObjectId", 400);
            if (!MiraiContextProvider.AttachTypes.Contains(attachToType))
                throw new BusinessException(
                    $"attachToType 取值无效（{string.Join("/", MiraiContextProvider.AttachTypes)}）", 400);
            if (!await AttachTargetExistsAsync(db, userId, attachToType, attachToId, ct))
                throw new BusinessException("挂载对象不存在", 400);
            return ("context", attachToType, attachToId);
        }

        if (attachToType != null || attachToObjectId.HasValue)
            throw new BusinessException("仅 context 会话支持挂载对象（attachToType/attachToObjectId）", 400);
        return (sessionType ?? "legacy", null, null);
    }

    private static Task<bool> AttachTargetExistsAsync(
        MiraiNoteDbContext db, int userId, string attachToType, int attachToObjectId, CancellationToken ct) =>
        attachToType switch
        {
            "worklog" => db.WorkLogs.AnyAsync(w => w.Id == attachToObjectId && w.UserId == userId, ct),
            "lifelog" => db.LifeLogs.AnyAsync(l => l.Id == attachToObjectId && l.UserId == userId, ct),
            "memo" => db.Memos.AnyAsync(m => m.Id == attachToObjectId && m.UserId == userId, ct),
            "inbox" => db.InboxItems.AnyAsync(i => i.Id == attachToObjectId && i.UserId == userId, ct),
            "briefing" => db.DailyBriefings.AnyAsync(b => b.Id == attachToObjectId && b.UserId == userId, ct),
            _ => Task.FromResult(false)
        };
}
