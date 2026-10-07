using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services.Mirai;
using MiraiNote.Core.Services.Tools;

namespace MiraiNote.Core.Services;

/// <summary>
/// Mirai Chat 的文件布局：uploads 放用户上传，generated 放工作模式产物，
/// exports 仍是交付成品。打开文件库时，把私有区根目录里已经生成、且不属于
/// 技能或上传目录的文件归入 generated/archive。
/// </summary>
public sealed class ChatFileLibrary
{
    public const string UploadsFolder = "uploads";
    public const string GeneratedFolder = "generated";
    public const string ArchiveFolder = "archive";
    public const string SkillsFolder = "skills";
    public const string LayoutMarkerName = ".mirainote-file-layout";
    public const int MaxListedFiles = 400;
    private const int MaxScanEntries = 5000;

    private static readonly Regex StoredNamePrefix = new(@"^\d{17}_(?<name>.+)$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions MarkerJson = new() { WriteIndented = false };
    private static readonly ConcurrentDictionary<int, object> Gates = new();

    private readonly FileSystemOptions _options;

    public ChatFileLibrary(IOptions<FileSystemOptions> options) => _options = options.Value;

    public string SaveUpload(int userId, string originalFileName, byte[] content, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(content);
        lock (Gate(userId))
        {
            var local = ToShanghai(utcNow);
            var safe = SafeFileName(originalFileName);
            var relativeDir = $"{UploadsFolder}/{local:yyyy}/{local:MM}";
            var privateRoot = PrivateRoot(userId);
            var directory = Path.Combine(privateRoot, relativeDir.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(directory);

            var storedName = $"{local:yyyyMMddHHmmssfff}_{safe}";
            var full = Path.Combine(directory, storedName);
            var suffix = 1;
            while (File.Exists(full))
            {
                storedName = $"{local:yyyyMMddHHmmssfff}_{suffix}_{safe}";
                full = Path.Combine(directory, storedName);
                suffix++;
            }

            File.WriteAllBytes(full, content);
            return $"{relativeDir}/{storedName}";
        }
    }

    public ChatFileLibrarySnapshot List(int userId, DateTime utcNow)
    {
        lock (Gate(userId))
        {
            var organized = OrganizeUserCore(userId, utcNow);
            var privateRoot = PrivateRoot(userId);
            var uploads = ListTree(
                Path.Combine(privateRoot, UploadsFolder),
                privateRoot,
                "upload",
                downloadUrl: null,
                out var uploadsTruncated);
            var generated = ListTree(
                Path.Combine(privateRoot, GeneratedFolder),
                privateRoot,
                "generated",
                downloadUrl: null,
                out var generatedTruncated);
            var exportsRoot = MiraiFileStorage.ExportsRoot(_options);
            var userExports = Path.Combine(exportsRoot, userId.ToString());
            var exports = ListTree(
                userExports,
                exportsRoot,
                "export",
                path => ExportUrl(exportsRoot, path),
                out var exportsTruncated);

            return new ChatFileLibrarySnapshot
            {
                OrganizedCount = organized.Moved.Count,
                OrganizedPaths = organized.Moved,
                Uploads = uploads,
                Generated = generated,
                Exports = exports,
                UploadsTruncated = uploadsTruncated,
                GeneratedTruncated = generatedTruncated,
                ExportsTruncated = exportsTruncated
            };
        }
    }

    public int OrganizeAllUsers(DateTime utcNow)
    {
        var usersRoot = Path.Combine(WorkspacePaths.Root(_options), "users");
        if (!Directory.Exists(usersRoot)) return 0;

        var total = 0;
        foreach (var dir in Directory.EnumerateDirectories(usersRoot))
        {
            var name = Path.GetFileName(dir);
            if (!int.TryParse(name, out var userId) || userId < 0) continue;
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            total += List(userId, utcNow).OrganizedCount;
        }

        return total;
    }

    public string? ResolvePrivateDownload(int userId, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var normalized = relativePath.Replace('\\', '/').Trim();
        if (normalized.Contains("..", StringComparison.Ordinal)) return null;
        if (!normalized.StartsWith(UploadsFolder + "/", StringComparison.OrdinalIgnoreCase)
            && !normalized.StartsWith(GeneratedFolder + "/", StringComparison.OrdinalIgnoreCase))
            return null;

        var root = WorkspacePaths.Root(_options);
        var (resolved, isPublic) = WorkspacePaths.Resolve(normalized, root, userId);
        if (resolved == null || isPublic || !File.Exists(resolved)) return null;
        if ((File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0) return null;

        var privateRoot = EnsureTrailingSeparator(Path.GetFullPath(PrivateRoot(userId)));
        var full = Path.GetFullPath(resolved);
        if (!full.StartsWith(privateRoot, StringComparison.OrdinalIgnoreCase)) return null;
        return full;
    }

    private ChatFileOrganizeResult OrganizeUserCore(int userId, DateTime utcNow)
    {
        var privateRoot = PrivateRoot(userId);
        if (!Directory.Exists(privateRoot))
            return new ChatFileOrganizeResult();

        var generatedRoot = Path.Combine(privateRoot, GeneratedFolder);
        var marker = Path.Combine(generatedRoot, LayoutMarkerName);
        if (File.Exists(marker))
            return new ChatFileOrganizeResult();

        Directory.CreateDirectory(generatedRoot);
        var archive = Path.Combine(generatedRoot, ArchiveFolder);
        var moved = new List<string>();
        var failed = false;

        foreach (var entry in Directory.EnumerateFileSystemEntries(privateRoot))
        {
            var name = Path.GetFileName(entry);
            if (string.IsNullOrEmpty(name) || name.StartsWith('.')) continue;
            if (IsReservedTopLevel(name)) continue;

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (IOException)
            {
                failed = true;
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                failed = true;
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;

            var destination = UniqueDestination(archive, name);
            try
            {
                Directory.CreateDirectory(archive);
                if ((attributes & FileAttributes.Directory) != 0)
                    Directory.Move(entry, destination);
                else
                    File.Move(entry, destination);

                var relative = Path.GetRelativePath(privateRoot, destination).Replace('\\', '/');
                moved.Add(relative);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed = true;
            }
        }

        if (!failed)
        {
            var payload = JsonSerializer.Serialize(new
            {
                organizedAtUtc = utcNow,
                moved
            }, MarkerJson);
            File.WriteAllText(marker, payload);
        }

        return new ChatFileOrganizeResult { Moved = moved };
    }

    private static List<ChatLibraryFile> ListTree(
        string root,
        string relativeBase,
        string kind,
        Func<string, string?>? downloadUrl,
        out bool truncated)
    {
        truncated = false;
        var found = new List<FileInfo>();
        if (!Directory.Exists(root)) return new List<ChatLibraryFile>();

        var scanned = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (++scanned > MaxScanEntries)
                {
                    truncated = true;
                    pending.Clear();
                    break;
                }

                var name = Path.GetFileName(entry);
                if (name.StartsWith('.')) continue;

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                found.Add(new FileInfo(entry));
            }
        }

        if (found.Count > MaxListedFiles) truncated = true;

        return found
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListedFiles)
            .Select(file =>
            {
                var relative = Path.GetRelativePath(relativeBase, file.FullName).Replace('\\', '/');
                return new ChatLibraryFile
                {
                    Kind = kind,
                    Name = DisplayName(file.Name),
                    RelativePath = relative,
                    SizeBytes = file.Length,
                    Extension = file.Extension.ToLowerInvariant(),
                    ModifiedAt = file.LastWriteTimeUtc,
                    DownloadUrl = downloadUrl?.Invoke(file.FullName)
                };
            })
            .ToList();
    }

    private string PrivateRoot(int userId) =>
        WorkspacePaths.UserPrivate(WorkspacePaths.Root(_options), userId);

    private static string ExportUrl(string exportsRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(exportsRoot, fullPath).Replace('\\', '/');
        var encoded = string.Join("/", relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        return "/api/v1/mirai/exports/" + encoded;
    }

    private static bool IsReservedTopLevel(string name) =>
        name.Equals(UploadsFolder, StringComparison.OrdinalIgnoreCase)
        || name.Equals(GeneratedFolder, StringComparison.OrdinalIgnoreCase)
        || name.Equals(SkillsFolder, StringComparison.OrdinalIgnoreCase);

    private static string UniqueDestination(string archiveDir, string name)
    {
        var destination = Path.Combine(archiveDir, name);
        if (!File.Exists(destination) && !Directory.Exists(destination)) return destination;
        return Path.Combine(archiveDir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{name}");
    }

    internal static string SafeFileName(string original)
    {
        var name = Path.GetFileName((original ?? "").Replace('\\', '/')).Trim();
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(ch => invalid.Contains(ch) || ch == ':' ? '_' : ch).ToArray());
        name = name.Replace("..", "_", StringComparison.Ordinal).Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        if (name.Length > 120) name = name[..120];
        return name;
    }

    public static string DisplayName(string fileName)
    {
        var match = StoredNamePrefix.Match(fileName);
        return match.Success ? match.Groups["name"].Value : fileName;
    }

    private static DateTime ToShanghai(DateTime utcNow)
    {
        var utc = utcNow.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)
            : utcNow.ToUniversalTime();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
    }

    private static object Gate(int userId) => Gates.GetOrAdd(userId, static _ => new object());

    private static string EnsureTrailingSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}

public sealed class ChatFileOrganizeResult
{
    public List<string> Moved { get; init; } = new();
}

public sealed class ChatFileLibrarySnapshot
{
    public int OrganizedCount { get; init; }
    public IReadOnlyList<string> OrganizedPaths { get; init; } = Array.Empty<string>();
    public List<ChatLibraryFile> Uploads { get; init; } = new();
    public List<ChatLibraryFile> Generated { get; init; } = new();
    public List<ChatLibraryFile> Exports { get; init; } = new();
    public bool UploadsTruncated { get; init; }
    public bool GeneratedTruncated { get; init; }
    public bool ExportsTruncated { get; init; }
}

public sealed class ChatLibraryFile
{
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Extension { get; init; } = "";
    public DateTime ModifiedAt { get; init; }
    public string? DownloadUrl { get; init; }
}
