using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MiraiNote.Shared.Agent;

namespace MiraiNote.Core.Services.Tools;

/// <summary>
/// Workspace path helper.
/// Structure: {WorkspaceRoot}/public/ (shared read-only) and {WorkspaceRoot}/users/{id}/ (per-user private).
/// </summary>
public static class WorkspacePaths
{
    public static string Root(FileSystemOptions options) =>
        string.IsNullOrWhiteSpace(options.WorkspaceRoot)
            ? Path.Combine(Directory.GetCurrentDirectory(), "workspace")
            : options.WorkspaceRoot;

    public static string UserPrivate(string root, int userId) =>
        Path.Combine(root, "users", userId.ToString());

    public static string Public(string root) =>
        Path.Combine(root, "public");

    /// <summary>
    /// Resolves a path input to an absolute path.
    /// - "public/..." -> public area (read-only)
    /// - Relative path -> user private area
    /// Returns (resolvedPath, isPublic). Returns null on path traversal.
    /// </summary>
    public static (string? path, bool isPublic) Resolve(string input, string root, int userId)
    {
        var publicRoot = EnsureTrailingSep(Path.GetFullPath(Public(root)));
        var privateRoot = EnsureTrailingSep(Path.GetFullPath(UserPrivate(root, userId)));

        string candidate;

        if (input.StartsWith("public/", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("public\\", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("public", StringComparison.OrdinalIgnoreCase))
        {
            var sub = input.Length > 7 ? input[7..] : "";
            candidate = string.IsNullOrEmpty(sub)
                ? publicRoot.TrimEnd(Path.DirectorySeparatorChar)
                : Path.Combine(publicRoot.TrimEnd(Path.DirectorySeparatorChar), sub);
        }
        else if (Path.IsPathRooted(input))
        {
            candidate = Path.GetFullPath(input);
        }
        else
        {
            candidate = Path.GetFullPath(Path.Combine(privateRoot.TrimEnd(Path.DirectorySeparatorChar), input));
        }

        var full = Path.GetFullPath(candidate);
        if (full.StartsWith(publicRoot, StringComparison.OrdinalIgnoreCase))
            return (full, true);
        if (full.StartsWith(privateRoot, StringComparison.OrdinalIgnoreCase))
            return (full, false);

        return (null, false);
    }

    private static string EnsureTrailingSep(string p) =>
        p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}

/// <summary>
/// Server-side file read tool.
/// Readable areas: user private (users/{userId}/) and shared public (public/).
/// Path format: relative path for private; "public/..." prefix for public area.
/// </summary>
public class ServerFileReadTool : IServerAgentTool
{
    private readonly FileSystemOptions _options;

    public string Name => "read_file";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "读取工作区文件内容（文本/代码/配置等）。直接写相对路径访问私有区域；public/ 前缀访问共享公共区域。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["path"] = ToolParameterProperty.String("文件路径（必填）。如 notes.md 或 public/readme.txt"),
            ["max_lines"] = ToolParameterProperty.Integer("最大读取行数，默认 500")
        },
        Required = new() { "path" }
    };

    public ServerFileReadTool(IOptions<FileSystemOptions> options) { _options = options.Value; }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "path", out var path))
            return Task.FromResult("读取失败：path 为必填项。");

        var root = WorkspacePaths.Root(_options);
        var (resolved, _) = WorkspacePaths.Resolve(path, root, userId);
        if (resolved == null)
            return Task.FromResult("安全限制：路径越界，只能访问私有或公共工作区。");

        if (!File.Exists(resolved))
            return Task.FromResult($"文件不存在：{path}");

        ToolArgHelper.TryGetInt(args, "max_lines", out var maxLines);
        if (maxLines <= 0) maxLines = 500;

        try
        {
            var lines = File.ReadAllLines(resolved, System.Text.Encoding.UTF8);
            var total = lines.Length;
            if (total <= maxLines)
                return Task.FromResult(string.Join("\n", lines));
            var preview = string.Join("\n", lines.Take(maxLines));
            return Task.FromResult($"{preview}\n\n... (共 {total} 行，以上为前 {maxLines} 行)");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"读取文件失败：{ex.Message}");
        }
    }

    // Legacy static helpers (kept for backward compat with ChatAttachmentController etc.)
    internal static string ResolveWorkspaceRoot(FileSystemOptions options) => WorkspacePaths.Root(options);
    internal static string? ResolvePath(string path, string workspaceRoot)
    {
        if (!Path.IsPathRooted(path))
            path = Path.Combine(workspaceRoot, path);
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}

