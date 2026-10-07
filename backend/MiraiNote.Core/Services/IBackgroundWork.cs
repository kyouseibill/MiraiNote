using Microsoft.Extensions.Logging;

namespace MiraiNote.Core.Services;

/// <summary>
/// 请求返回前不等待的工作。验证邮件重发用它，避免 SMTP 耗时暴露邮箱是否存在。
/// </summary>
public interface IBackgroundWork
{
    void Run(Func<Task> work);
}

public sealed class BackgroundWork : IBackgroundWork
{
    private readonly ILogger<BackgroundWork> _logger;

    public BackgroundWork(ILogger<BackgroundWork> logger) => _logger = logger;

    public void Run(Func<Task> work)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception)
            {
                _logger.LogError("后台任务失败");
            }
        });
    }
}
