using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using Xunit;

namespace MiraiNote.Tests;

public class ChatFileLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mirai-files-" + Guid.NewGuid().ToString("N"));
    private readonly ChatFileLibrary _library;

    public ChatFileLibraryTests()
    {
        Directory.CreateDirectory(_root);
        _library = new ChatFileLibrary(Options.Create(new FileSystemOptions
        {
            WorkspaceRoot = Path.Combine(_root, "workspace"),
            ExportsRoot = Path.Combine(_root, "exports")
        }));
    }

    [Fact]
    public void List_moves_loose_files_once_and_keeps_skills_uploads_and_generated()
    {
        var user = UserDir(7);
        File.WriteAllText(Path.Combine(user, "report.md"), "周报");
        Directory.CreateDirectory(Path.Combine(user, "charts"));
        File.WriteAllText(Path.Combine(user, "charts", "plot.png"), "png");
        Directory.CreateDirectory(Path.Combine(user, "skills", "digest"));
        File.WriteAllText(Path.Combine(user, "skills", "digest", "SKILL.md"), "技能");
        Directory.CreateDirectory(Path.Combine(user, "uploads", "2026", "09"));
        File.WriteAllText(Path.Combine(user, "uploads", "2026", "09", "kept.txt"), "上传");
        Directory.CreateDirectory(Path.Combine(user, "generated"));
        File.WriteAllText(Path.Combine(user, "generated", "keep.md"), "留下");

        var first = _library.List(7, new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, first.OrganizedCount);
        Assert.Contains("generated/archive/report.md", first.OrganizedPaths);
        Assert.Contains("generated/archive/charts", first.OrganizedPaths);
        Assert.False(File.Exists(Path.Combine(user, "report.md")));
        Assert.True(File.Exists(Path.Combine(user, "generated", "archive", "report.md")));
        Assert.True(File.Exists(Path.Combine(user, "generated", "archive", "charts", "plot.png")));
        Assert.True(File.Exists(Path.Combine(user, "skills", "digest", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(user, "uploads", "2026", "09", "kept.txt")));
        Assert.True(File.Exists(Path.Combine(user, "generated", "keep.md")));
        Assert.Contains(first.Generated, file => file.RelativePath == "generated/keep.md");
        Assert.Contains(first.Generated, file => file.RelativePath == "generated/archive/report.md" && file.Name == "report.md");
        Assert.Contains(first.Uploads, file => file.RelativePath == "uploads/2026/09/kept.txt");

        File.WriteAllText(Path.Combine(user, "later.txt"), "之后才出现");
        var second = _library.List(7, DateTime.UtcNow);
        Assert.Equal(0, second.OrganizedCount);
        Assert.True(File.Exists(Path.Combine(user, "later.txt")));
    }

    [Fact]
    public void SaveUpload_stores_under_shanghai_month_and_lists_the_friendly_name()
    {
        var path = _library.SaveUpload(3, "../年报.pdf", "pdf"u8.ToArray(), new DateTime(2026, 10, 6, 16, 30, 0, DateTimeKind.Utc));

        Assert.StartsWith("uploads/2026/10/", path);
        Assert.EndsWith("_年报.pdf", path);
        Assert.DoesNotContain("..", path);

        var listed = _library.List(3, DateTime.UtcNow);
        var file = Assert.Single(listed.Uploads);
        Assert.Equal("年报.pdf", file.Name);
        Assert.Equal(path, file.RelativePath);
        Assert.Equal("upload", file.Kind);
    }

    [Fact]
    public void List_includes_exports_with_a_download_url()
    {
        var dir = Path.Combine(_root, "exports", "4", "2026", "10");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "20261007010101000_周报.docx"), "doc");

        var listed = _library.List(4, DateTime.UtcNow);
        var file = Assert.Single(listed.Exports);
        Assert.Equal("周报.docx", file.Name);
        Assert.Equal("/api/v1/mirai/exports/4/2026/10/20261007010101000_%E5%91%A8%E6%8A%A5.docx", file.DownloadUrl);
    }

    [Fact]
    public void ResolvePrivateDownload_rejects_paths_outside_uploads_and_generated()
    {
        var user = UserDir(8);
        Directory.CreateDirectory(Path.Combine(user, "skills"));
        File.WriteAllText(Path.Combine(user, "skills", "secret.md"), "no");
        Directory.CreateDirectory(Path.Combine(user, "generated"));
        File.WriteAllText(Path.Combine(user, "generated", "ok.txt"), "yes");

        Assert.Null(_library.ResolvePrivateDownload(8, "../secrets.txt"));
        Assert.Null(_library.ResolvePrivateDownload(8, "skills/secret.md"));
        Assert.Null(_library.ResolvePrivateDownload(8, "uploads/../generated/ok.txt"));
        Assert.NotNull(_library.ResolvePrivateDownload(8, "generated/ok.txt"));
    }

    [Fact]
    public void Organize_skips_symlinks()
    {
        var user = UserDir(9);
        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, "outside");
        var link = Path.Combine(user, "link.txt");
        try
        {
            File.CreateSymbolicLink(link, outside);
        }
        catch (IOException)
        {
            return;
        }

        var listed = _library.List(9, DateTime.UtcNow);
        Assert.Equal(0, listed.OrganizedCount);
        Assert.True(File.Exists(link));
        Assert.Equal("outside", File.ReadAllText(outside));
        Assert.False(File.Exists(Path.Combine(user, "generated", "archive", "link.txt")));
    }

    [Fact]
    public void OrganizeAllUsers_only_touches_numeric_user_directories()
    {
        File.WriteAllText(Path.Combine(UserDir(2), "a.txt"), "a");
        var ignored = Path.Combine(_root, "workspace", "users", "guest");
        Directory.CreateDirectory(ignored);
        File.WriteAllText(Path.Combine(ignored, "b.txt"), "b");

        var moved = _library.OrganizeAllUsers(DateTime.UtcNow);

        Assert.Equal(1, moved);
        Assert.True(File.Exists(Path.Combine(_root, "workspace", "users", "2", "generated", "archive", "a.txt")));
        Assert.True(File.Exists(Path.Combine(ignored, "b.txt")));
    }

    private string UserDir(int userId)
    {
        var dir = Path.Combine(_root, "workspace", "users", userId.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