/// <summary>
/// Server-side file write tool.
/// Only writes to user private area (users/{userId}/). Public area is read-only.
/// </summary>
public class ServerFileWriteTool : IServerAgentTool
{
    private readonly FileSystemOptions _options;

    public string Name => "write_file";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public string Description =>
        "将内容写入工作区私有文件（覆盖写入）。只能写入自己的私有区域，不能写入公共区域。" +
        (_options.AllowWrite ? "" : "（当前已禁用写入）");

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["path"] = ToolParameterProperty.String("文件路径（必填），相对路径写入私有区域"),
            ["content"] = ToolParameterProperty.String("要写入的内容（必填）")
        },
        Required = new() { "path", "content" }
    };

    public ServerFileWriteTool(IOptions<FileSystemOptions> options) { _options = options.Value; }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        if (!_options.AllowWrite)
            return Task.FromResult("写入被禁用（FileSystem.AllowWrite = false）。");

        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "path", out var path))
            return Task.FromResult("写入失败：path 为必填项。");
        if (!ToolArgHelper.TryGetString(args, "content", out var content))
            return Task.FromResult("写入失败：content 为必填项。");

        var root = WorkspacePaths.Root(_options);
        var (resolved, isPublic) = WorkspacePaths.Resolve(path, root, userId);

        if (resolved == null)
            return Task.FromResult("安全限制：路径越界。");
        if (isPublic)
            return Task.FromResult("权限不足：不允许写入公共区域（public/）。");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
            File.WriteAllText(resolved, content, System.Text.Encoding.UTF8);
            var relPath = Path.GetRelativePath(WorkspacePaths.UserPrivate(root, userId), resolved);
            return Task.FromResult($"已写入私有文件：{relPath}（{content.Length} 字符）");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"写入文件失败：{ex.Message}");
        }
    }
}

/// <summary>
/// Server-side directory list tool.
/// Default lists user private area root; "public/" prefix lists shared public area.
/// </summary>
public class ServerFileListTool : IServerAgentTool
{
    private readonly FileSystemOptions _options;

    public string Name => "list_files";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Safe;
    public string Description =>
        "列出工作区目录内容。默认列出私有区域根目录；path=public/ 列出公共区域。";

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["path"] = ToolParameterProperty.String("目录路径，不填默认私有根目录；public/ 查看公共区域"),
            ["pattern"] = ToolParameterProperty.String("文件匹配模式，如 *.cs，不填列出所有")
        }
    };

    public ServerFileListTool(IOptions<FileSystemOptions> options) { _options = options.Value; }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        var args = JsonDocument.Parse(argumentsJson).RootElement;
        ToolArgHelper.TryGetString(args, "path", out var path);
        ToolArgHelper.TryGetString(args, "pattern", out var pattern);
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "*";

        var root = WorkspacePaths.Root(_options);

        string resolved;
        if (string.IsNullOrWhiteSpace(path))
        {
            resolved = WorkspacePaths.UserPrivate(root, userId);
        }
        else
        {
            var (r, _) = WorkspacePaths.Resolve(path, root, userId);
            if (r == null) return Task.FromResult("安全限制：路径越界。");
            resolved = r;
        }

        if (!Directory.Exists(resolved))
        {
            Directory.CreateDirectory(resolved);
            return Task.FromResult(JsonSerializer.Serialize(new { path = resolved, pattern, count = 0, entries = Array.Empty<object>() }));
        }

        try
        {
            var entries = new List<object>();
            foreach (var dir in Directory.GetDirectories(resolved))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(".") || name == "node_modules" || name == "bin" || name == "obj") continue;
                entries.Add(new { type = "dir", name, path = dir });
            }
            var files = Directory.GetFiles(resolved, pattern).Take(100).ToList();
            entries.AddRange(files.Select(f => new { type = "file", name = Path.GetFileName(f), size = new FileInfo(f).Length, path = f }));
            return Task.FromResult(JsonSerializer.Serialize(new { path = resolved, pattern, count = entries.Count, entries = entries.Take(100) }));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"列出目录失败：{ex.Message}");
        }
    }
}

