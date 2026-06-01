namespace MiraiNote.Shared.Dtos.LifeLogs;

/// <summary>
/// 生活记录列表查询参数。
/// </summary>
public class LifeLogListQuery
{
    public string? Keyword { get; set; }
    public string? Mood { get; set; }
    /// <summary>按月查询，格式 yyyy-MM。</summary>
    public string? Month { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// 创建生活记录请求。
/// </summary>
public class CreateLifeLogRequest
{
    public string Content { get; set; } = string.Empty;
    public string? Mood { get; set; }
    public string? ImagePath { get; set; }
    public DateTime LogDate { get; set; }
}

/// <summary>
/// 更新生活记录请求。
/// </summary>
public class UpdateLifeLogRequest
{
    public string Content { get; set; } = string.Empty;
    public string? Mood { get; set; }
    public string? ImagePath { get; set; }
    public DateTime LogDate { get; set; }
}

/// <summary>
/// 生活记录响应 DTO。
/// </summary>
public class LifeLogDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Mood { get; set; }
    public string? ImagePath { get; set; }
    public DateTime LogDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
