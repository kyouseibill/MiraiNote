using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiraiNote.Core.Services.Mirai;
using MiraiNote.Core.Services.Tools;

namespace MiraiNote.Core.Services;

/// <summary>
/// temp 目录每日清理后台服务（Mirai M1）。
/// temp 存放即弃文件（Chat 附件解析中间产物、M2 语音转写缓存），
/// 超过 48 小时未修改的文件与空目录每日删除一次。
/// </summary>
public class TempCleanupBackgroundService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FileMaxAge = TimeSpan.FromHours(48);

    private readonly IServiceProvider _services;
    private readonly ILogger<TempCleanupBackgroundService> _logger;

    public TempCleanupBackgroundService(
        IServiceProvider services, ILogger<TempCleanupBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("temp 目录清理服务已启动，扫描周期：{Interval}", CleanupInterval);

        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "temp 目录清理出现异常，将在下一周期重试");
            }

            try { await Task.Delay(CleanupInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CleanupOnceAsync(CancellationToken ct)
    {
        var root = ResolveTempRoot();
        if (!Directory.Exists(root)) return;

        var cutoffUtc = DateTime.UtcNow - FileMaxAge;
        var deletedFiles = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                {
                    File.Delete(file);
                    deletedFiles++;
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning("temp 文件删除失败（{File}）：{Message}", file, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("temp 文件无权限删除（{File}）：{Message}", file, ex.Message);
            }
        }

        // 自底向上清理空目录（根目录本身保留）
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) continue;
                Directory.Delete(dir);
            }
            catch (IOException)
            {
                // 并发写入导致的竞态：留待下一周期
            }
        }

        if (deletedFiles > 0)
            _logger.LogInformation("temp 目录清理完成：删除 {Count} 个过期文件", deletedFiles);

        await Task.CompletedTask;
    }

    private string ResolveTempRoot()
    {
        using var scope = _services.CreateScope();
        var fsOptions = scope.ServiceProvider.GetRequiredService<IOptions<FileSystemOptions>>();
        return MiraiFileStorage.TempRoot(fsOptions.Value);
    }
}
