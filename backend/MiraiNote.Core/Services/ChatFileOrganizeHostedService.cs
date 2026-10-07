using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MiraiNote.Core.Services;

/// <summary>
/// 启动时把各用户私有区根目录里已经生成的散落文件归入 generated/archive。
/// 每个用户只整理一次，进行中的新文件不会被反复搬走。
/// </summary>
public sealed class ChatFileOrganizeHostedService : IHostedService
{
    private readonly ChatFileLibrary _library;
    private readonly ILogger<ChatFileOrganizeHostedService> _logger;

    public ChatFileOrganizeHostedService(
        ChatFileLibrary library,
        ILogger<ChatFileOrganizeHostedService> logger)
    {
        _library = library;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var moved = _library.OrganizeAllUsers(DateTime.UtcNow);
            if (moved > 0)
                _logger.LogInformation("已整理 {Count} 个散落在工作区根目录的文件", moved);
        }
        catch (Exception ex)
        {
            _logger.LogError("整理工作区文件失败，类型 {ExceptionType}", ex.GetType().Name);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
