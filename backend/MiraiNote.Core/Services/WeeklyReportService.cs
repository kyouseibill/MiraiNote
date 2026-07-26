using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiraiNote.Data.Context;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using MiraiNote.Shared.Dtos.WeeklyReports;

namespace MiraiNote.Core.Services;

public interface IWeeklyReportService
{
    Task<WeeklyReportDto> GenerateAsync(int userId, GenerateReportRequest request, CancellationToken ct = default);
    Task<List<WeeklyReportDto>> GetListAsync(int userId, CancellationToken ct = default);
    Task<WeeklyReportDto> GetByIdAsync(int userId, int id, CancellationToken ct = default);
    Task<WeeklyReportDto> UpdateAsync(int userId, int id, UpdateReportRequest request, CancellationToken ct = default);
    Task DeleteAsync(int userId, int id, CancellationToken ct = default);

    // 参考文件管理
    Task<WeeklyReportReferenceDto> UploadReferenceAsync(int userId, IFormFile file, DateTime? weekStart, DateTime? weekEnd, string? remark, CancellationToken ct = default);
    Task<List<WeeklyReportReferenceDto>> GetReferencesAsync(int userId, CancellationToken ct = default);
    Task DeleteReferenceAsync(int userId, int id, CancellationToken ct = default);
}

/// <summary>
/// 周报业务实现：调用 DeepSeek AI 生成周报，解析 Excel 参考文件。
/// </summary>
public class WeeklyReportService : IWeeklyReportService
{
    private readonly MiraiNoteDbContext _db;
    private readonly DeepSeekOptions _deepSeekOptions;
    private readonly UploadOptions _uploadOptions;
    private readonly IHttpClientFactory _httpClientFactory;

    public WeeklyReportService(
        MiraiNoteDbContext db,
        IOptions<DeepSeekOptions> deepSeekOptions,
        IOptions<UploadOptions> uploadOptions,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _deepSeekOptions = deepSeekOptions.Value;
        _uploadOptions = uploadOptions.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<WeeklyReportDto> GenerateAsync(int userId, GenerateReportRequest request, CancellationToken ct = default)
    {
        var weekStart = request.WeekStart.Date;
        var weekEnd = request.WeekEnd.Date;

        // 查询该周工作记录
        var workLogs = await _db.WorkLogs
            .AsNoTracking()
            .Where(w => w.UserId == userId && w.LogDate >= weekStart && w.LogDate <= weekEnd)
            .OrderBy(w => w.LogDate)
            .ToListAsync(ct);

        // 查询参考文件
        var references = await _db.WeeklyReportReferences
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(3)
            .ToListAsync(ct);

        // 构建 Prompt
        var detailLevel = request.DetailLevel is >= 1 and <= 3 ? request.DetailLevel : 2;
        var prompt = BuildPrompt(weekStart, weekEnd, workLogs, references, detailLevel);

        // 调用 DeepSeek API
        var content = await CallDeepSeekAsync(prompt, ct);

        // 保存周报（若已有则更新）
        var existing = await _db.WeeklyReports
            .FirstOrDefaultAsync(r => r.UserId == userId && r.WeekStart == weekStart, ct);

        if (existing != null)
        {
            existing.Content = content;
            existing.WeekEnd = weekEnd;
            existing.GeneratedAt = DateTime.UtcNow;
            existing.IsEdited = false;
            await _db.SaveChangesAsync(ct);
            return Map(existing);
        }

        var report = new WeeklyReport
        {
            UserId = userId,
            WeekStart = weekStart,
            WeekEnd = weekEnd,
            Content = content,
            GeneratedAt = DateTime.UtcNow,
            IsEdited = false
        };
        _db.WeeklyReports.Add(report);
        await _db.SaveChangesAsync(ct);
        return Map(report);
    }

    public async Task<List<WeeklyReportDto>> GetListAsync(int userId, CancellationToken ct = default)
    {
        return await _db.WeeklyReports
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.WeekStart)
            .Select(r => Map(r))
            .ToListAsync(ct);
    }

