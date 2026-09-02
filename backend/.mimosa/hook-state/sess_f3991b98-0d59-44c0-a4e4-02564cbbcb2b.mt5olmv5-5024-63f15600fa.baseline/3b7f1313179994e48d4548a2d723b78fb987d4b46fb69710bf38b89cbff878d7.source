using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.ScheduledTasks;

namespace MiraiNote.Core.Services;

public interface IScheduledTaskService
{
    Task<ScheduledTaskDto> CreateAsync(int userId, string description, DateTime executeAt, bool notifyEmail = false, CancellationToken ct = default);
    Task<List<ScheduledTaskDto>> GetPendingAsync(int userId, CancellationToken ct = default);
    Task<List<ScheduledTaskDto>> GetAllAsync(int userId, CancellationToken ct = default);
    Task<ScheduledTaskDto?> GetByIdAsync(int userId, int taskId, CancellationToken ct = default);
    Task MarkRunningAsync(int taskId, CancellationToken ct = default);
    Task MarkCompletedAsync(int taskId, string result, CancellationToken ct = default);
    Task MarkFailedAsync(int taskId, string error, CancellationToken ct = default);
    Task MarkCancelledAsync(int userId, int taskId, CancellationToken ct = default);
    Task<List<ScheduledTask>> GetDueTasksAsync(CancellationToken ct = default);
}

public class ScheduledTaskService : IScheduledTaskService
{
    private readonly MiraiNoteDbContext _db;

    public ScheduledTaskService(MiraiNoteDbContext db)
    {
        _db = db;
    }

    public async Task<ScheduledTaskDto> CreateAsync(int userId, string description, DateTime executeAt, bool notifyEmail = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new BusinessException("任务描述不能为空", 400);
        if (executeAt <= DateTime.UtcNow)
            throw new BusinessException("执行时间必须在将来", 400);

        var task = new ScheduledTask
        {
            UserId = userId,
            Description = description.Trim(),
            ExecuteAt = executeAt,
            NotifyEmail = notifyEmail,
            Status = "Pending"
        };
        _db.ScheduledTasks.Add(task);
        await _db.SaveChangesAsync(ct);
        return Map(task);
    }

    public async Task<List<ScheduledTaskDto>> GetPendingAsync(int userId, CancellationToken ct = default)
        => await _db.ScheduledTasks
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.Status == "Pending")
            .OrderBy(t => t.ExecuteAt)
            .Select(t => Map(t))
            .ToListAsync(ct);

    public async Task<List<ScheduledTaskDto>> GetAllAsync(int userId, CancellationToken ct = default)
        => await _db.ScheduledTasks
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.ExecuteAt)
            .Select(t => Map(t))
            .ToListAsync(ct);

    public async Task<ScheduledTaskDto?> GetByIdAsync(int userId, int taskId, CancellationToken ct = default)
    {
        var task = await _db.ScheduledTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct);
        return task == null ? null : Map(task);
    }

    public async Task MarkRunningAsync(int taskId, CancellationToken ct = default)
    {
        var task = await _db.ScheduledTasks.FindAsync(new object[] { taskId }, ct);
        if (task == null) return;
        task.Status = "Running";
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkCompletedAsync(int taskId, string result, CancellationToken ct = default)
    {
        var task = await _db.ScheduledTasks.FindAsync(new object[] { taskId }, ct);
        if (task == null) return;
        task.Status = "Completed";
        task.Result = result;
        task.ExecutedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(int taskId, string error, CancellationToken ct = default)
    {
        var task = await _db.ScheduledTasks.FindAsync(new object[] { taskId }, ct);
        if (task == null) return;
        task.Status = "Failed";
        task.ErrorMessage = error;
        task.ExecutedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkCancelledAsync(int userId, int taskId, CancellationToken ct = default)
    {
        var task = await _db.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            ?? throw new BusinessException("任务不存在", 404);
        if (task.Status is "Running" or "Completed")
            throw new BusinessException("任务已在执行或已完成，无法取消", 400);
        task.Status = "Cancelled";
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<ScheduledTask>> GetDueTasksAsync(CancellationToken ct = default)
        => await _db.ScheduledTasks
            .Include(t => t.User)
            .Where(t => t.Status == "Pending" && t.ExecuteAt <= DateTime.UtcNow)
            .OrderBy(t => t.ExecuteAt)
            .Take(10) // 单次最多处理 10 个任务
            .ToListAsync(ct);

    private static ScheduledTaskDto Map(ScheduledTask t) => new(
        t.Id,
        t.Description,
        t.ExecuteAt,
        t.Status,
        t.Result,
        t.ErrorMessage,
        t.NotifyEmail,
        t.ExecutedAt,
        t.CreatedAt
    );
}