/// <summary>
/// Server-side shell command execution tool.
/// Runs in user private workspace only. Public area is protected.
/// </summary>
public class ServerShellTool : IServerAgentTool
{
    private readonly FileSystemOptions _options;

    private static readonly HashSet<string> DangerousCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm -rf /", "del /f /s c:", "format", "shutdown", "reboot",
        "dd if=", "mkfs", "chmod 777 /", "rd /s /q c:"
    };

    public string Name => "run_shell";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Dangerous;
    public string Description =>
        "执行 Shell 命令（Windows cmd /c）。运行在当前用户的私有工作区目录下，超时 30 秒。" +
        "支持所有常规命令：dir/ls、mkdir、del/rd、copy/move/rename、echo、type、python、node、git、curl、wget 等。" +
        "也支持通过 Python（requests、playwright、selenium）实现网页登录、页面抓取、HTTP 请求、自动化操作等任务。" +
        "仅禁止以下极少数破坏性命令（精确匹配）：rm -rf /、del /f /s c:、format、shutdown、reboot、dd if=、mkfs、chmod 777 /、rd /s /q c:。" +
        "不可在公共区域（public/）执行命令。" +
        (_options.AllowShell ? "" : "（当前已禁用，FileSystem.AllowShell = false）");

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["command"] = ToolParameterProperty.String("要执行的命令（必填）"),
            ["working_dir"] = ToolParameterProperty.String("工作目录，不填默认用户私有工作区根目录")
        },
        Required = new() { "command" }
    };

    public ServerShellTool(IOptions<FileSystemOptions> options) { _options = options.Value; }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public async Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        if (!_options.AllowShell)
            return "Shell 执行被禁用（FileSystem.AllowShell = false）。";

        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "command", out var command))
            return "执行失败：command 为必填项。";

        if (DangerousCommands.Any(dc => command.Contains(dc, StringComparison.OrdinalIgnoreCase)))
            return "安全限制：禁止执行危险命令。";

        var root = WorkspacePaths.Root(_options);
        var privateRoot = WorkspacePaths.UserPrivate(root, userId);

        ToolArgHelper.TryGetString(args, "working_dir", out var workingDir);
        string resolved;
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            resolved = privateRoot;
        }
        else
        {
            var (r, isPublic) = WorkspacePaths.Resolve(workingDir, root, userId);
            if (r == null) return "安全限制：工作目录越界。";
            if (isPublic) return "安全限制：不允许在公共区域执行命令。";
            resolved = r;
        }

        Directory.CreateDirectory(resolved);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = resolved,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);

            var exited = process.WaitForExit(30_000);
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                return "命令执行超时（30 秒）。";
            }

            var output = stdout.TrimEnd();
            if (!string.IsNullOrEmpty(stderr))
                output += (output.Length > 0 ? "\n" : "") + $"[stderr]\n{stderr.TrimEnd()}";
            if (string.IsNullOrEmpty(output))
                output = $"(exit code: {process.ExitCode})";

            return output.Length > 5000 ? output[..5000] + "\n... (输出已截断)" : output;
        }
        catch (Exception ex)
        {
            return $"执行命令失败：{ex.Message}";
        }
    }
}

/// <summary>
/// 删除工作区私有文件或目录（不允许操作公共区域）。
/// </summary>
public class ServerFileDeleteTool : IServerAgentTool
{
    private readonly FileSystemOptions _options;

