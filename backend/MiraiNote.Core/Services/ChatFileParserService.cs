using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace MiraiNote.Core.Services;

/// <summary>
/// 聊天附件文件解析服务。
/// 支持 PDF、Word(.docx)、Excel(.xlsx/.xls)、纯文本及常见图片类型。
/// </summary>
public class ChatFileParserService
{
    /// <summary>支持的文本类文件扩展名（可直接读取内容）</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".xml", ".html", ".htm", ".yaml", ".yml",
        ".toml", ".ini", ".env", ".gitignore", ".log", ".sql", ".ts", ".js", ".jsx",
        ".tsx", ".py", ".cs", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".php",
        ".rb", ".sh", ".bat", ".ps1", ".vue", ".css", ".scss", ".less", ".conf",
        ".config", ".csproj", ".sln", ".dockerfile", ".tf", ".hcl"
    };

    /// <summary>支持的图片扩展名（返回占位描述，不尝试读取内容）</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico",
        ".tiff", ".tif", ".avif", ".heic", ".heif"
    };

    /// <summary>最大字符数（防止超出 AI 上下文窗口）</summary>
    private const int MaxTextChars = 50_000;

    /// <summary>
    /// 从流中提取文本内容。
    /// 返回提取的文本，如果为图片则返回图片说明字符串。
    /// </summary>
    public async Task<string> ExtractTextAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        // 图片类型
        if (ImageExtensions.Contains(ext))
            return $"[图片文件: {fileName}]";

        // PDF
        if (ext == ".pdf")
            return ExtractPdfText(fileStream, fileName);

        // Word
        if (ext == ".docx")
            return ExtractDocxText(fileStream, fileName);

        // Excel
        if (ext is ".xlsx" or ".xls")
            return ExtractExcelText(fileStream, fileName);

        // 纯文本及代码文件
        if (TextExtensions.Contains(ext) || IsLikelyText(ext))
        {
            using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = await reader.ReadToEndAsync(ct);
            return TruncateIfNeeded(text, fileName);
        }

        return $"[不支持的文件类型: {ext}，文件名: {fileName}]";
    }

    private static string ExtractPdfText(Stream stream, string fileName)
    {
        try
        {
            var sb = new StringBuilder();
            using var doc = PdfDocument.Open(stream);
            foreach (var page in doc.GetPages())
            {
                sb.AppendLine(page.Text);
                if (sb.Length > MaxTextChars) break;
            }
            var result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result)
                ? $"[PDF 文件 {fileName}：无法提取文本内容（可能是扫描件）]"
                : TruncateIfNeeded(result, fileName);
        }
        catch (Exception ex)
        {
            return $"[PDF 解析失败：{ex.Message}，文件名：{fileName}]";
        }
    }

    private static string ExtractDocxText(Stream stream, string fileName)
    {
        try
        {
            using var wordDoc = WordprocessingDocument.Open(stream, isEditable: false);
            var body = wordDoc.MainDocumentPart?.Document?.Body;
            if (body == null)
                return $"[Word 文件 {fileName}：无法读取正文内容]";

            var sb = new StringBuilder();
            foreach (var para in body.Elements<Paragraph>())
            {
                sb.AppendLine(para.InnerText);
                if (sb.Length > MaxTextChars) break;
            }
            var result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result)
                ? $"[Word 文件 {fileName}：文档内容为空]"
                : TruncateIfNeeded(result, fileName);
        }
        catch (Exception ex)
        {
            return $"[Word 解析失败：{ex.Message}，文件名：{fileName}]";
        }
    }

    private static string ExtractExcelText(Stream stream, string fileName)
    {
        try
        {
            using var workbook = new XLWorkbook(stream);
            var sb = new StringBuilder();
            foreach (var worksheet in workbook.Worksheets)
            {
                sb.AppendLine($"=== 工作表：{worksheet.Name} ===");
                var usedRange = worksheet.RangeUsed();
                if (usedRange == null) continue;

                foreach (var row in usedRange.RowsUsed())
                {
                    var cells = row.CellsUsed().Select(c => c.GetValue<string>() ?? "");
                    sb.AppendLine(string.Join("\t", cells));
                    if (sb.Length > MaxTextChars) break;
                }
                if (sb.Length > MaxTextChars) break;
            }
            var result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result)
                ? $"[Excel 文件 {fileName}：表格内容为空]"
                : TruncateIfNeeded(result, fileName);
        }
        catch (Exception ex)
        {
            return $"[Excel 解析失败：{ex.Message}，文件名：{fileName}]";
        }
    }

    private static string TruncateIfNeeded(string text, string fileName)
    {
        if (text.Length <= MaxTextChars) return text;
        return text[..MaxTextChars] + $"\n\n... [文件内容已截断，共 {text.Length} 字符，文件名：{fileName}]";
    }

    private static bool IsLikelyText(string ext)
    {
        // 没有扩展名或未知扩展名，暂时尝试作为文本读取
        return string.IsNullOrEmpty(ext);
    }
}
