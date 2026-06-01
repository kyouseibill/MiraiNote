namespace MiraiNote.Shared.Dtos.WeeklyReports;

/// <summary>
/// 生成周报请求。
/// </summary>
public class GenerateReportRequest
{
    public DateTime WeekStart { get; set; }
    public DateTime WeekEnd { get; set; }
}

/// <summary>
/// 更新周报内容请求（手动编辑后保存）。
/// </summary>
public class UpdateReportRequest
{
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// 周报响应 DTO。
/// </summary>
public class WeeklyReportDto
{
    public int Id { get; set; }
    public DateTime WeekStart { get; set; }
    public DateTime WeekEnd { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public bool IsEdited { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 上传周报参考文件后返回的 DTO。
/// </summary>
public class WeeklyReportReferenceDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime? WeekStart { get; set; }
    public DateTime? WeekEnd { get; set; }
    public string? Remark { get; set; }
    public DateTime CreatedAt { get; set; }
}
