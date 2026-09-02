namespace MiraiNote.Shared.Dtos.Memos;

// ===== 请求 DTO =====

public class CreateMemoRequest
{
    /// <summary>板块：work | life。</summary>
    public string Section { get; set; } = "work";
    public string Content { get; set; } = string.Empty;
    public DateTime? RemindAt { get; set; }
    /// <summary>提醒方式位标志：0=不提醒，1=弹窗，2=邮件，3=弹窗+邮件。</summary>
    public byte RemindMethods { get; set; } = 0;
    /// <summary>优先级：1=低 2=中 3=高，默认 2。</summary>
    public byte Priority { get; set; } = 2;
    public bool IsPinned { get; set; } = false;
}

public class UpdateMemoRequest
{
    public string Content { get; set; } = string.Empty;
    public DateTime? RemindAt { get; set; }
    public byte RemindMethods { get; set; } = 0;
    public byte Priority { get; set; } = 2;
    public bool IsPinned { get; set; } = false;
}

/// <summary>用于完成/归档等状态切换的轻量 PATCH。</summary>
public class PatchMemoStatusRequest
{
    public bool? IsDone { get; set; }
    public bool? IsPinned { get; set; }
    public bool? IsArchived { get; set; }
}

public class MemoListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    /// <summary>板块：work | life。必填。</summary>
    public string Section { get; set; } = "work";

    /// <summary>关键词：在 Content 中模糊匹配。</summary>
    public string? Keyword { get; set; }

    /// <summary>是否包含已归档（默认 false：仅显示未归档）。</summary>
    public bool IncludeArchived { get; set; } = false;

    /// <summary>是否包含已完成（默认 true）。</summary>
    public bool IncludeDone { get; set; } = true;
}

// ===== 响应 DTO =====

public class MemoDto
{
    public int Id { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime? RemindAt { get; set; }
    public byte RemindMethods { get; set; }
    public bool EmailReminderSent { get; set; }
    public bool PopupAcknowledged { get; set; }
    public DateTime? RemindedAt { get; set; }
    public byte Priority { get; set; }
    public bool IsPinned { get; set; }
    public bool IsDone { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
