using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.Mirai;
using MiraiNote.Core.Services.Tools;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;
using Xunit;

namespace MiraiNote.Tests;

/// <summary>
/// Chat 会话 M1 扩展规则（契约 §2.9/2.10）：sessionType 校验、context 挂载对象存在性、快照装配。
/// </summary>
public class ChatSessionExtensionTests : IDisposable
{
    private const int UserId = 1;
    private readonly MiraiTestFixture _fx;

    public ChatSessionExtensionTests() => _fx = new MiraiTestFixture();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Validate_Default_IsLegacyWithoutAttachment()
    {
        await using var db = _fx.CreateContext();
        var result = await MiraiSessionRules.ValidateAsync(null, null, null, db, UserId);
        Assert.Equal("legacy", result.SessionType);
        Assert.Null(result.AttachToType);
        Assert.Null(result.AttachToObjectId);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("Context")] // 大小写敏感
    public async Task Validate_UnknownSessionType_400(string sessionType)
    {
        await using var db = _fx.CreateContext();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            MiraiSessionRules.ValidateAsync(sessionType, null, null, db, UserId));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Validate_ContextWithoutAttach_400()
    {
        await using var db = _fx.CreateContext();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            MiraiSessionRules.ValidateAsync("context", null, null, db, UserId));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Validate_ContextWithMissingObject_400()
    {
        await using var db = _fx.CreateContext();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            MiraiSessionRules.ValidateAsync("context", "worklog", 424242, db, UserId));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Validate_CommandWithAttachFields_400()
    {
        await using var db = _fx.CreateContext();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            MiraiSessionRules.ValidateAsync("command", "memo", 1, db, UserId));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Validate_AttachToTypeInvalid_400()
    {
        await using var db = _fx.CreateContext();
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            MiraiSessionRules.ValidateAsync("context", "project", 1, db, UserId));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Validate_ContextWithExistingObject_Passes()
    {
        int memoId;
        await using (var db = _fx.CreateContext())
        {
            var memo = new Memo { UserId = UserId, Section = "work", Content = "挂载目标" };
            db.Memos.Add(memo);
            await db.SaveChangesAsync();
            memoId = memo.Id;
        }

        await using var db2 = _fx.CreateContext();
        var result = await MiraiSessionRules.ValidateAsync("context", "memo", memoId, db2, UserId);
        Assert.Equal(("context", "memo", memoId), result);
    }

    // ===== 对象快照 =====

    [Fact]
    public async Task BuildSnapshot_WorkLog_ContainsCoreFields()
    {
        int workLogId;
        await using (var db = _fx.CreateContext())
        {
            var w = new WorkLog
            {
                UserId = UserId, Title = "安全评审要求", Content = "重构方案需过安全评审。",
                Tags = "重构方案", Category = "研发", LogDate = DateTime.UtcNow.Date
            };
            db.WorkLogs.Add(w);
            await db.SaveChangesAsync();
            workLogId = w.Id;
        }

        var provider = new MiraiContextProvider(_fx.CreateContext());
        var snapshot = await provider.BuildSnapshotAsync(UserId, "worklog", workLogId);

        Assert.NotNull(snapshot);
        Assert.Contains("安全评审要求", snapshot);
        Assert.Contains("研发", snapshot);
        Assert.StartsWith("【当前挂载对象】", snapshot);
    }

    [Fact]
    public async Task BuildSnapshot_DeletedObject_ReturnsNull()
    {
        int memoId;
        await using (var db = _fx.CreateContext())
        {
            var memo = new Memo { UserId = UserId, Section = "work", Content = "将被删除" };
            db.Memos.Add(memo);
            await db.SaveChangesAsync();
            memoId = memo.Id;
            memo.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var provider = new MiraiContextProvider(_fx.CreateContext());
        Assert.Null(await provider.BuildSnapshotAsync(UserId, "memo", memoId));
    }

    [Fact]
    public async Task BuildSnapshot_OtherUsersObject_ReturnsNull()
    {
        var otherUserId = _fx.SeedAnotherUser();
        int memoId;
        await using (var db = _fx.CreateContext())
        {
            var memo = new Memo { UserId = otherUserId, Section = "work", Content = "他人数据" };
            db.Memos.Add(memo);
            await db.SaveChangesAsync();
            memoId = memo.Id;
        }

        var provider = new MiraiContextProvider(_fx.CreateContext());
        Assert.Null(await provider.BuildSnapshotAsync(UserId, "memo", memoId));
    }
}

/// <summary>文件存储布局（任务卡 §6）：ExportsRoot/TempRoot 回落规则与 export_file 落点。</summary>
public class FileStorageTests
{
    [Fact]
    public void ExportsRoot_FallsBackToSiblingOfWorkspace()
    {
        var options = new FileSystemOptions { WorkspaceRoot = @"D:\fileservice\workspace" };
        Assert.Equal(@"D:\fileservice\exports", MiraiFileStorage.ExportsRoot(options));
        Assert.Equal(@"D:\fileservice\temp", MiraiFileStorage.TempRoot(options));
    }

    [Fact]
    public void ExplicitRoots_WinOverFallback()
    {
        var options = new FileSystemOptions
        {
            WorkspaceRoot = @"D:\fileservice\workspace",
            ExportsRoot = @"E:\exports",
            TempRoot = @"E:\temp"
        };
        Assert.Equal(@"E:\exports", MiraiFileStorage.ExportsRoot(options));
        Assert.Equal(@"E:\temp", MiraiFileStorage.TempRoot(options));
    }

    [Fact]
    public async Task ExportFile_WritesToExportsYearMonthLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "mirai-test-exports-" + Guid.NewGuid().ToString("N"));
        var tool = new ServerExportFileTool(Options.Create(new FileSystemOptions { ExportsRoot = root }));

        var json = await tool.ExecuteAsync(7, """
            {"filename":"报告.txt","content":"hello mirai"}
            """);

        // 落点：exports\{userId}\yyyy\MM\，文件名带时间戳
        var now = DateTime.UtcNow;
        var expectedDir = Path.Combine(root, "7", now.ToString("yyyy"), now.ToString("MM"));
        var file = Directory.EnumerateFiles(expectedDir).Single();
        Assert.EndsWith("_报告.txt", Path.GetFileName(file));
        Assert.Equal("hello mirai", await File.ReadAllTextAsync(file));

        // 返回鉴权下载 URL（不再走静态 uploads 目录）
        Assert.Contains("/api/v1/mirai/exports/7/", json);
        Directory.Delete(root, recursive: true);
    }
}
