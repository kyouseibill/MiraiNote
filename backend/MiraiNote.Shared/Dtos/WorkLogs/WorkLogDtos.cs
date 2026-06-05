namespace MiraiNote.Shared.Dtos.WorkLogs;

// ===== 请求 DTO =====

public class CreateWorkLogRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string? Content { get; set; }
    public string? Tags { get; set; }
    public string? Category { get; set; }
    /// <summary>记录日期（仅日期部分，格式 yyyy-MM-dd）。</summary>
    public DateTime LogDate { get; set; }
    /// <summary>工作状态：0=未标记，1=进行中，2=已完成，3=已延期。</summary>
    public byte Status { get; set; } = 0;
}

public class UpdateWorkLogRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string? Content { get; set; }
    public string? Tags { get; set; }
    public string? Category { get; set; }
    public DateTime LogDate { get; set; }
    /// <summary>工作状态：0=未标记，1=进行中，2=已完成，3=已延期。</summary>
    public byte Status { get; set; } = 0;
}

/// <summary>列表查询参数。</summary>
public class WorkLogListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>关键词：在 Title / Purpose / Content / Tags 中模糊匹配。</summary>
    public string? Keyword { get; set; }

    /// <summary>按分类筛选。</summary>
    public string? Category { get; set; }

    /// <summary>按标签筛选（单个标签）。</summary>
    public string? Tag { get; set; }

    /// <summary>日期范围起（含），格式 yyyy-MM-dd。</summary>
    public DateTime? DateFrom { get; set; }

    /// <summary>日期范围止（含），格式 yyyy-MM-dd。</summary>
    public DateTime? DateTo { get; set; }

    /// <summary>按状态筛选：null=全部，1=进行中，2=已完成，3=已延期。</summary>
    public byte? Status { get; set; }
}

// ===== 响应 DTO =====

public class WorkLogDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string? Content { get; set; }
    public string? Tags { get; set; }
    public string? Category { get; set; }
    public DateTime LogDate { get; set; }
    /// <summary>工作状态：0=未标记，1=进行中，2=已完成，3=已延期。</summary>
    public byte Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
