using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.Tools;
using MiraiNote.Shared.Common;

namespace MiraiNote.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/workspace")]
public class WorkspaceController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly FileSystemOptions _fsOptions;
    private readonly ChatFileParserService _parser;

    public WorkspaceController(
        ICurrentUserService currentUser,
        IOptions<FileSystemOptions> fsOptions,
        ChatFileParserService parser)
    {
        _currentUser = currentUser;
        _fsOptions = fsOptions.Value;
        _parser = parser;
    }

    /// <summary>
    /// 浏览工作区目录。
    /// scope = private（默认）→ 用户私有区域；scope = public → 公共区域。
    /// path 为相对于所选区域的子路径。
    /// </summary>
    [HttpGet("files")]
    public ActionResult<ApiResponse<WorkspaceDirDto>> BrowseFiles(
        [FromQuery] string? path,
        [FromQuery] string scope = "private")
    {
        var userId = _currentUser.UserId;
        var root = WorkspacePaths.Root(_fsOptions);

        string baseDir;
        if (scope == "public")
        {
            baseDir = WorkspacePaths.Public(root);
            Directory.CreateDirectory(baseDir);
        }
        else
        {
            baseDir = WorkspacePaths.UserPrivate(root, userId);
            Directory.CreateDirectory(baseDir);
        }

        // 解析子路径（防路径遍历）
        string targetDir;
        if (string.IsNullOrWhiteSpace(path) || path == "." || path == "/")
        {
            targetDir = baseDir;
        }
        else
        {
            var candidate = Path.GetFullPath(Path.Combine(baseDir, path.TrimStart('/', '\\')));
            var baseFull = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                return BadRequest(ApiResponse.Fail("路径越界"));
            targetDir = candidate;
        }

        if (!Directory.Exists(targetDir))
            return NotFound(ApiResponse.Fail("目录不存在"));

        var entries = new List<WorkspaceEntryDto>();

        // 子目录
        foreach (var dir in Directory.GetDirectories(targetDir))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith(".") || name == "node_modules" || name == "bin" || name == "obj")
                continue;
            var rel = Path.GetRelativePath(baseDir, dir).Replace('\\', '/');
            entries.Add(new WorkspaceEntryDto { Name = name, RelativePath = rel, Type = "dir" });
        }

        // 文件
        foreach (var file in Directory.GetFiles(targetDir).Take(200))
        {
            var name = Path.GetFileName(file);
            var rel = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
            entries.Add(new WorkspaceEntryDto
            {
                Name = name,
                RelativePath = rel,
                Type = "file",
                SizeBytes = new FileInfo(file).Length,
                Extension = Path.GetExtension(file).ToLowerInvariant()
            });
        }

        var currentRel = Path.GetRelativePath(baseDir, targetDir).Replace('\\', '/');
        if (currentRel == ".") currentRel = "";

        return Ok(ApiResponse<WorkspaceDirDto>.Ok(new WorkspaceDirDto
        {
            Scope = scope,
            CurrentPath = currentRel,
            Entries = entries
        }));
    }

    /// <summary>
    /// 从工作区读取文件并提取文本内容，供前端附加到聊天消息。
    /// scope = private（默认）| public。
    /// </summary>
    [HttpPost("attach")]
    public async Task<ActionResult<ApiResponse<WorkspaceAttachDto>>> AttachFile(
        [FromBody] WorkspaceAttachRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            return BadRequest(ApiResponse.Fail("path 不能为空"));

        var userId = _currentUser.UserId;
        var root = WorkspacePaths.Root(_fsOptions);

        string baseDir = request.Scope == "public"
            ? WorkspacePaths.Public(root)
            : WorkspacePaths.UserPrivate(root, userId);

        var candidate = Path.GetFullPath(Path.Combine(baseDir, request.Path.TrimStart('/', '\\')));
        var baseFull = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse.Fail("路径越界"));

        if (!System.IO.File.Exists(candidate))
            return NotFound(ApiResponse.Fail("文件不存在"));

        var fileInfo = new FileInfo(candidate);
        if (fileInfo.Length > 20 * 1024 * 1024)
            return BadRequest(ApiResponse.Fail("文件超过 20MB 限制"));

        await using var stream = System.IO.File.OpenRead(candidate);
        var text = await _parser.ExtractTextAsync(stream, fileInfo.Name, ct);

        var ext = fileInfo.Extension.ToLowerInvariant();
        var fileType = ext switch
        {
            ".pdf" => "PDF",
            ".docx" => "Word",
            ".xlsx" or ".xls" => "Excel",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" or ".tiff" or ".tif" or ".avif" => "图片",
            _ => "文本"
        };

        return Ok(ApiResponse<WorkspaceAttachDto>.Ok(new WorkspaceAttachDto
        {
            FileName = fileInfo.Name,
            FileType = fileType,
            TextContent = text,
            FileSizeBytes = fileInfo.Length,
            RelativePath = request.Path,
            Scope = request.Scope ?? "private"
        }));
    }
}

public class WorkspaceDirDto
{
    public string Scope { get; set; } = "private";
    public string CurrentPath { get; set; } = "";
    public List<WorkspaceEntryDto> Entries { get; set; } = new();
}

public class WorkspaceEntryDto
{
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Type { get; set; } = "file"; // "file" | "dir"
    public long SizeBytes { get; set; }
    public string Extension { get; set; } = "";
}

public class WorkspaceAttachRequest
{
    public string Path { get; set; } = "";
    public string? Scope { get; set; } = "private";
}

public class WorkspaceAttachDto
{
    public string FileName { get; set; } = "";
    public string FileType { get; set; } = "";
    public string TextContent { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string RelativePath { get; set; } = "";
    public string Scope { get; set; } = "private";
}