    public async Task<WeeklyReportDto> GetByIdAsync(int userId, int id, CancellationToken ct = default)
    {
        var entity = await _db.WeeklyReports.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct)
            ?? throw new BusinessException("周报不存在", 404);
        return Map(entity);
    }

    public async Task<WeeklyReportDto> UpdateAsync(int userId, int id, UpdateReportRequest request, CancellationToken ct = default)
    {
        var entity = await _db.WeeklyReports
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct)
            ?? throw new BusinessException("周报不存在", 404);

        entity.Content = request.Content;
        entity.IsEdited = true;
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var entity = await _db.WeeklyReports
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct)
            ?? throw new BusinessException("周报不存在", 404);

        entity.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<WeeklyReportReferenceDto> UploadReferenceAsync(
        int userId, IFormFile file, DateTime? weekStart, DateTime? weekEnd, string? remark, CancellationToken ct = default)
    {
        if (file.Length > 10 * 1024 * 1024)
            throw new BusinessException("文件大小不能超过 10MB", 400);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls")
            throw new BusinessException("只支持 .xlsx / .xls 格式", 400);

        // 物理存储目录：优先使用 PhysicalPath（生产），否则回退到 BasePath
        var storageRoot = !string.IsNullOrEmpty(_uploadOptions.PhysicalPath)
            ? _uploadOptions.PhysicalPath
            : _uploadOptions.BasePath;
        var dir = Path.Combine(storageRoot, "references");
        Directory.CreateDirectory(dir);
        var savedName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(dir, savedName);

        await using (var stream = File.Create(filePath))
        {
            await file.CopyToAsync(stream, ct);
        }

        // 解析 Excel 文本
        var parsedText = ParseExcel(filePath);

        var entity = new WeeklyReportReference
        {
            UserId = userId,
            FileName = file.FileName,
            FilePath = filePath,
            ParsedText = parsedText,
            WeekStart = weekStart?.Date,
            WeekEnd = weekEnd?.Date,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim()
        };
        _db.WeeklyReportReferences.Add(entity);
        await _db.SaveChangesAsync(ct);
        return MapRef(entity);
    }

    public async Task<List<WeeklyReportReferenceDto>> GetReferencesAsync(int userId, CancellationToken ct = default)
    {
        return await _db.WeeklyReportReferences
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => MapRef(r))
            .ToListAsync(ct);
    }

    public async Task DeleteReferenceAsync(int userId, int id, CancellationToken ct = default)
    {
        var entity = await _db.WeeklyReportReferences
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct)
            ?? throw new BusinessException("参考文件不存在", 404);

        entity.IsDeleted = true;
        await _db.SaveChangesAsync(ct);
    }

    // ===== 私有辅助方法 =====

    private string BuildPrompt(DateTime weekStart, DateTime weekEnd, List<Data.Entities.WorkLog> workLogs, List<WeeklyReportReference> references, int detailLevel = 2)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是一名工作周报撰写助手。请根据下方【本周工作记录】，生成一份纯文本格式的工作周报。");
        sb.AppendLine();
        sb.AppendLine("【严格内容限制——最高优先级，不得违反】");
        sb.AppendLine("- 周报内容必须且只能来源于下方【本周工作记录】中已明确写出的信息。");
        sb.AppendLine("- 允许对记录的文字进行润色、精简、归纳，使其更通顺专业；");
        sb.AppendLine("- 禁止添加、推测或虚构任何记录中未提及的工作内容、步骤或结论。");
        sb.AppendLine("- 若某条记录的 \"目的\" 或 \"内容\" 为空，则对应字段跳过，不要自行补充。");
        sb.AppendLine("- 若本周无工作记录，如实输出 \"本周暂无工作记录。\" ，不要编造内容。");
        sb.AppendLine();
        sb.AppendLine("【输出复杂度要求】");
        sb.AppendLine(detailLevel switch
        {
            1 => "- 复杂度：简洁。每项工作只保留核心信息，过程步骤合并为 1~2 条短句，不展开细节，目的与结果只用一句话概括。",
            3 => "- 复杂度：详细。在不违背上述严格内容限制的前提下，尽量展开每个步骤的表述，补充必要的上下文连接词使叙述更完整，目的与结果可适当展开为 1~2 句，但仍不得新增记录中没有的事实。",
            _ => "- 复杂度：标准。按记录实际内容适度描述，既不过于精简也不过度展开。",
        });
        sb.AppendLine();
        sb.AppendLine("【输出格式要求】");
        sb.AppendLine("- 禁止使用 Markdown 语法（禁止 #、**、*、-、> 等符号）");
        sb.AppendLine("- 每项工作独立成块，块内依次包含：");
        sb.AppendLine("    工作标题（一行，直接写标题，不加序号和符号）");
        sb.AppendLine("    目的：（简述目标，一句话；原记录无此字段则省略）");
        sb.AppendLine("    过程：");
        sb.AppendLine("    1.（步骤一）");
        sb.AppendLine("    2.（步骤二）");
        sb.AppendLine("    …");
        sb.AppendLine("    结果：（完成情况；如只是完成了某件事，直接写 \"已完成\"；若记录中有状态备注，格式为 \"[状态]，[备注]\"，例如 \"进行中，计划下周完成\" ）");
        sb.AppendLine("- 每项工作结束后输出一行 60 个短横线作为分隔线：");
        sb.AppendLine("    ------------------------------------------------------------");
        sb.AppendLine("- 不需要总体概述，不需要下周计划，直接逐项列出工作内容");
        sb.AppendLine();

        if (references.Any())
        {
            sb.AppendLine("【参考资料（仅用于理解工作背景，参考资料的内容不得写入周报，格式严格按照上述要求）】");
            foreach (var r in references)
            {
                if (!string.IsNullOrWhiteSpace(r.Remark))
                    sb.AppendLine($"--- 参考文件：{r.Remark} ---");
                sb.AppendLine(r.ParsedText);
                sb.AppendLine();
            }
        }

        sb.AppendLine($"【本周工作记录（{weekStart:yyyy-MM-dd} 至 {weekEnd:yyyy-MM-dd}，严格只使用此范围内的记录）】");
        if (!workLogs.Any())
        {
            sb.AppendLine("（本周无工作记录）");
        }
        else
        {
            // 相同标题的记录合并：目的取第一条非空值，内容按日期顺序换行拼接，标签去重合并
            var groups = workLogs
                .GroupBy(w => w.Title.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Title = g.Key,
                    Dates = g.Select(w => w.LogDate).OrderBy(d => d).ToList(),
                    Purpose = g.Select(w => w.Purpose).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)),
                    Contents = g.Where(w => !string.IsNullOrWhiteSpace(w.Content))
                                .OrderBy(w => w.LogDate)
                                .Select(w => $"[{w.LogDate:MM-dd}] {w.Content!.Trim()}")
                                .ToList(),
                    Tags = g.SelectMany(w => (w.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                             .Where(t => !string.IsNullOrWhiteSpace(t))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .ToList(),
                    // 取最后一条有值的状态（最新的为准）
                    Status = g.OrderByDescending(w => w.LogDate).Select(w => w.Status).FirstOrDefault(),
                    StatusRemark = g.OrderByDescending(w => w.LogDate)
                                    .Select(w => w.StatusRemark)
                                    .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)),
                });

            foreach (var group in groups)
            {
                var dateRange = group.Dates.Count == 1
                    ? $"[{group.Dates[0]:MM-dd}]"
                    : $"[{group.Dates.First():MM-dd}~{group.Dates.Last():MM-dd}]";
                sb.AppendLine($"{dateRange} {group.Title}");
                if (!string.IsNullOrWhiteSpace(group.Purpose))
                    sb.AppendLine($"目的：{group.Purpose}");
                if (group.Contents.Any())
                {
                    sb.AppendLine("内容：");
                    foreach (var c in group.Contents)
                        sb.AppendLine(c);
                }
                if (group.Tags.Any())
                    sb.AppendLine($"标签：{string.Join(", ", group.Tags)}");
                // 状态（非"未标记"时才输出）
                if (group.Status != 0)
                {
                    var statusLabel = group.Status switch
                    {
                        1 => "进行中",
                        2 => "已完成",
                        3 => "已延期",
                        _ => null
                    };
                    if (statusLabel != null)
                    {
                        var statusLine = string.IsNullOrWhiteSpace(group.StatusRemark)
                            ? statusLabel
                            : $"{statusLabel}，{group.StatusRemark.Trim()}";
                        sb.AppendLine($"状态：{statusLine}");
                    }
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine("再次提醒：只能使用上方工作记录中已有的信息，不得臆测或添加任何未记录的内容。");
        sb.AppendLine("请严格按照上述格式要求输出周报，不要添加任何 Markdown 标记。");
        return sb.ToString();
    }

    private async Task<string> CallDeepSeekAsync(string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_deepSeekOptions.ApiKey))
            throw new BusinessException("DeepSeek API Key 未配置，请联系管理员", 500);

        var client = _httpClientFactory.CreateClient("DeepSeek");
        client.BaseAddress = new Uri(_deepSeekOptions.BaseUrl);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _deepSeekOptions.ApiKey);

        var body = new
        {
            model = _deepSeekOptions.Model,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var response = await client.PostAsJsonAsync("/v1/chat/completions", body, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: ct)
            ?? throw new BusinessException("AI 服务返回异常", 500);

        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    private static string ParseExcel(string filePath)
    {
        var sb = new StringBuilder();
        using var workbook = new XLWorkbook(filePath);
        foreach (var worksheet in workbook.Worksheets)
        {
            foreach (var row in worksheet.RowsUsed())
            {
                var cells = row.CellsUsed().Select(c => c.Value.ToString()).Where(v => !string.IsNullOrWhiteSpace(v));
                sb.AppendLine(string.Join("\t", cells));
            }
        }
        return sb.ToString();
    }

    private static WeeklyReportDto Map(WeeklyReport r) => new()
    {
        Id = r.Id,
        WeekStart = r.WeekStart,
        WeekEnd = r.WeekEnd,
        Content = r.Content,
        GeneratedAt = r.GeneratedAt,
        IsEdited = r.IsEdited,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    private static WeeklyReportReferenceDto MapRef(WeeklyReportReference r) => new()
    {
        Id = r.Id,
        FileName = r.FileName,
        WeekStart = r.WeekStart,
        WeekEnd = r.WeekEnd,
        Remark = r.Remark,
        CreatedAt = r.CreatedAt
    };
}