    public string Name => "delete_file";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public string Description =>
        "删除工作区私有区域的文件或目录（目录会递归删除）。只能操作自己的私有工作区，不能删除公共区域文件。" +
        (_options.AllowWrite ? "" : "（当前已禁用写操作）");

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["path"] = ToolParameterProperty.String("要删除的文件或目录路径（必填），相对路径指私有区域")
        },
        Required = new() { "path" }
    };

    public ServerFileDeleteTool(IOptions<FileSystemOptions> options) { _options = options.Value; }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        if (!_options.AllowWrite)
            return Task.FromResult("删除被禁用（FileSystem.AllowWrite = false）。");

        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "path", out var path))
            return Task.FromResult("删除失败：path 为必填项。");

        var root = WorkspacePaths.Root(_options);
        var (resolved, isPublic) = WorkspacePaths.Resolve(path, root, userId);

        if (resolved == null)
            return Task.FromResult("安全限制：路径越界。");
        if (isPublic)
            return Task.FromResult("权限不足：不允许删除公共区域文件。");

        try
        {
            if (File.Exists(resolved))
            {
                File.Delete(resolved);
                var rel = Path.GetRelativePath(WorkspacePaths.UserPrivate(root, userId), resolved);
                return Task.FromResult($"已删除文件：{rel}");
            }
            else if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
                var rel = Path.GetRelativePath(WorkspacePaths.UserPrivate(root, userId), resolved);
                return Task.FromResult($"已删除目录（含子文件）：{rel}");
            }
            else
            {
                return Task.FromResult($"路径不存在：{path}");
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult($"删除失败：{ex.Message}");
        }
    }
}

/// <summary>
/// 在工作区私有区域内移动或重命名文件/目录（不允许跨出私有区域）。
/// </summary>
public class ServerFileMoveOrRenameTool : IServerAgentTool
{
    private readonly FileSystemOptions _options;

    public string Name => "move_file";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Write;
    public string Description =>
        "在私有工作区内移动或重命名文件/目录。源路径和目标路径均须在自己的私有区域内，不允许涉及公共区域。" +
        (_options.AllowWrite ? "" : "（当前已禁用写操作）");

    public ToolParameterSchema Parameters => new()
    {
        Properties = new()
        {
            ["source"] = ToolParameterProperty.String("源路径（必填），相对路径指私有区域"),
            ["destination"] = ToolParameterProperty.String("目标路径（必填），相对路径指私有区域；若目标是已有目录则移动进该目录")
        },
        Required = new() { "source", "destination" }
    };

    public ServerFileMoveOrRenameTool(IOptions<FileSystemOptions> options) { _options = options.Value; }

    Task<string> IAgentTool.ExecuteAsync(string argumentsJson, CancellationToken ct)
        => ExecuteAsync(0, argumentsJson, ct);

    public Task<string> ExecuteAsync(int userId, string argumentsJson, CancellationToken ct = default)
    {
        if (!_options.AllowWrite)
            return Task.FromResult("移动被禁用（FileSystem.AllowWrite = false）。");

        var args = JsonDocument.Parse(argumentsJson).RootElement;
        if (!ToolArgHelper.TryGetString(args, "source", out var source))
            return Task.FromResult("移动失败：source 为必填项。");
        if (!ToolArgHelper.TryGetString(args, "destination", out var destination))
            return Task.FromResult("移动失败：destination 为必填项。");

        var root = WorkspacePaths.Root(_options);
        var privateRoot = WorkspacePaths.UserPrivate(root, userId);

        var (srcResolved, srcPublic) = WorkspacePaths.Resolve(source, root, userId);
        if (srcResolved == null) return Task.FromResult("安全限制：源路径越界。");
        if (srcPublic) return Task.FromResult("权限不足：不允许移动公共区域文件。");

        var (dstResolved, dstPublic) = WorkspacePaths.Resolve(destination, root, userId);
        if (dstResolved == null) return Task.FromResult("安全限制：目标路径越界。");
        if (dstPublic) return Task.FromResult("权限不足：不允许移动到公共区域。");

        try
        {
            if (!File.Exists(srcResolved) && !Directory.Exists(srcResolved))
                return Task.FromResult($"源路径不存在：{source}");

            // 若目标是已有目录，则移动进该目录
            var finalDst = Directory.Exists(dstResolved)
                ? Path.Combine(dstResolved, Path.GetFileName(srcResolved))
                : dstResolved;

            Directory.CreateDirectory(Path.GetDirectoryName(finalDst)!);

            if (File.Exists(srcResolved))
                File.Move(srcResolved, finalDst, overwrite: true);
            else
                Directory.Move(srcResolved, finalDst);

            var srcRel = Path.GetRelativePath(privateRoot, srcResolved);
            var dstRel = Path.GetRelativePath(privateRoot, finalDst);
            return Task.FromResult($"已移动：{srcRel} → {dstRel}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"移动失败：{ex.Message}");
        }
    }
}