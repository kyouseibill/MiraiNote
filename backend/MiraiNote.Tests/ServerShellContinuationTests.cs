using Microsoft.Extensions.Options;
using MiraiNote.Core.Services;
using MiraiNote.Core.Services.Tools;
using System.Text.Json;
using System.Diagnostics;
using Xunit;

namespace MiraiNote.Tests;

public class ServerShellContinuationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mirai-shell-" + Guid.NewGuid().ToString("N"));
    private ServerShellTool CreateTool() => new(Options.Create(new FileSystemOptions { WorkspaceRoot = _root, AllowShell = true }));

    [Fact]
    public async Task LongCommandCanCompletePastThirtySeconds()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var result = await CreateTool().ExecuteAsync(1, JsonSerializer.Serialize(new
        { command = "powershell -NoProfile -Command \"Start-Sleep -Seconds 31; Write-Output 'long-task-finished'\"" }), cts.Token);
        Assert.Contains("long-task-finished", result);
    }

    [Fact]
    public async Task TimeoutPreservesPartialOutput()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await CreateTool().ExecuteAsync(1, JsonSerializer.Serialize(new
        { command = "echo checkpoint & powershell -NoProfile -Command \"Start-Sleep -Seconds 10\"", timeout_seconds = 1 }));
        Assert.Contains("超时", result);
        Assert.Contains("checkpoint", result);
    }

    [Fact]
    public async Task ChineseOutputUsesUtf8()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await CreateTool().ExecuteAsync(1, JsonSerializer.Serialize(new { command = "echo 职位匹配完成" }));
        Assert.Contains("职位匹配完成", result);
    }

    [Fact]
    public async Task CancellationStopsChildProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var execution = CreateTool().ExecuteAsync(1, JsonSerializer.Serialize(new
        { command = "powershell -NoProfile -Command \"$PID | Out-File -Encoding ascii child.pid; Start-Sleep -Seconds 30\"" }), cts.Token);
        var pidPath = Path.Combine(_root, "users", "1", "child.pid");
        string? pidText = null;
        while (string.IsNullOrWhiteSpace(pidText))
        {
            Assert.False(execution.IsCompleted, execution.IsCompletedSuccessfully ? await execution : "Shell exited before writing child PID");
            await Task.Delay(50, cts.Token);
            if (File.Exists(pidPath)) pidText = await File.ReadAllTextAsync(pidPath, cts.Token);
        }
        var pid = int.Parse(pidText);
        using var child = Process.GetProcessById(pid);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await child.WaitForExitAsync(exitTimeout.Token);
        Assert.True(child.HasExited);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public async Task InvalidTimeoutNeverExecutesCommand(int timeout)
    {
        var result = await CreateTool().ExecuteAsync(1, JsonSerializer.Serialize(new
        { command = "echo should-not-run", timeout_seconds = timeout }));
        Assert.Contains("timeout_seconds", result);
        Assert.DoesNotContain("should-not-run", result);
        Assert.False(Directory.Exists(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
