namespace MiraiNote.Shared.Dtos.ScheduledTasks;

public record ScheduledTaskDto(
    int Id,
    string Description,
    DateTime ExecuteAt,
    string Status,
    string? Result,
    string? ErrorMessage,
    bool NotifyEmail,
    DateTime? ExecutedAt,
    DateTime CreatedAt
);

public record CreateScheduledTaskRequest(
    string Description,
    DateTime ExecuteAt,
    bool NotifyEmail = false
);

public record ScheduledTaskListResponse(
    int Total,
    List<ScheduledTaskDto> Items
);
